using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AI_Chat.Models;
using AI_Chat.Services;
using AI_Chat.Managers;
using Newtonsoft.Json;

namespace AI_Chat.Plugins
{
    internal class PriorityHandler<TContext, TResult>
    {
        public int Priority { get; set; }
        public Func<TContext, TResult> Handler { get; set; }
        public string PluginId { get; set; }
    }

    public class PluginApi : IPluginApi
    {
        private readonly ConfigManager _configManager;
        private readonly UserSessionManager _sessionManager;
        private readonly LLMService _llmService;
        private readonly WebSocketClient _webSocketClient;
        private readonly IPluginManager _pluginManager;

        public static event Action<ControlPanelConfig> OnConfigChanged;

        private readonly List<PriorityHandler<PreMergeMessageContext, PreMergeMessageResult>> _preMergeHandlers = new List<PriorityHandler<PreMergeMessageContext, PreMergeMessageResult>>();
        private readonly List<PriorityHandler<PostMergeMessageContext, PostMergeMessageResult>> _postMergeHandlers = new List<PriorityHandler<PostMergeMessageContext, PostMergeMessageResult>>();
        private readonly List<PriorityHandler<MessageAppendedContext, MessageAppendedResult>> _messageAppendedHandlers = new List<PriorityHandler<MessageAppendedContext, MessageAppendedResult>>();
        private readonly List<PriorityHandler<LLMResponseContext, LLMResponseResult>> _llmResponseHandlers = new List<PriorityHandler<LLMResponseContext, LLMResponseResult>>();
        private readonly List<PriorityHandler<PreLLMRequestContext, PreLLMRequestResult>> _preLLMRequestHandlers = new List<PriorityHandler<PreLLMRequestContext, PreLLMRequestResult>>();
        private readonly List<PriorityHandler<GroupMessageContext, GroupMessageResult>> _groupMessageHandlers = new List<PriorityHandler<GroupMessageContext, GroupMessageResult>>();

        private readonly Dictionary<string, List<string>> _pluginPermissions = new Dictionary<string, List<string>>();

        public PluginApi(
            ConfigManager configManager,
            UserSessionManager sessionManager,
            LLMService llmService,
            WebSocketClient webSocketClient,
            IPluginManager pluginManager = null)
        {
            _configManager = configManager;
            _sessionManager = sessionManager;
            _llmService = llmService;
            _webSocketClient = webSocketClient;
            _pluginManager = pluginManager;
        }

        private string GetCurrentPluginId()
        {
            if (_pluginManager == null) return null;

            var stackTrace = new System.Diagnostics.StackTrace();
            foreach (var frame in stackTrace.GetFrames())
            {
                var method = frame.GetMethod();
                var declaringType = method.DeclaringType;
                if (declaringType != null && typeof(IPlugin).IsAssignableFrom(declaringType))
                {
                    foreach (var plugin in _pluginManager.GetAllPlugins())
                    {
                        if (plugin.GetType() == declaringType)
                        {
                            return plugin.Id;
                        }
                    }
                }
            }
            return null;
        }

        private int GetCurrentPluginPriority()
        {
            if (_pluginManager == null) return 50;

            var stackTrace = new System.Diagnostics.StackTrace();
            foreach (var frame in stackTrace.GetFrames())
            {
                var method = frame.GetMethod();
                var declaringType = method.DeclaringType;
                if (declaringType != null && typeof(IPlugin).IsAssignableFrom(declaringType))
                {
                    foreach (var plugin in _pluginManager.GetAllPlugins())
                    {
                        if (plugin.GetType() == declaringType)
                        {
                            var pluginInfo = _pluginManager.GetPluginInfo(plugin.Id);
                            return pluginInfo?.Priority ?? 50;
                        }
                    }
                }
            }
            return 50;
        }

        #region 1. 合并前用户消息接口

        public void RegisterPreMergeMessageHandler(Func<PreMergeMessageContext, PreMergeMessageResult> handler)
        {
            if (handler != null)
            {
                string pluginId = GetCurrentPluginId();
                _preMergeHandlers.Add(new PriorityHandler<PreMergeMessageContext, PreMergeMessageResult>
                {
                    PluginId = pluginId,
                    Priority = GetCurrentPluginPriority(),
                    Handler = handler
                });
                _preMergeHandlers.Sort((a, b) => a.Priority.CompareTo(b.Priority));
                string permission = "注册合并前消息处理器（可拦截/修改用户输入）";
                AddPermissionToPlugin(pluginId, permission);
            }
        }

        internal void UnregisterPreMergeMessageHandlers(string pluginId)
        {
            if (!string.IsNullOrEmpty(pluginId))
            {
                _preMergeHandlers.RemoveAll(h => h.PluginId == pluginId);
            }
        }

        internal PreMergeMessageResult HandlePreMergeMessage(PreMergeMessageContext context)
        {
            string modifiedMessage = context.RawMessage;
            bool isModified = false;

            foreach (var handlerWrapper in _preMergeHandlers)
            {
                try
                {
                    context.RawMessage = modifiedMessage;
                    var result = handlerWrapper.Handler(context);

                    if (result?.IsIntercepted == true)
                    {
                        return new PreMergeMessageResult
                        {
                            IsIntercepted = true,
                            Response = result.Response,
                            ModifiedMessage = modifiedMessage,
                            IsModified = isModified
                        };
                    }

                    if (result?.IsModified == true)
                    {
                        modifiedMessage = result.ModifiedMessage;
                        isModified = true;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError("PluginApi", $"Pre-merge message handler execution failed: {ex.Message}", ex);
                }
            }

            return new PreMergeMessageResult
            {
                ModifiedMessage = modifiedMessage,
                IsModified = isModified
            };
        }

        #endregion

        #region 2. 合并后用户消息接口

        public void RegisterPostMergeMessageHandler(Func<PostMergeMessageContext, PostMergeMessageResult> handler)
        {
            if (handler != null)
            {
                string pluginId = GetCurrentPluginId();
                _postMergeHandlers.Add(new PriorityHandler<PostMergeMessageContext, PostMergeMessageResult>
                {
                    PluginId = pluginId,
                    Priority = GetCurrentPluginPriority(),
                    Handler = handler
                });
                _postMergeHandlers.Sort((a, b) => a.Priority.CompareTo(b.Priority));
                string permission = "注册合并后消息处理器（可拦截/修改完整消息）";
                AddPermissionToPlugin(pluginId, permission);
            }
        }

        internal void UnregisterPostMergeMessageHandlers(string pluginId)
        {
            if (!string.IsNullOrEmpty(pluginId))
            {
                _postMergeHandlers.RemoveAll(h => h.PluginId == pluginId);
            }
        }

        internal PostMergeMessageResult HandlePostMergeMessage(PostMergeMessageContext context)
        {
            string modifiedMessage = context.FullMessage;
            bool isModified = false;

            foreach (var handlerWrapper in _postMergeHandlers)
            {
                try
                {
                    context.FullMessage = modifiedMessage;
                    var result = handlerWrapper.Handler(context);

                    if (result?.IsIntercepted == true)
                    {
                        return new PostMergeMessageResult
                        {
                            IsIntercepted = true,
                            Response = result.Response,
                            ModifiedMessage = modifiedMessage,
                            IsModified = isModified
                        };
                    }

                    if (result?.IsModified == true)
                    {
                        modifiedMessage = result.ModifiedMessage;
                        isModified = true;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError("PluginApi", $"Post-merge message handler execution failed: {ex.Message}", ex);
                }
            }

            return new PostMergeMessageResult
            {
                ModifiedMessage = modifiedMessage,
                IsModified = isModified
            };
        }

        #endregion

        #region 2.5 消息追加完成接口

        public void RegisterMessageAppendedHandler(Func<MessageAppendedContext, MessageAppendedResult> handler)
        {
            if (handler != null)
            {
                string pluginId = GetCurrentPluginId();
                _messageAppendedHandlers.Add(new PriorityHandler<MessageAppendedContext, MessageAppendedResult>
                {
                    PluginId = pluginId,
                    Priority = GetCurrentPluginPriority(),
                    Handler = handler
                });
                _messageAppendedHandlers.Sort((a, b) => a.Priority.CompareTo(b.Priority));
                string permission = "注册消息追加完成处理器（可修改追加后的消息）";
                AddPermissionToPlugin(pluginId, permission);
            }
        }

        internal void UnregisterMessageAppendedHandlers(string pluginId)
        {
            if (!string.IsNullOrEmpty(pluginId))
            {
                _messageAppendedHandlers.RemoveAll(h => h.PluginId == pluginId);
            }
        }

        internal MessageAppendedResult HandleMessageAppended(MessageAppendedContext context)
        {
            string modifiedMessage = context.FullMessage;
            bool isModified = false;

            foreach (var handlerWrapper in _messageAppendedHandlers)
            {
                try
                {
                    context.FullMessage = modifiedMessage;
                    var result = handlerWrapper.Handler(context);
                    
                    if (result?.IsIntercepted == true)
                    {
                        return new MessageAppendedResult
                        {
                            IsIntercepted = true,
                            Response = result.Response,
                            ModifiedMessage = modifiedMessage,
                            IsModified = isModified
                        };
                    }
                    
                    if (result?.IsModified == true)
                    {
                        modifiedMessage = result.ModifiedMessage;
                        isModified = true;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError("PluginApi", $"Message appended handler execution failed: {ex.Message}", ex);
                }
            }

            return new MessageAppendedResult
            {
                ModifiedMessage = modifiedMessage,
                IsModified = isModified
            };
        }

        #endregion

        #region 3. 大模型回复消息接口

        public void RegisterLLMResponseHandler(Func<LLMResponseContext, LLMResponseResult> handler)
        {
            if (handler != null)
            {
                string pluginId = GetCurrentPluginId();
                _llmResponseHandlers.Add(new PriorityHandler<LLMResponseContext, LLMResponseResult>
                {
                    PluginId = pluginId,
                    Priority = GetCurrentPluginPriority(),
                    Handler = handler
                });
                _llmResponseHandlers.Sort((a, b) => a.Priority.CompareTo(b.Priority));
                string permission = "注册LLM响应处理器（可拦截/修改AI回复）";
                AddPermissionToPlugin(pluginId, permission);
            }
        }

        internal void UnregisterLLMResponseHandlers(string pluginId)
        {
            if (!string.IsNullOrEmpty(pluginId))
            {
                _llmResponseHandlers.RemoveAll(h => h.PluginId == pluginId);
            }
        }

        internal LLMResponseResult HandleLLMResponse(LLMResponseContext context)
        {
            bool anyModified = false;
            string currentResponse = context.RawResponse;

            foreach (var handlerWrapper in _llmResponseHandlers)
            {
                try
                {
                    var handlerContext = new LLMResponseContext
                    {
                        RawResponse = currentResponse,
                        RequestId = context.RequestId
                    };

                    var result = handlerWrapper.Handler(handlerContext);

                    if (result?.IsIntercepted == true)
                    {
                        return result;
                    }

                    if (result?.IsModified == true && !string.IsNullOrEmpty(result.AlternativeResponse))
                    {
                        currentResponse = result.AlternativeResponse;
                        anyModified = true;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError("PluginApi", $"LLM response handler execution failed: {ex.Message}", ex);
                }
            }

            if (anyModified)
            {
                return new LLMResponseResult
                {
                    IsModified = true,
                    AlternativeResponse = currentResponse
                };
            }

            return new LLMResponseResult { IsIntercepted = false, IsModified = false };
        }

        #endregion

        #region 3.5 LLM请求前处理器接口

        public void RegisterPreLLMRequestHandler(Func<PreLLMRequestContext, PreLLMRequestResult> handler)
        {
            if (handler != null)
            {
                string pluginId = GetCurrentPluginId();
                _preLLMRequestHandlers.Add(new PriorityHandler<PreLLMRequestContext, PreLLMRequestResult>
                {
                    PluginId = pluginId,
                    Priority = GetCurrentPluginPriority(),
                    Handler = handler
                });
                _preLLMRequestHandlers.Sort((a, b) => a.Priority.CompareTo(b.Priority));
                string permission = "注册LLM请求前处理器（可修改请求内容）";
                AddPermissionToPlugin(pluginId, permission);
            }
        }

        internal void UnregisterPreLLMRequestHandlers(string pluginId)
        {
            if (!string.IsNullOrEmpty(pluginId))
            {
                _preLLMRequestHandlers.RemoveAll(h => h.PluginId == pluginId);
            }
        }

        internal PreLLMRequestResult HandlePreLLMRequest(PreLLMRequestContext context)
        {
            string modifiedRequestJson = context.RequestJson;
            bool isModified = false;

            foreach (var handlerWrapper in _preLLMRequestHandlers)
            {
                try
                {
                    context.RequestJson = modifiedRequestJson;
                    var result = handlerWrapper.Handler(context);

                    if (result?.IsIntercepted == true)
                    {
                        return new PreLLMRequestResult
                        {
                            IsIntercepted = true,
                            InterceptedResponse = result.InterceptedResponse,
                            ModifiedRequestJson = modifiedRequestJson,
                            IsModified = isModified
                        };
                    }

                    if (result?.IsModified == true)
                    {
                        modifiedRequestJson = result.ModifiedRequestJson;
                        isModified = true;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError("PluginApi", $"Pre-LLM request handler execution failed: {ex.Message}", ex);
                }
            }

            return new PreLLMRequestResult
            {
                ModifiedRequestJson = modifiedRequestJson,
                IsModified = isModified
            };
        }

        #endregion

        #region 4. 大模型请求接口

        public async Task<string> RequestLLMAsync(string requestJson)
        {
            if (string.IsNullOrWhiteSpace(requestJson))
            {
                return JsonConvert.SerializeObject(new { error = "请求JSON不能为空" });
            }

            try
            {
                var response = await _llmService.SendRequestRawAsync(requestJson);
                return response;
            }
            catch (Exception ex)
            {
                Logger.LogError("PluginApi", $"LLM request failed: {ex.Message}", ex);
                return JsonConvert.SerializeObject(new { error = ex.Message });
            }
        }

        #endregion

        #region 5-6. 配置接口

        public AppConfig GetConfig()
        {
            return new AppConfig
            {
                ApiKey = _configManager.Config.LlmApiKey,
                ApiUrl = _configManager.Config.LlmApiBaseUrl,
                Model = _configManager.Config.LlmModelName,
                Temperature = (float)_configManager.Config.LlmTemperature,
                MaxTokens = _configManager.Config.LlmMaxTokens,
                TopP = (float)_configManager.Config.LlmTopP,
                WebsocketServerUri = _configManager.Config.WebsocketServerUri,
                WebsocketToken = _configManager.Config.WebsocketToken,
                WebsocketKeepAliveInterval = _configManager.Config.WebsocketKeepAliveInterval,
                MaxContextRounds = _configManager.Config.MaxContextRounds,
                RoleCardsApiUrl = _configManager.Config.RoleCardsApiUrl
            };
        }

        public void SetConfig(AppConfig config)
        {
            if (config == null) return;

            _configManager.Config.LlmApiKey = config.ApiKey;
            _configManager.Config.LlmApiBaseUrl = config.ApiUrl;
            _configManager.Config.LlmModelName = config.Model;
            _configManager.Config.LlmTemperature = config.Temperature;
            _configManager.Config.LlmMaxTokens = config.MaxTokens;
            _configManager.Config.LlmTopP = config.TopP;
            _configManager.Config.WebsocketServerUri = config.WebsocketServerUri;
            _configManager.Config.WebsocketToken = config.WebsocketToken;
            _configManager.Config.WebsocketKeepAliveInterval = config.WebsocketKeepAliveInterval;
            _configManager.Config.MaxContextRounds = config.MaxContextRounds;
            _configManager.Config.RoleCardsApiUrl = config.RoleCardsApiUrl;

            _configManager.SaveConfig();

            OnConfigChanged?.Invoke(_configManager.Config);
        }

        public T GetConfigValue<T>(string key, T defaultValue = default)
        {
            try
            {
                var property = typeof(ControlPanelConfig).GetProperty(key);
                if (property != null)
                {
                    var value = property.GetValue(_configManager.Config);
                    if (value is T tValue)
                    {
                        return tValue;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("PluginApi", $"Failed to get config value: {key}, {ex.Message}");
            }
            return defaultValue;
        }

        public void SetConfigValue<T>(string key, T value)
        {
            try
            {
                var property = typeof(ControlPanelConfig).GetProperty(key);
                if (property != null && property.CanWrite)
                {
                    property.SetValue(_configManager.Config, value);
                    _configManager.SaveConfig();
                    OnConfigChanged?.Invoke(_configManager.Config);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("PluginApi", $"Failed to set config value: {key}, {ex.Message}");
            }
        }

        #endregion

        #region 7. 发送消息接口

        public async Task<bool> SendMessageAsync(long userId, string message, SendMessageOptions options = null)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            options = options ?? new SendMessageOptions();

            try
            {
                object payload = null;

                switch (options.MessageType)
                {
                    case MessageType.Text:
                        payload = new
                        {
                            action = "send_msg",
                            @params = new
                            {
                                user_id = userId,
                                message = message
                            }
                        };
                        break;

                    case MessageType.Image:
                        var imagePath = message.StartsWith("file://") ? message : $"file://{message}";
                        payload = new
                        {
                            action = "send_msg",
                            @params = new
                            {
                                user_id = userId,
                                message = new[]
                                {
                                    new { type = "image", data = new { file = imagePath } }
                                }
                            }
                        };
                        break;

                    case MessageType.Voice:
                        var voicePath = message.StartsWith("file://") ? message : $"file://{message}";
                        payload = new
                        {
                            action = "send_msg",
                            @params = new
                            {
                                user_id = userId,
                                message = new[]
                                {
                                    new { type = "record", data = new { file = voicePath } }
                                }
                            }
                        };
                        break;
                }

                if (payload != null)
                {
                    var json = JsonConvert.SerializeObject(payload);
                    await _webSocketClient.SendMessageAsync(json);

                    var chatHistoryManager = _sessionManager.GetOrCreateChatHistoryManager(userId);
                    chatHistoryManager.AddMessage("plugin", message);

                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("PluginApi", $"Failed to send message: {ex.Message}", ex);
            }

            return false;
        }

        #endregion

        #region 8. 上下文接口

        public List<ContextMessage> GetFullContext(long userId)
        {
            var context = new List<ContextMessage>();

            try
            {
                var contextManager = _sessionManager.GetOrCreateContextManager(userId);
                var systemContext = contextManager.Context;
                foreach (var msg in systemContext)
                {
                    context.Add(new ContextMessage
                    {
                        Role = msg.Role,
                        Content = msg.Content?.ToString(),
                        Timestamp = DateTime.Now
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("PluginApi", $"Failed to get context for user {userId}: {ex.Message}", ex);
            }

            return context;
        }

        #endregion

        #region 9. 上下文写入接口

        public void AddContextMessage(long userId, string role, string content)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(role) || string.IsNullOrWhiteSpace(content))
                {
                    Logger.LogWarning("PluginApi", "Failed to add context message: role or content cannot be empty");
                    return;
                }

                role = NormalizeRole(role);
                var contextManager = _sessionManager.GetOrCreateContextManager(userId);

                switch (role)
                {
                    case "system":
                        contextManager.AddSystemMessage(content);
                        break;
                    case "user":
                        contextManager.AddUserMessage(content, out _);
                        break;
                    case "assistant":
                        contextManager.AddAssistantMessage(content);
                        break;
                    default:
                        contextManager.AddSystemMessage(content);
                        break;
                }

                Logger.LogInfo("PluginApi", $"Plugin added context message successfully: role={role}, user={userId}");
            }
            catch (Exception ex)
            {
                Logger.LogError("PluginApi", $"Failed to add context message: {ex.Message}", ex);
            }
        }

        public void ClearContext(long userId)
        {
            try
            {
                var contextManager = _sessionManager.GetOrCreateContextManager(userId);
                contextManager.ClearContext();
                
                var chatHistoryManager = _sessionManager.GetOrCreateChatHistoryManager(userId);
                chatHistoryManager.ClearHistory();
                
                Logger.LogInfo("PluginApi", $"Plugin cleared context successfully for user {userId}");
            }
            catch (Exception ex)
            {
                Logger.LogError("PluginApi", $"Failed to clear context: {ex.Message}", ex);
            }
        }

        public int RemoveLastMessages(long userId, string role, int count)
        {
            try
            {
                if (count <= 0) return 0;

                role = NormalizeRole(role);
                var contextManager = _sessionManager.GetOrCreateContextManager(userId);
                int removed = 0;
                var context = contextManager.Context;
                if (context == null || context.Count == 0) return 0;

                for (int i = context.Count - 1; i >= 0 && removed < count; i--)
                {
                    if (context[i].Role.Equals(role, StringComparison.OrdinalIgnoreCase))
                    {
                        context.RemoveAt(i);
                        removed++;
                    }
                }

                if (removed > 0)
                {
                    contextManager.SaveContextToDisk();
                }

                Logger.LogInfo("PluginApi", $"Plugin removed context messages successfully: role={role}, count={removed}, user={userId}");
                return removed;
            }
            catch (Exception ex)
            {
                Logger.LogError("PluginApi", $"Failed to remove context messages: {ex.Message}", ex);
                return 0;
            }
        }

        private string NormalizeRole(string role)
        {
            string lowerRole = role.ToLower();
            if (lowerRole == "user" || lowerRole == "用户")
                return "user";
            if (lowerRole == "assistant" || lowerRole == "ai" || lowerRole == "模型" || lowerRole == "助手")
                return "assistant";
            if (lowerRole == "system" || lowerRole == "系统")
                return "system";
            return lowerRole;
        }

        #endregion

        #region 10. 权限相关接口

        private void AddPermissionToPlugin(string pluginId, string permission)
        {
            if (string.IsNullOrEmpty(pluginId)) return;

            lock (_pluginPermissions)
            {
                if (!_pluginPermissions.ContainsKey(pluginId))
                {
                    _pluginPermissions[pluginId] = new List<string>();
                }
                if (!_pluginPermissions[pluginId].Contains(permission))
                {
                    _pluginPermissions[pluginId].Add(permission);
                }
            }
        }

        public List<string> GetRegisteredPermissions()
        {
            string currentPluginId = GetCurrentPluginId();
            return GetPluginPermissions(currentPluginId);
        }

        public List<string> GetPluginPermissions(string pluginId)
        {
            if (string.IsNullOrEmpty(pluginId))
            {
                return new List<string> { "基础插件功能（无特殊权限）" };
            }

            lock (_pluginPermissions)
            {
                if (_pluginPermissions.TryGetValue(pluginId, out var permissions))
                {
                    return new List<string>(permissions);
                }
            }

            return new List<string> { "基础插件功能（无特殊权限）" };
        }

        public Dictionary<string, List<string>> GetAllPluginPermissions()
        {
            lock (_pluginPermissions)
            {
                return new Dictionary<string, List<string>>(_pluginPermissions);
            }
        }

        internal void UnregisterPluginPermissions(string pluginId)
        {
            if (string.IsNullOrEmpty(pluginId)) return;

            lock (_pluginPermissions)
            {
                _pluginPermissions.Remove(pluginId);
            }
        }

        #endregion

        #region 11. 多用户管理接口

        public List<long> GetAllowedUserIds()
        {
            return new List<long>(_configManager.Config.AllowedUserIds);
        }

        public bool IsUserAllowed(long userId)
        {
            return _configManager.IsUserAllowed(userId);
        }

        public void AddAllowedUser(long userId)
        {
            _configManager.AddAllowedUser(userId);
        }

        public void RemoveAllowedUser(long userId)
        {
            _configManager.RemoveAllowedUser(userId);
        }

        #endregion

        #region 12. 群聊消息处理器接口

        public void RegisterGroupMessageHandler(Func<GroupMessageContext, GroupMessageResult> handler)
        {
            if (handler != null)
            {
                string pluginId = GetCurrentPluginId();
                _groupMessageHandlers.Add(new PriorityHandler<GroupMessageContext, GroupMessageResult>
                {
                    PluginId = pluginId,
                    Priority = GetCurrentPluginPriority(),
                    Handler = handler
                });
                _groupMessageHandlers.Sort((a, b) => a.Priority.CompareTo(b.Priority));
                string permission = "注册群聊消息处理器";
                AddPermissionToPlugin(pluginId, permission);
            }
        }

        internal void UnregisterGroupMessageHandlers(string pluginId)
        {
            if (!string.IsNullOrEmpty(pluginId))
            {
                _groupMessageHandlers.RemoveAll(h => h.PluginId == pluginId);
            }
        }

        internal GroupMessageResult HandleGroupMessage(GroupMessageContext context)
        {
            foreach (var handlerWrapper in _groupMessageHandlers)
            {
                try
                {
                    var result = handlerWrapper.Handler(context);

                    if (result?.IsHandled == true)
                    {
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError("PluginApi", $"Group message handler execution failed: {ex.Message}", ex);
                }
            }

            return new GroupMessageResult { IsHandled = false };
        }

        public List<long> GetAllowedGroupIds()
        {
            return new List<long>(_configManager.Config.AllowedGroupIds);
        }

        public bool IsGroupAllowed(long groupId)
        {
            return _configManager.IsGroupAllowed(groupId);
        }

        public void AddAllowedGroup(long groupId)
        {
            _configManager.AddAllowedGroup(groupId);
        }

        public void RemoveAllowedGroup(long groupId)
        {
            _configManager.RemoveAllowedGroup(groupId);
        }

        public async Task<bool> SendGroupMessageAsync(long groupId, string message, SendMessageOptions options = null)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            options = options ?? new SendMessageOptions();

            try
            {
                object payload = null;

                switch (options.MessageType)
                {
                    case MessageType.Text:
                        payload = new
                        {
                            action = "send_group_msg",
                            @params = new
                            {
                                group_id = groupId,
                                message = message
                            }
                        };
                        break;

                    case MessageType.Image:
                        var imagePath = message.StartsWith("file://") ? message : $"file://{message}";
                        payload = new
                        {
                            action = "send_group_msg",
                            @params = new
                            {
                                group_id = groupId,
                                message = new[]
                                {
                                    new { type = "image", data = new { file = imagePath } }
                                }
                            }
                        };
                        break;

                    case MessageType.Voice:
                        var voicePath = message.StartsWith("file://") ? message : $"file://{message}";
                        payload = new
                        {
                            action = "send_group_msg",
                            @params = new
                            {
                                group_id = groupId,
                                message = new[]
                                {
                                    new { type = "record", data = new { file = voicePath } }
                                }
                            }
                        };
                        break;
                }

                if (payload != null)
                {
                    var json = JsonConvert.SerializeObject(payload);
                    await _webSocketClient.SendMessageAsync(json);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("PluginApi", $"Failed to send group message: {ex.Message}", ex);
            }

            return false;
        }

        #endregion
    }
}
