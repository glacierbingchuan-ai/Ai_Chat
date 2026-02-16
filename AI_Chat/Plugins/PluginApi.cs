using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AI_Chat.Models;
using AI_Chat.Services;
using Newtonsoft.Json;

namespace AI_Chat.Plugins
{
    /// <summary>
    /// 带优先级的处理器包装类
    /// </summary>
    internal class PriorityHandler<TContext, TResult>
    {
        public int Priority { get; set; }
        public Func<TContext, TResult> Handler { get; set; }
        public string PluginId { get; set; }
    }

    /// <summary>
    /// 插件API实现类
    /// </summary>
    public class PluginApi : IPluginApi
    {
        private readonly ConfigManager _configManager;
        private readonly ContextManager _contextManager;
        private readonly LLMService _llmService;
        private readonly WebSocketClient _webSocketClient;
        private readonly ChatHistoryManager _chatHistoryManager;
        private readonly IPluginManager _pluginManager;

        /// <summary>
        /// 配置变更事件（用于通知前端刷新）
        /// </summary>
        public static event Action<AppConfig> OnConfigChanged;

        // 消息处理器（带优先级）
        private readonly List<PriorityHandler<PreMergeMessageContext, PreMergeMessageResult>> _preMergeHandlers = new List<PriorityHandler<PreMergeMessageContext, PreMergeMessageResult>>();
        private readonly List<PriorityHandler<PostMergeMessageContext, PostMergeMessageResult>> _postMergeHandlers = new List<PriorityHandler<PostMergeMessageContext, PostMergeMessageResult>>();
        private readonly List<PriorityHandler<MessageAppendedContext, MessageAppendedResult>> _messageAppendedHandlers = new List<PriorityHandler<MessageAppendedContext, MessageAppendedResult>>();
        private readonly List<PriorityHandler<LLMResponseContext, LLMResponseResult>> _llmResponseHandlers = new List<PriorityHandler<LLMResponseContext, LLMResponseResult>>();

        // 权限记录（按插件ID区分）
        private readonly Dictionary<string, List<string>> _pluginPermissions = new Dictionary<string, List<string>>();

        public PluginApi(
            ConfigManager configManager,
            ContextManager contextManager,
            LLMService llmService,
            WebSocketClient webSocketClient,
            ChatHistoryManager chatHistoryManager,
            IPluginManager pluginManager = null)
        {
            _configManager = configManager;
            _contextManager = contextManager;
            _llmService = llmService;
            _webSocketClient = webSocketClient;
            _chatHistoryManager = chatHistoryManager;
            _pluginManager = pluginManager;
        }

        /// <summary>
        /// 获取当前调用插件的ID
        /// </summary>
        private string GetCurrentPluginId()
        {
            if (_pluginManager == null) return null;

            // 通过调用堆栈查找插件类型
            var stackTrace = new System.Diagnostics.StackTrace();
            foreach (var frame in stackTrace.GetFrames())
            {
                var method = frame.GetMethod();
                var declaringType = method.DeclaringType;
                if (declaringType != null && typeof(IPlugin).IsAssignableFrom(declaringType))
                {
                    // 遍历所有插件，找到类型匹配的插件
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

        /// <summary>
        /// 获取当前调用插件的优先级
        /// </summary>
        private int GetCurrentPluginPriority()
        {
            if (_pluginManager == null) return 50; // 默认优先级

            // 通过调用堆栈查找插件类型
            var stackTrace = new System.Diagnostics.StackTrace();
            foreach (var frame in stackTrace.GetFrames())
            {
                var method = frame.GetMethod();
                var declaringType = method.DeclaringType;
                if (declaringType != null && typeof(IPlugin).IsAssignableFrom(declaringType))
                {
                    // 遍历所有插件，找到类型匹配的插件
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
            return 50; // 默认优先级
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
                // 按优先级排序（数值小的在前）
                _preMergeHandlers.Sort((a, b) => a.Priority.CompareTo(b.Priority));
                // 记录权限到对应插件
                string permission = "注册合并前消息处理器（可拦截/修改用户输入）";
                AddPermissionToPlugin(pluginId, permission);
            }
        }

        /// <summary>
        /// 注销指定插件的所有合并前消息处理器
        /// </summary>
        internal void UnregisterPreMergeMessageHandlers(string pluginId)
        {
            if (!string.IsNullOrEmpty(pluginId))
            {
                _preMergeHandlers.RemoveAll(h => h.PluginId == pluginId);
            }
        }

        /// <summary>
        /// 处理合并前消息（内部使用）
        /// </summary>
        internal PreMergeMessageResult HandlePreMergeMessage(PreMergeMessageContext context)
        {
            string modifiedMessage = context.RawMessage;
            bool isModified = false;

            foreach (var handlerWrapper in _preMergeHandlers)
            {
                try
                {
                    // 更新上下文中的消息为已修改的版本
                    context.RawMessage = modifiedMessage;
                    var result = handlerWrapper.Handler(context);

                    // 如果被拦截，直接返回
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

                    // 如果被修改，更新消息内容，继续让下一个插件处理
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
                // 按优先级排序（数值小的在前）
                _postMergeHandlers.Sort((a, b) => a.Priority.CompareTo(b.Priority));
                // 记录权限到对应插件
                string permission = "注册合并后消息处理器（可拦截/修改完整消息）";
                AddPermissionToPlugin(pluginId, permission);
            }
        }

        /// <summary>
        /// 注销指定插件的所有合并后消息处理器
        /// </summary>
        internal void UnregisterPostMergeMessageHandlers(string pluginId)
        {
            if (!string.IsNullOrEmpty(pluginId))
            {
                _postMergeHandlers.RemoveAll(h => h.PluginId == pluginId);
            }
        }

        /// <summary>
        /// 处理合并后消息（内部使用）
        /// </summary>
        internal PostMergeMessageResult HandlePostMergeMessage(PostMergeMessageContext context)
        {
            string modifiedMessage = context.FullMessage;
            bool isModified = false;

            foreach (var handlerWrapper in _postMergeHandlers)
            {
                try
                {
                    // 更新上下文中的消息为已修改的版本
                    context.FullMessage = modifiedMessage;
                    var result = handlerWrapper.Handler(context);

                    // 如果被拦截，直接返回
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

                    // 如果被修改，更新消息内容，继续让下一个插件处理
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
                // 按优先级排序（数值小的在前）
                _messageAppendedHandlers.Sort((a, b) => a.Priority.CompareTo(b.Priority));
                // 记录权限到对应插件
                string permission = "注册消息追加完成处理器（可修改追加后的消息）";
                AddPermissionToPlugin(pluginId, permission);
            }
        }

        /// <summary>
        /// 注销指定插件的所有消息追加完成处理器
        /// </summary>
        internal void UnregisterMessageAppendedHandlers(string pluginId)
        {
            if (!string.IsNullOrEmpty(pluginId))
            {
                _messageAppendedHandlers.RemoveAll(h => h.PluginId == pluginId);
            }
        }

        /// <summary>
        /// 处理消息追加完成（内部使用）
        /// </summary>
        internal MessageAppendedResult HandleMessageAppended(MessageAppendedContext context)
        {
            string modifiedMessage = context.FullMessage;
            bool isModified = false;

            foreach (var handlerWrapper in _messageAppendedHandlers)
            {
                try
                {
                    // 更新上下文中的消息为已修改的版本
                    context.FullMessage = modifiedMessage;
                    var result = handlerWrapper.Handler(context);
                    
                    // 检查是否被拦截
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
                // 按优先级排序（数值小的在前）
                _llmResponseHandlers.Sort((a, b) => a.Priority.CompareTo(b.Priority));
                // 记录权限到对应插件
                string permission = "注册LLM响应处理器（可拦截/修改AI回复）";
                AddPermissionToPlugin(pluginId, permission);
            }
        }

        /// <summary>
        /// 注销指定插件的所有LLM响应处理器
        /// </summary>
        internal void UnregisterLLMResponseHandlers(string pluginId)
        {
            if (!string.IsNullOrEmpty(pluginId))
            {
                _llmResponseHandlers.RemoveAll(h => h.PluginId == pluginId);
            }
        }

        /// <summary>
        /// 处理LLM响应（内部使用）
        /// </summary>
        internal LLMResponseResult HandleLLMResponse(LLMResponseContext context)
        {
            bool anyModified = false;
            string currentResponse = context.RawResponse;

            foreach (var handlerWrapper in _llmResponseHandlers)
            {
                try
                {
                    // 创建新的上下文，使用当前处理后的响应
                    var handlerContext = new LLMResponseContext
                    {
                        RawResponse = currentResponse,
                        RequestId = context.RequestId
                    };

                    var result = handlerWrapper.Handler(handlerContext);

                    // 如果被拦截，直接返回
                    if (result?.IsIntercepted == true)
                    {
                        return result;
                    }

                    // 如果被修改，更新当前响应，继续让下一个插件处理
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

            // 返回最终结果
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

        #region 4. 大模型请求接口

        public async Task<string> RequestLLMAsync(string requestJson)
        {
            if (string.IsNullOrWhiteSpace(requestJson))
            {
                return JsonConvert.SerializeObject(new { error = "请求JSON不能为空" });
            }

            try
            {
                // 直接使用LLMService发送请求
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
                // LLM 配置
                ApiKey = _configManager.Config.LlmApiKey,
                ApiUrl = _configManager.Config.LlmApiBaseUrl,
                Model = _configManager.Config.LlmModelName,
                Temperature = (float)_configManager.Config.LlmTemperature,
                MaxTokens = _configManager.Config.LlmMaxTokens,
                TopP = (float)_configManager.Config.LlmTopP,

                // WebSocket 配置
                WebsocketServerUri = _configManager.Config.WebsocketServerUri,
                WebsocketToken = _configManager.Config.WebsocketToken,
                WebsocketKeepAliveInterval = _configManager.Config.WebsocketKeepAliveInterval,

                // 功能开关
                IntentAnalysisEnabled = _configManager.Config.IntentAnalysisEnabled,
                ProactiveChatEnabled = _configManager.Config.ProactiveChatEnabled,
                ReminderEnabled = _configManager.Config.ReminderEnabled,

                // 聊天配置
                TargetUserId = _configManager.Config.TargetUserId,
                MaxContextRounds = _configManager.Config.MaxContextRounds,
                ActiveChatProbability = _configManager.Config.ActiveChatProbability,

                // 日志配置
                LogRootFolder = _configManager.Config.LogRootFolder,
                GeneralLogSubfolder = _configManager.Config.GeneralLogSubfolder,
                ContextLogSubfolder = _configManager.Config.ContextLogSubfolder,

                // 提示词配置
                BaseSystemPrompt = _configManager.Config.BaseSystemPrompt,
                IncompleteInputPrompt = _configManager.Config.IncompleteInputPrompt,

                // 角色卡配置
                RoleCardsApiUrl = _configManager.Config.RoleCardsApiUrl
            };
        }

        public void SetConfig(AppConfig config)
        {
            if (config == null) return;

            _configManager.Config.LlmApiKey = config.ApiKey;
            _configManager.Config.LlmApiBaseUrl = config.ApiUrl;
            _configManager.Config.LlmModelName = config.Model;
            _configManager.Config.TargetUserId = config.TargetUserId;
            _configManager.Config.IntentAnalysisEnabled = config.IntentAnalysisEnabled;
            _configManager.Config.ProactiveChatEnabled = config.ProactiveChatEnabled;
            _configManager.Config.ReminderEnabled = config.ReminderEnabled;
            _configManager.Config.MaxContextRounds = config.MaxContextRounds;
            _configManager.Config.LlmTemperature = config.Temperature;
            _configManager.Config.LlmMaxTokens = config.MaxTokens;
            _configManager.Config.ActiveChatProbability = config.ActiveChatProbability;

            _configManager.SaveConfig();
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

                    // 触发配置变更事件，通知前端刷新
                    OnConfigChanged?.Invoke(GetConfig());
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("PluginApi", $"Failed to set config value: {key}, {ex.Message}");
            }
        }

        #endregion

        #region 7. 发送消息接口

        public async Task<bool> SendMessageAsync(string message, SendMessageOptions options = null)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            options = options ?? new SendMessageOptions();
            var targetUserId = options.TargetUserId ?? _configManager.Config.TargetUserId;

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
                                user_id = targetUserId,
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
                                user_id = targetUserId,
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
                                user_id = targetUserId,
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

                    // 插件主动发送的消息默认添加到聊天记录
                    // 注意：使用 "plugin" 角色，与模型回复的 "ai" 角色区分开
                    _chatHistoryManager.AddMessage("plugin", message);

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

        public List<ContextMessage> GetFullContext()
        {
            var context = new List<ContextMessage>();

            try
            {
                // 获取系统上下文
                var systemContext = _contextManager.Context;
                foreach (var msg in systemContext)
                {
                    context.Add(new ContextMessage
                    {
                        Role = msg.Role,
                        Content = msg.Content?.ToString(),
                        Timestamp = DateTime.Now // 上下文没有存储时间戳
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("PluginApi", $"Failed to get context: {ex.Message}", ex);
            }

            return context;
        }

        #endregion

        #region 9. 上下文写入接口

        public void AddContextMessage(string role, string content)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(role) || string.IsNullOrWhiteSpace(content))
                {
                    Logger.LogWarning("PluginApi", "Failed to add context message: role or content cannot be empty");
                    return;
                }

                // 标准化角色名称
                role = NormalizeRole(role);

                // 添加到AI对话上下文（用于LLM请求）
                switch (role)
                {
                    case "system":
                        _contextManager.AddSystemMessage(content);
                        break;
                    case "user":
                        _contextManager.AddUserMessage(content, out _);
                        break;
                    case "assistant":
                        _contextManager.AddAssistantMessage(content);
                        break;
                    default:
                        _contextManager.AddSystemMessage(content);
                        break;
                }

                Logger.LogInfo("PluginApi", $"Plugin added context message successfully: role={role}");
            }
            catch (Exception ex)
            {
                Logger.LogError("PluginApi", $"Failed to add context message: {ex.Message}", ex);
            }
        }

        public void ClearContext()
        {
            try
            {
                _contextManager.ClearContext();
                Logger.LogInfo("PluginApi", "Plugin cleared context successfully");
            }
            catch (Exception ex)
            {
                Logger.LogError("PluginApi", $"Failed to clear context: {ex.Message}", ex);
            }
        }

        public int RemoveLastMessages(string role, int count)
        {
            try
            {
                if (count <= 0) return 0;

                // 标准化角色名称
                role = NormalizeRole(role);

                int removed = 0;
                var context = _contextManager.Context;
                if (context == null || context.Count == 0) return 0;

                // 从后往前遍历，删除匹配角色的消息
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
                    _contextManager.SaveContextToDisk();
                }

                Logger.LogInfo("PluginApi", $"Plugin removed context messages successfully: role={role}, count={removed}");
                return removed;
            }
            catch (Exception ex)
            {
                Logger.LogError("PluginApi", $"Failed to remove context messages: {ex.Message}", ex);
                return 0;
            }
        }

        /// <summary>
        /// 标准化角色名称
        /// </summary>
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

        /// <summary>
        /// 添加权限到指定插件
        /// </summary>
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

        /// <summary>
        /// 获取当前插件的权限列表
        /// </summary>
        public List<string> GetRegisteredPermissions()
        {
            string currentPluginId = GetCurrentPluginId();
            return GetPluginPermissions(currentPluginId);
        }

        /// <summary>
        /// 获取指定插件的权限列表
        /// </summary>
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

        /// <summary>
        /// 获取所有插件的权限信息
        /// </summary>
        public Dictionary<string, List<string>> GetAllPluginPermissions()
        {
            lock (_pluginPermissions)
            {
                return new Dictionary<string, List<string>>(_pluginPermissions);
            }
        }

        /// <summary>
        /// 注销插件时清理权限
        /// </summary>
        internal void UnregisterPluginPermissions(string pluginId)
        {
            if (string.IsNullOrEmpty(pluginId)) return;

            lock (_pluginPermissions)
            {
                _pluginPermissions.Remove(pluginId);
            }
        }

        #endregion
    }
}
