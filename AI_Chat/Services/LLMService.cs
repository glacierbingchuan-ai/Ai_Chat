using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AI_Chat.Models;
using AI_Chat.Constants;
using AI_Chat.Plugins;
using AI_Chat.Utils;
using static AI_Chat.Utils.PathUtils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Betalgo.Ranul.OpenAI;
using Betalgo.Ranul.OpenAI.Managers;
using Betalgo.Ranul.OpenAI.ObjectModels;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using Betalgo.Ranul.OpenAI.Contracts.Enums.Image;
using OpenAIChatMessage = Betalgo.Ranul.OpenAI.ObjectModels.RequestModels.ChatMessage;
using Message = AI_Chat.Models.Message;

namespace AI_Chat.Services
{
    public class LLMService
    {
        private readonly ConfigManager _configManager;
        private readonly RequestRateLimiter _requestRateLimiter;
        private readonly ImageService _imageService;
        private bool _lastLlmStatus = false;
        private DateTime _lastLlmCheckTime = DateTime.MinValue;
        private PluginApi _pluginApi;
        private IPluginManager _pluginManager;
        private OpenAIService _openAIService;

        private Func<string, Task> _webSocketSendFunc;

        public LLMService(ConfigManager configManager, RequestRateLimiter requestRateLimiter = null)
        {
            _configManager = configManager;
            _requestRateLimiter = requestRateLimiter;
            _imageService = new ImageService();
            InitializeOpenAIService();
        }

        /// <summary>
        /// 设置WebSocket发送函数
        /// </summary>
        public void SetWebSocketSendFunc(Func<string, Task> sendFunc)
        {
            _webSocketSendFunc = sendFunc;
        }

        /// <summary>
        /// 处理协议端返回的WebSocket响应
        /// </summary>
        public void HandleWebSocketResponse(string json)
        {
            try
            {
                var response = JObject.Parse(json);
                // 将响应传递给 ImageService 处理（静态方法）
                ImageService.HandleResponse(response);
            }
            catch (Exception ex)
            {
                Logger.LogError("LLMService", $"Error handling WebSocket response: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置PluginManager引用（用于获取插件能力列表）
        /// </summary>
        public void SetPluginManager(IPluginManager pluginManager)
        {
            _pluginManager = pluginManager;
        }

        private void InitializeOpenAIService()
        {
            var options = new OpenAIOptions
            {
                ApiKey = _configManager.Config.LlmApiKey,
                BaseDomain = GetBaseDomain(_configManager.Config.LlmApiBaseUrl)
            };
            _openAIService = new OpenAIService(options);
        }

        private string GetBaseDomain(string apiUrl)
        {
            if (string.IsNullOrEmpty(apiUrl))
                return null;

            try
            {
                var uri = new Uri(apiUrl);
                string baseUrl = $"{uri.Scheme}://{uri.Host}:{uri.Port}";
                
                if (!string.IsNullOrEmpty(uri.PathAndQuery))
                {
                    string path = uri.PathAndQuery;
                    
                    int chatCompletionsIndex = path.IndexOf("/chat/completions", StringComparison.OrdinalIgnoreCase);
                    if (chatCompletionsIndex >= 0)
                    {
                        path = path.Substring(0, chatCompletionsIndex);
                    }
                    
                    if (!string.IsNullOrEmpty(path))
                    {
                        baseUrl += path;
                    }
                }
                
                return baseUrl;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 设置PluginApi引用（用于调用LLM请求前处理器）
        /// </summary>
        public void SetPluginApi(PluginApi pluginApi)
        {
            _pluginApi = pluginApi;
        }

        public void UpdateApiKey(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Logger.LogWarning("LLMService", "Attempted to update API key with empty value, ignoring");
                return;
            }

            // 重新初始化 OpenAIService
            var options = new OpenAIOptions
            {
                ApiKey = apiKey,
                BaseDomain = GetBaseDomain(_configManager.Config.LlmApiBaseUrl)
            };
            _openAIService = new OpenAIService(options);
            Logger.LogInfo("LLMService", "API key updated successfully");
        }

        public async Task<string> GetRawLLMResponseAsync(List<Message> context, CancellationToken token, string userMessage = null, long userId = 0)
        {
            // LLM 离线时不排队，直接返回错误（状态检查请求除外）
            if (!_lastLlmStatus)
            {
                Logger.LogWarning("LLMService", "LLM is offline, skipping GetRawLLMResponseAsync request");
                return "[LLM 服务当前离线，请稍后再试]";
            }

            if (_requestRateLimiter != null)
            {
                return await _requestRateLimiter.EnqueueRequest(async () =>
                {
                    return await DoGetRawLLMResponseAsync(context, token, userMessage, userId);
                });
            }
            return await DoGetRawLLMResponseAsync(context, token, userMessage, userId);
        }

        private async Task<string> DoGetRawLLMResponseAsync(List<Message> context, CancellationToken token, string userMessage = null, long userId = 0)
        {
            Logger.LogInfo("LLMService", $"========== Sending context to LLM (total {context.Count} messages) ==========");
            for (int i = 0; i < context.Count; i++)
            {
                var msg = context[i];
                Logger.LogInfo("LLMService", $"  [{i}] {msg.Role}: {msg.Content ?? ""}");
            }
            Logger.LogInfo("LLMService", "========== End of context ==========");

            // 转换消息，支持多模态
            var messages = new List<OpenAIChatMessage>();
            foreach (var msg in context)
            {
                var chatMessage = await ConvertToOpenAIChatMessageAsync(msg, userId);
                messages.Add(chatMessage);
            }

            var request = new ChatCompletionCreateRequest
            {
                Model = _configManager.Config.LlmModelName,
                Messages = messages,
                MaxTokens = _configManager.Config.LlmMaxTokens,
                Temperature = (float)_configManager.Config.LlmTemperature,
                TopP = (float)_configManager.Config.LlmTopP
            };

            string requestJson = JsonConvert.SerializeObject(new
            {
                model = request.Model,
                messages = context.Select(m => new { role = m.Role, content = m.Content }),
                max_tokens = request.MaxTokens,
                temperature = request.Temperature,
                top_p = request.TopP
            });

            // 调用LLM请求前处理器（插件可修改请求内容）
            if (_pluginApi != null)
            {
                var preRequestContext = new AI_Chat.Plugins.PreLLMRequestContext
                {
                    UserId = userId,
                    RequestJson = requestJson,
                    RequestId = Guid.NewGuid().ToString("N").Substring(0, 8),
                    ContextMessages = context.Select(m => new AI_Chat.Plugins.ContextMessage
                    {
                        Role = m.Role,
                        Content = m.Content?.ToString(),
                        Timestamp = DateTime.Now
                    }).ToList(),
                    UserMessage = userMessage
                };

                var preRequestResult = _pluginApi.HandlePreLLMRequest(preRequestContext);

                if (preRequestResult.IsIntercepted)
                {
                    // 插件拦截了请求，返回拦截的响应
                    Logger.LogInfo("LLMService", "[PLUGIN] LLM request intercepted by plugin");
                    return preRequestResult.InterceptedResponse ?? "{\"choices\":[{\"message\":{\"content\":\"\"}}]}";
                }

                if (preRequestResult.IsModified)
                {
                    // 插件修改了请求，使用修改后的JSON
                    Logger.LogInfo("LLMService", "[PLUGIN] LLM request modified by plugin");
                    // 解析修改后的JSON并请求
                    try
                    {
                        var modifiedRequest = JsonConvert.DeserializeObject<dynamic>(preRequestResult.ModifiedRequestJson);
                        if (modifiedRequest.model != null)
                            request.Model = modifiedRequest.model.ToString();
                        if (modifiedRequest.max_tokens != null)
                            request.MaxTokens = (int)modifiedRequest.max_tokens;
                        if (modifiedRequest.temperature != null)
                            request.Temperature = (float)modifiedRequest.temperature;
                        if (modifiedRequest.top_p != null)
                            request.TopP = (float)modifiedRequest.top_p;
                        if (modifiedRequest.messages != null)
                        {
                            var modifiedMessages = new List<OpenAIChatMessage>();
                            foreach (var msg in modifiedRequest.messages)
                            {
                                modifiedMessages.Add(new OpenAIChatMessage
                                {
                                    Role = msg.role?.ToString(),
                                    Content = msg.content?.ToString()
                                });
                            }
                            request.Messages = modifiedMessages;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning("LLMService", $"Failed to parse modified request: {ex.Message}");
                    }
                }

            }

            // 在最终提交给大模型之前，临时添加可用插件提示词（不持久化）
            if (_pluginManager != null)
            {
                var pluginPrompt = _pluginManager.GeneratePluginCapabilitiesPrompt();
                if (!string.IsNullOrEmpty(pluginPrompt))
                {
                    // 找到第一条system消息，临时追加插件提示词
                    var systemMessage = request.Messages?.FirstOrDefault(m => m.Role == "system");
                    if (systemMessage != null)
                    {
                        // 临时修改，不保存回原始context
                        systemMessage.Content = systemMessage.Content + pluginPrompt;
                        Logger.LogInfo("LLMService", "[PLUGIN] Temporarily added plugin capabilities prompt to system message");
                    }
                    else
                    {
                        // 如果没有system消息，临时创建一条新的
                        request.Messages.Insert(0, new OpenAIChatMessage
                        {
                            Role = "system",
                            Content = pluginPrompt
                        });
                        Logger.LogInfo("LLMService", "[PLUGIN] Temporarily created system message with plugin capabilities prompt");
                    }
                }
            }

            try
            {
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(40));
                    var completionResult = await _openAIService.ChatCompletion.CreateCompletion(request, cancellationToken: cts.Token);
                    
                    if (completionResult.Successful)
                    {
                        return completionResult.Choices.FirstOrDefault()?.Message?.Content;
                    }
                    else
                    {
                        Logger.LogError("LLMService", $"OpenAI API error: {completionResult.Error?.Message}");
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("LLMService", $"Error in GetChatCompletionAsync: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 将消息转换为OpenAI ChatMessage，支持多模态
        /// </summary>
        private async Task<OpenAIChatMessage> ConvertToOpenAIChatMessageAsync(Message msg, long userId)
        {
            // 检查消息是否包含软件自己的图片格式 [IMG:]
            if (msg.Role == "user" && ImageService.ContainsImgTag(msg.Content))
            {
                try
                {
                    // 获取用户图片存储目录
                    string userImageDir = GetUserImageDirectory(userId);

                    // 构建多模态内容（从本地读取图片）
                    var contentParts = await _imageService.BuildMultimodalContentAsync(msg.Content, userImageDir);

                    if (contentParts.Count > 0)
                    {
                        // 使用 SDK 的 MessageContent 构建多模态内容
                        var messageContents = BuildMessageContents(contentParts);
                        Logger.LogInfo("LLMService", $"[MULTIMODAL] Built multimodal content with {contentParts.Count} parts from local images");

                        // 使用 SDK 的 FromUser 方法创建多模态消息
                        return OpenAIChatMessage.FromUser(messageContents);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning("LLMService", $"[MULTIMODAL] Failed to build multimodal content: {ex.Message}, using original content");
                }
            }

            // 默认返回普通文本消息
            return new OpenAIChatMessage
            {
                Role = msg.Role,
                Content = msg.Content
            };
        }

        /// <summary>
        /// 获取用户图片存储目录
        /// </summary>
        private string GetUserImageDirectory(long userId)
        {
            string imageDir = Path.Combine(PathUtils.GetUserDirectory(userId), "images");
            if (!Directory.Exists(imageDir))
            {
                Directory.CreateDirectory(imageDir);
            }
            return imageDir;
        }

        /// <summary>
        /// 构建 SDK MessageContent 列表
        /// </summary>
        private IList<MessageContent> BuildMessageContents(List<ContentPart> contentParts)
        {
            var contents = new List<MessageContent>();

            foreach (var part in contentParts)
            {
                if (part.Type == "text" && !string.IsNullOrEmpty(part.Text))
                {
                    contents.Add(MessageContent.TextContent(part.Text));
                }
                else if (part.Type == "image_base64" && !string.IsNullOrEmpty(part.Base64Data))
                {
                    // 使用 SDK 的 ImageBinaryContent 方法
                    byte[] imageData = Convert.FromBase64String(part.Base64Data);
                    var imageType = GetImageFileType(part.MimeType);
                    contents.Add(MessageContent.ImageBinaryContent(imageData, imageType, null));
                }
            }

            return contents;
        }

        /// <summary>
        /// 将 MIME 类型转换为 ImageFileType
        /// </summary>
        private ImageFileType GetImageFileType(string mimeType)
        {
            return mimeType?.ToLowerInvariant() switch
            {
                "image/png" => ImageFileType.Png,
                "image/jpeg" or "image/jpg" => ImageFileType.Jpeg,
                "image/gif" => ImageFileType.Gif,
                "image/webp" => ImageFileType.Webp,
                _ => ImageFileType.Jpeg
            };
        }

        public async Task<CompletenessLevel> IsUserMessageCompleteAsync(string message, string hid, string incompleteInputPrompt = null)
        {
            // LLM 离线时不排队，直接返回 Complete（状态检查请求除外）
            if (!_lastLlmStatus)
            {
                Logger.LogWarning("LLMService", "LLM is offline, skipping IsUserMessageCompleteAsync request");
                return CompletenessLevel.Complete;
            }

            if (_requestRateLimiter != null)
            {
                return await _requestRateLimiter.EnqueueRequest(async () =>
                {
                    return await DoIsUserMessageCompleteAsync(message, hid, incompleteInputPrompt);
                });
            }
            return await DoIsUserMessageCompleteAsync(message, hid, incompleteInputPrompt);
        }

        private async Task<CompletenessLevel> DoIsUserMessageCompleteAsync(string message, string hid, string incompleteInputPrompt = null)
        {
            string prompt = incompleteInputPrompt ?? Constants.SystemPrompts.INCOMPLETE_INPUT_PROMPT;

            var request = new ChatCompletionCreateRequest
            {
                Model = _configManager.Config.LlmModelName,
                Messages = new List<OpenAIChatMessage>
                {
                    new OpenAIChatMessage { Role = "system", Content = prompt },
                    new OpenAIChatMessage { Role = "user", Content = message }
                },
                MaxTokens = 15,
                Temperature = 0.0f
            };

            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                {
                    var completionResult = await _openAIService.ChatCompletion.CreateCompletion(request, cancellationToken: cts.Token);
                    
                    if (completionResult.Successful)
                    {
                        string result = completionResult.Choices.FirstOrDefault()?.Message?.Content?.ToUpper() ?? "";
                        if (result.Contains("INCOMPLETE")) return CompletenessLevel.Incomplete;
                        if (result.Contains("UNCERTAIN")) return CompletenessLevel.Uncertain;
                        return CompletenessLevel.Complete;
                    }
                    return CompletenessLevel.Complete;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("LLMService", $"Error in IsUserMessageCompleteAsync: {ex.Message}");
                return CompletenessLevel.Complete;
            }
        }

        public async Task<string> SummarizeContextAsync(List<Message> messagesToSummarize)
        {
            // LLM 离线时不排队，直接返回空（状态检查请求除外）
            if (!_lastLlmStatus)
            {
                Logger.LogWarning("LLMService", "LLM is offline, skipping SummarizeContextAsync request");
                return null;
            }

            if (_requestRateLimiter != null)
            {
                return await _requestRateLimiter.EnqueueRequest(async () =>
                {
                    return await DoSummarizeContextAsync(messagesToSummarize);
                });
            }
            return await DoSummarizeContextAsync(messagesToSummarize);
        }

        private async Task<string> DoSummarizeContextAsync(List<Message> messagesToSummarize)
        {
            string history = MessageUtils.FormatMessagesToHistory(
                messagesToSummarize
                    .Where(m => m.Role != "system"
                             && !m.Content.Contains(AppConstants.TAG_PROACTIVE)
                             && !m.Content.Contains(AppConstants.TAG_REMINDER)
                             && !m.Content.Contains(AppConstants.TAG_FORMAT_ERROR))
                    .ToList());

            if (string.IsNullOrWhiteSpace(history)) return null;

            var request = new ChatCompletionCreateRequest
            {
                Model = _configManager.Config.LlmModelName,
                Messages = new List<OpenAIChatMessage>
                {
                    new OpenAIChatMessage { Role = "system", Content = "请基于【历史对话总结】和【新增对话内容】，生成一份完整、详细的最新对话总结。\n要求：\n1. 必须包含所有核心信息：人物、核心话题、关键观点、时间信息、约定事件、补充细节\n2. 合并历史总结和新增内容，避免重复，保持逻辑连贯\n3. 语言精炼，去除冗余话术\n4. 总结开头必须以\"对话总结：\"开头\n5. 注意分清人物 assistant是助手，user是用户\n6. 注意包含历史对话总结的详细信息，不要遗漏任何关键信息7. 只能使用纯文本输出" },
                    new OpenAIChatMessage { Role = "user", Content = history }
                }
            };

            var completionResult = await _openAIService.ChatCompletion.CreateCompletion(request);
            
            if (completionResult.Successful)
            {
                string summary = completionResult.Choices.FirstOrDefault()?.Message?.Content;
                if (summary != null && summary.StartsWith("对话总结："))
                    summary = summary.Substring(5).Trim();
                return summary;
            }
            return null;
        }

        public bool TryParseAndValidateReply(string raw, out AIReplyModel model)
        {
            model = null;
            try
            {
                string content = Regex.Replace(raw, @"```json\s*", "");
                content = Regex.Replace(content, @"```\s*", "").Trim();

                var settings = new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Ignore };
                model = JsonConvert.DeserializeObject<AIReplyModel>(content, settings);

                if (model == null || model.Messages == null)
                {
                    model = null;
                    return false;
                }

                foreach (var m in model.Messages)
                {
                    string mStr = m.ToString();
                    bool hasContent = mStr.Contains("\"content\":");
                    bool hasMeme = mStr.Contains("\"meme\":");

                    if (hasContent && hasMeme)
                    {
                        model = null;
                        return false;
                    }

                    if (hasContent)
                    {
                        string text = "";
                        try { text = m.content?.ToString() ?? ""; } catch (Exception ex) { Logger.LogWarning("LLM", $"Error parsing message content: {ex.Message}"); }
                        if (text.IndexOf("MEME", StringComparison.OrdinalIgnoreCase) >= 0
                            || text.IndexOf(".jpg", StringComparison.OrdinalIgnoreCase) >= 0
                            || text.Contains("_"))
                        {
                            model = null;
                            return false;
                        }
                    }

                    if (!hasContent && !hasMeme)
                    {
                        model = null;
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                model = null;
                return false;
            }
        }

        public async Task<Dictionary<string, object>> CheckLlmApiStatusAsync(string modelName = null, string apiBaseUrl = null, string apiKey = null)
        {
            // 离线状态时不走队列直接请求，在线状态走队列
            if (!_lastLlmStatus)
            {
                return await DoCheckLlmApiStatusAsync(modelName, apiBaseUrl, apiKey);
            }

            if (_requestRateLimiter != null)
            {
                return await _requestRateLimiter.EnqueueRequest(async () =>
                {
                    return await DoCheckLlmApiStatusAsync(modelName, apiBaseUrl, apiKey);
                });
            }
            return await DoCheckLlmApiStatusAsync(modelName, apiBaseUrl, apiKey);
        }

        private async Task<Dictionary<string, object>> DoCheckLlmApiStatusAsync(string modelName = null, string apiBaseUrl = null, string apiKey = null)
        {
            try
            {
                string actualModelName = modelName ?? _configManager.Config.LlmModelName;
                string actualApiKey = apiKey ?? _configManager.Config.LlmApiKey;
                string actualBaseDomain = GetBaseDomain(apiBaseUrl ?? _configManager.Config.LlmApiBaseUrl);

                var testOptions = new OpenAIOptions
                {
                    ApiKey = actualApiKey,
                    BaseDomain = actualBaseDomain
                };
                var testService = new OpenAIService(testOptions);

                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
                {
                    var request = new ChatCompletionCreateRequest
                    {
                        Model = actualModelName,
                        Messages = new List<OpenAIChatMessage>
                        {
                            new OpenAIChatMessage { Role = "system", Content = "Ping" },
                            new OpenAIChatMessage { Role = "user", Content = "Ping" }
                        },
                        MaxTokens = 1,
                        Temperature = 0.0f
                    };

                    var completionResult = await testService.ChatCompletion.CreateCompletion(request, cancellationToken: cts.Token);
                    
                    if (completionResult.Successful)
                        return new Dictionary<string, object> { { "success", true }, { "message", "Success: LLM service is available" } };
                    else
                        return new Dictionary<string, object> { { "success", false }, { "message", $"Failed: {completionResult.Error?.Message}" } };
                }
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object> { { "success", false }, { "message", $"Failed: {ex.Message}" } };
            }
        }

        public async Task<string> GetLlmStatusAsync()
        {
            bool llmApiAvailable = _lastLlmStatus;
            if (!_lastLlmStatus || (DateTime.Now - _lastLlmCheckTime).TotalMilliseconds >= AppConstants.LLM_STATUS_CHECK_INTERVAL)
            {
                var result = await CheckLlmApiStatusAsync();
                llmApiAvailable = (bool)result["success"];
                _lastLlmStatus = llmApiAvailable;
                _lastLlmCheckTime = DateTime.Now;
            }
            return llmApiAvailable ? "Online" : "Offline";
        }

        /// <summary>
        /// 发送原始LLM请求（插件使用）
        /// </summary>
        /// <param name="requestJson">请求JSON字符串</param>
        /// <returns>原始响应JSON</returns>
        public async Task<string> SendRequestRawAsync(string requestJson)
        {
            if (string.IsNullOrWhiteSpace(requestJson))
            {
                return JsonConvert.SerializeObject(new { error = "请求JSON不能为空" });
            }

            try
            {
                // 解析请求JSON
                var requestObj = JsonConvert.DeserializeObject<ChatCompletionCreateRequest>(requestJson);
                if (requestObj == null)
                {
                    // 尝试解析为动态对象并转换为 ChatCompletionCreateRequest
                    dynamic dynamicRequest = JsonConvert.DeserializeObject(requestJson);
                    requestObj = new ChatCompletionCreateRequest
                    {
                        Model = dynamicRequest.model?.ToString() ?? _configManager.Config.LlmModelName,
                        MaxTokens = dynamicRequest.max_tokens != null ? (int?)dynamicRequest.max_tokens : _configManager.Config.LlmMaxTokens,
                        Temperature = dynamicRequest.temperature != null ? (float?)dynamicRequest.temperature : (float)_configManager.Config.LlmTemperature,
                        TopP = dynamicRequest.top_p != null ? (float?)dynamicRequest.top_p : (float)_configManager.Config.LlmTopP
                    };

                    if (dynamicRequest.messages != null)
                    {
                        var messages = new List<OpenAIChatMessage>();
                        foreach (var msg in dynamicRequest.messages)
                        {
                            messages.Add(new OpenAIChatMessage
                            {
                                Role = msg.role?.ToString(),
                                Content = msg.content?.ToString()
                            });
                        }
                        requestObj.Messages = messages;
                    }
                }

                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
                {
                    var completionResult = await _openAIService.ChatCompletion.CreateCompletion(requestObj, cancellationToken: cts.Token);
                    
                    if (completionResult.Successful)
                    {
                        // 将结果转换为JSON字符串
                        return JsonConvert.SerializeObject(new
                        {
                            choices = completionResult.Choices.Select(c => new
                            {
                                message = new
                                {
                                    role = c.Message.Role,
                                    content = c.Message.Content
                                }
                            }).ToList()
                        });
                    }
                    else
                    {
                        return JsonConvert.SerializeObject(new { error = completionResult.Error?.Message });
                    }
                }
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new { error = ex.Message });
            }
        }
    }
}
