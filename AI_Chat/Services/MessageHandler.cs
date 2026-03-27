using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AI_Chat.Models;
using AI_Chat.Constants;
using AI_Chat.Managers;
using AI_Chat.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using AI_Chat.Plugins;

namespace AI_Chat.Services
{
    public class MessageHandler
    {
        private readonly ConfigManager _configManager;
        private readonly UserConfigManager _userConfigManager;
        private readonly LLMService _llmService;
        private readonly WebSocketClient _webSocketClient;
        private readonly CancellationTokenSource _globalCts;
        private readonly Random _random = new Random();

        private readonly UserSessionManager _sessionManager;

        private Action<WebSocketMessage> _broadcastCallback;
        private PluginManager _pluginManager;
        private PluginApi _pluginApi;

        public MessageHandler(
            ConfigManager configManager,
            UserConfigManager userConfigManager,
            LLMService llmService,
            WebSocketClient webSocketClient,
            CancellationTokenSource globalCts,
            UserSessionManager sessionManager)
        {
            _configManager = configManager;
            _userConfigManager = userConfigManager;
            _llmService = llmService;
            _webSocketClient = webSocketClient;
            _globalCts = globalCts;
            _sessionManager = sessionManager;
        }

        public void SetPluginManager(PluginManager pluginManager)
        {
            _pluginManager = pluginManager;
        }

        public void SetPluginApi(PluginApi pluginApi)
        {
            _pluginApi = pluginApi;
        }

        public void InitializeBroadcastCallback(Action<WebSocketMessage> broadcastCallback)
        {
            _broadcastCallback = broadcastCallback;
        }

        public async Task HandleMessageAsync(string json)
        {
            string hid = Guid.NewGuid().ToString("N").Substring(0, 8);
            try
            {
                dynamic msgData = JsonConvert.DeserializeObject(json);
                if (msgData?.post_type != "message") return;

                string messageType = msgData?.message_type?.ToString();

                if (messageType == "private")
                {
                    await HandlePrivateMessageAsync(msgData, hid);
                }
                else if (messageType == "group")
                {
                    await HandleGroupMessageAsync(msgData, hid);
                }
            }
            catch (Exception ex) { Logger.LogError(hid, "Critical error during message handling pipeline.", ex); }
        }

        /// <summary>
        /// 处理消息中的图片，下载并转换为软件自己的格式
        /// </summary>
        private async Task<string> ProcessMessageImagesAsync(string message, long userId, string hid)
        {
            if (!ImageService.ContainsCqImage(message))
            {
                return message;
            }

            try
            {
                Logger.LogInfo(hid, $"[IMAGE_PROCESSING] Detected CQ:image in message from user {userId}");

                // 获取用户图片存储目录
                string userImageDir = GetUserImageDirectory(userId);

                // 使用ImageService处理图片（WebSocket响应处理器已在Program.cs中全局设置）
                var imageService = new ImageService();
                var result = await imageService.ProcessCqImagesAsync(message, userImageDir, async (json) =>
                {
                    // 发送WebSocket请求
                    await _webSocketClient.SendMessageAsync(json);
                });

                if (result.ProcessedImages.Count > 0)
                {
                    Logger.LogInfo(hid, $"[IMAGE_PROCESSING] Successfully processed {result.ProcessedImages.Count} images");
                    Logger.LogInfo(hid, $"[IMAGE_PROCESSING] Converted message: \"{result.ConvertedMessage}\"");
                }

                return result.ConvertedMessage;
            }
            catch (Exception ex)
            {
                Logger.LogError(hid, $"[IMAGE_PROCESSING] Failed to process images: {ex.Message}");
                return message; // 如果处理失败，返回原始消息
            }
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

        private async Task HandlePrivateMessageAsync(dynamic msgData, string hid)
        {
            long userId = (long)msgData.user_id;

            if (!_configManager.Config.AllowedUserIds.Contains(userId))
            {
                Logger.LogInfo(hid, $"[REJECTION] Message from unauthorized user: {userId}");
                return;
            }

            string messageId = msgData.message_id?.ToString();

            var session = _sessionManager.GetOrCreateSession(userId);
            session.LastActiveTime = DateTime.Now;

            if (!session.TryAddProcessedMessage(messageId)) return;

            session.LatestHandlerId = hid;

            string rawContent = msgData.raw_message?.ToString() ?? "";
            Logger.LogInfo(hid, $"[RECEPTION] Raw message fragment from user {userId}: \"{rawContent}\"");

            // 处理消息中的图片（如果是CQ:image格式）
            string processedContent = await ProcessMessageImagesAsync(rawContent, userId, hid);

            var chatHistoryManager = _sessionManager.GetOrCreateChatHistoryManager(userId);
            chatHistoryManager.AddMessage("user", processedContent);

            if (_pluginApi != null)
            {
                var preMergeContext = new PreMergeMessageContext
                {
                    UserId = userId,
                    RawMessage = rawContent,
                    Source = userId.ToString(),
                    Timestamp = DateTime.Now
                };

                var preMergeResult = _pluginApi.HandlePreMergeMessage(preMergeContext);

                if (preMergeResult.IsIntercepted)
                {
                    Logger.LogInfo(hid, "[PLUGIN] Raw message intercepted by plugin");

                    if (session.GetAccumulatedMessage().Length > 0)
                    {
                        Logger.LogInfo(hid, "[PLUGIN] Clearing accumulated message buffer to avoid affecting subsequent message merging");
                        session.ClearAccumulatedMessage();
                    }

                    if (preMergeResult.Response != null)
                    {
                        await SendPluginResponseAsync(preMergeResult.Response, userId, hid);
                    }
                    return;
                }

                if (preMergeResult.IsModified)
                {
                    rawContent = preMergeResult.ModifiedMessage;
                    Logger.LogInfo(hid, $"[PLUGIN] Raw message modified by plugin: \"{rawContent}\"");
                }
            }

            InterruptionAndPhysicalCleanup(session, hid);

            session.AppendToAccumulatedMessage(processedContent);

            string draft = session.GetAccumulatedMessage();

            CompletenessLevel level = CompletenessLevel.Complete;
            var userConfig = _userConfigManager.GetOrCreateUserConfig(userId);
            if (userConfig.IntentAnalysisEnabled)
            {
                Logger.LogInfo(hid, "[INTENT_ANALYSIS] Invoking LLM for message completeness verification...");
                level = await _llmService.IsUserMessageCompleteAsync(draft, hid, userConfig.IncompleteInputPrompt);
                Logger.LogInfo(hid, $"[ANALYSIS_RESULT] Determined status: {level}");
            }
            else
            {
                Logger.LogInfo(hid, "[INTENT_ANALYSIS] Intent analysis disabled. Skipping message completeness verification.");
            }

            if (level == CompletenessLevel.Incomplete)
            {
                Logger.LogInfo(hid, "[STATE_UPDATE] Completeness: INCOMPLETE. Buffering draft and awaiting further input.");
                StartIncompleteTimeout(session, hid);
                return;
            }

            if (level == CompletenessLevel.Uncertain)
            {
                Logger.LogInfo(hid, "[STATE_UPDATE] Completeness: UNCERTAIN. Commencing 5000ms observation window...");
                DateTime waitStart = DateTime.Now;
                while (DateTime.Now - waitStart < TimeSpan.FromSeconds(5))
                {
                    await Task.Delay(200);
                    if (session.InputState.LastMessageTime > waitStart || session.LatestHandlerId != hid)
                    {
                        Logger.LogInfo(hid, "[OBSERVATION] Newer message or task priority detected. Aborting current handler.");
                        return;
                    }
                }
                Logger.LogInfo(hid, "[OBSERVATION] Observation window closed with no new input. Proceeding to reply.");
            }

            if (session.LatestHandlerId != hid) return;

            await CommitAndReplyAsync(session, hid);
        }

        private async Task HandleGroupMessageAsync(dynamic msgData, string hid)
        {
            long groupId = (long)msgData.group_id;
            long userId = (long)msgData.user_id;

            if (!_configManager.Config.AllowedGroupIds.Contains(groupId))
            {
                Logger.LogInfo(hid, $"[REJECTION] Message from unauthorized group: {groupId}");
                return;
            }

            string rawContent = msgData.raw_message?.ToString() ?? "";
            Logger.LogInfo(hid, $"[RECEPTION] Group message from group {groupId}, user {userId}: \"{rawContent}\"");

            if (_pluginApi == null)
            {
                Logger.LogInfo(hid, "[GROUP] No plugin API available, ignoring group message");
                return;
            }

            var groupContext = new GroupMessageContext
            {
                GroupId = groupId,
                UserId = userId,
                MessageId = msgData.message_id?.ToString(),
                RawMessage = rawContent,
                Timestamp = DateTime.Now,
                SenderNickname = msgData.sender?.nickname?.ToString() ?? "",
                MessageArray = msgData.message
            };

            var result = _pluginApi.HandleGroupMessage(groupContext);

            if (result?.IsHandled == true)
            {
                Logger.LogInfo(hid, "[GROUP] Message handled by plugin");

                if (!string.IsNullOrEmpty(result.ReplyMessage))
                {
                    await SendGroupMessageAsync(result.ReplyMessage, groupId, hid);
                }
            }
            else
            {
                Logger.LogInfo(hid, "[GROUP] No plugin handled this message, ignoring");
            }
        }

        private async Task SendGroupMessageAsync(string message, long groupId, string hid)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(message))
                    return;

                var payload = new
                {
                    action = "send_group_msg",
                    @params = new
                    {
                        group_id = groupId,
                        message = message
                    }
                };

                await _webSocketClient.SendMessageAsync(JsonConvert.SerializeObject(payload));
                Logger.LogInfo(hid, $"[GROUP_RESPONSE] Sent reply to group {groupId}: \"{message}\"");
            }
            catch (Exception ex)
            {
                Logger.LogError(hid, $"[GROUP_RESPONSE] Failed to send group message: {ex.Message}", ex);
            }
        }

        private async Task CommitAndReplyAsync(UserSession session, string hid)
        {
            string finalizedMessage = session.GetAndClearAccumulatedMessage();
            if (string.IsNullOrEmpty(finalizedMessage)) return;

            long userId = session.UserId;
            var contextManager = _sessionManager.GetOrCreateContextManager(userId);
            var chatHistoryManager = _sessionManager.GetOrCreateChatHistoryManager(userId);

            if (_pluginApi != null)
            {
                var postMergeContext = new PostMergeMessageContext
                {
                    UserId = userId,
                    FullMessage = finalizedMessage,
                    Source = userId.ToString(),
                    Timestamp = DateTime.Now,
                    MessageFragments = new List<string> { finalizedMessage }
                };

                var postMergeResult = _pluginApi.HandlePostMergeMessage(postMergeContext);

                if (postMergeResult.IsIntercepted)
                {
                    Logger.LogInfo(hid, "[PLUGIN] Full message intercepted by plugin");
                    if (postMergeResult.Response != null)
                    {
                        await SendPluginResponseAsync(postMergeResult.Response, userId, hid);
                    }
                    return;
                }

                if (postMergeResult.IsModified)
                {
                    finalizedMessage = postMergeResult.ModifiedMessage;
                    Logger.LogInfo(hid, $"[PLUGIN] Full message modified by plugin: \"{finalizedMessage}\"");
                }
            }

            var (isAppended, fullMessage) = await contextManager.AddUserMessageAsync(finalizedMessage);
            if (isAppended)
            {
                Logger.LogInfo(hid, $"[CONTEXT_FUSION] Appended message to existing user turn: \"{fullMessage}\"");

                if (_pluginApi != null)
                {
                    int msgIndex = contextManager.LastUserMessageIndex;
                    var appendedContext = new AI_Chat.Plugins.MessageAppendedContext
                    {
                        UserId = userId,
                        OriginalMessage = fullMessage.Substring(0, fullMessage.Length - finalizedMessage.Length - 1),
                        AppendedContent = finalizedMessage,
                        FullMessage = fullMessage,
                        MessageIndex = msgIndex
                    };

                    var appendedResult = _pluginApi.HandleMessageAppended(appendedContext);

                    if (appendedResult.IsIntercepted)
                    {
                        Logger.LogInfo(hid, $"[PLUGIN] Appended message intercepted by plugin");
                        if (!string.IsNullOrEmpty(appendedResult.Response))
                        {
                            await SendPluginResponseAsync(appendedResult.Response, userId, hid);
                        }
                        return;
                    }

                    if (appendedResult.IsModified)
                    {
                        contextManager.UpdateUserMessage(msgIndex, appendedResult.ModifiedMessage);
                        fullMessage = appendedResult.ModifiedMessage;
                        Logger.LogInfo(hid, $"[PLUGIN] Appended message modified by plugin: \"{fullMessage}\"");
                    }
                }
            }
            else
            {
                Logger.LogInfo(hid, $"[CONTEXT_COMMIT] New user dialogue turn recorded: \"{fullMessage}\"");
            }

            Logger.LogAIContext(hid, contextManager.Context);
            await TriggerAIReplyFlow(session, hid);
        }

        private async Task SendPluginResponseAsync(object response, long userId, string hid)
        {
            try
            {
                string text = response?.ToString();
                if (!string.IsNullOrEmpty(text))
                {
                    Logger.LogInfo(hid, $"[PLUGIN_RESPONSE] Sending plugin response: {text}");

                    var payload = new { action = "send_msg", @params = new { user_id = userId, message = text } };
                    await _webSocketClient.SendMessageAsync(JsonConvert.SerializeObject(payload));

                    var session = _sessionManager.GetOrCreateSession(userId);
                    session.IncrementTotalMessages();

                    var chatHistoryManager = _sessionManager.GetOrCreateChatHistoryManager(userId);
                    chatHistoryManager.AddMessage("ai", text);

                    BroadcastStats();
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(hid, $"[PLUGIN_RESPONSE] Failed to send plugin response: {ex.Message}", ex);
            }
        }

        private async Task TriggerAIReplyFlow(UserSession session, string hid)
        {
            long userId = session.UserId;
            var contextManager = _sessionManager.GetOrCreateContextManager(userId);
            var chatHistoryManager = _sessionManager.GetOrCreateChatHistoryManager(userId);

            CancellationTokenSource thisTaskCts;
            thisTaskCts = new CancellationTokenSource();
            session.MasterCts = thisTaskCts;

            try
            {
                AIReplyModel aiReply = null;
                int retryCount = 0;
                const int MAX_RETRIES = 6;
                bool isPluginIntercepted = false;
                string originalRawResponse = null;

                while (retryCount < MAX_RETRIES)
                {
                    // 如果不使用向量上下文且开启对话压缩，则执行压缩
                    Logger.LogInfo(hid, $"[CONTEXT_DEBUG] Context.Count={contextManager.Context.Count}, MaxContextRounds={_configManager.Config.MaxContextRounds}, Threshold={_configManager.Config.MaxContextRounds * 2 + 2}");
                    if (!_configManager.Config.UseVectorContext && _configManager.Config.UseContextSummarization)
                    {
                        if (contextManager.Context.Count > _configManager.Config.MaxContextRounds * 2 + 2)
                        {
                            Logger.LogInfo(hid, "[CONTEXT_COMPRESSION] Context exceeds threshold, triggering summarization...");
                            if (contextManager is ContextManager cm)
                            {
                                await cm.SummarizeContextAsync(hid);
                            }
                        }
                    }

                    string lastUserMessage = contextManager.Context
                        .LastOrDefault(m => m.Role == "user" 
                            && !m.Content.Contains(AppConstants.TAG_PROACTIVE) 
                            && !m.Content.Contains(AppConstants.TAG_REMINDER))
                        ?.Content?.ToString();
                    
                    float threshold = _configManager.Config.VectorDbSimilarityThreshold;
                    int maxRecentMessages = _configManager.Config.MaxContextRounds;
                    List<Message> contextCopy = contextManager.GetContextForPrompt(lastUserMessage ?? "", maxRecentMessages, threshold);
                    Logger.LogAIContext(hid, contextCopy);
                    Logger.LogInfo(hid, $"[LLM_REQUEST] Requesting reply (Attempt {retryCount + 1}/{MAX_RETRIES})...");

                    string rawResponse = await _llmService.GetRawLLMResponseAsync(contextCopy, thisTaskCts.Token, lastUserMessage, userId);
                    if (originalRawResponse == null)
                        originalRawResponse = rawResponse;
                    Logger.LogInfo(hid, $"[LLM_RAW_RESPONSE] Raw response from LLM: {rawResponse}");
                    if (string.IsNullOrEmpty(rawResponse))
                    {
                        Logger.LogWarning(hid, "[LLM_REQUEST] LLM API returned empty response, triggering retry");
                        await Task.Delay(1000, thisTaskCts.Token);
                        retryCount++;
                        continue;
                    }

                    if (_pluginApi != null)
                    {
                        var llmContext = new LLMResponseContext
                        {
                            UserId = userId,
                            RawResponse = rawResponse,
                            RequestId = hid
                        };

                        var llmResult = _pluginApi.HandleLLMResponse(llmContext);

                        if (llmResult.IsIntercepted)
                        {
                            Logger.LogInfo(hid, "[PLUGIN] LLM response intercepted by plugin");
                            if (!string.IsNullOrEmpty(llmResult.AlternativeResponse))
                            {
                                rawResponse = llmResult.AlternativeResponse;
                            }
                            else
                            {
                                aiReply = new AIReplyModel { NeedReply = false, Messages = new List<dynamic>(), Events = new List<EventModel>() };
                                isPluginIntercepted = true;
                                break;
                            }
                        }
                        else if (llmResult.IsModified && !string.IsNullOrEmpty(llmResult.AlternativeResponse))
                        {
                            Logger.LogInfo(hid, "[PLUGIN] LLM response modified by plugin");
                            rawResponse = llmResult.AlternativeResponse;
                        }
                    }

                    if (_llmService.TryParseAndValidateReply(rawResponse, out aiReply))
                    {
                        Logger.LogInfo(hid, $"[LLM_PARSE_SUCCESS] Successfully parsed AI reply. NeedReply={aiReply.NeedReply}, MessagesCount={aiReply.Messages?.Count}, HasPluginInvoke={aiReply.PluginInvoke != null}");
                        break;
                    }
                    else
                    {
                        retryCount++;
                        Logger.LogWarning(hid, $"[SELF_CHECK_FAILED] Invalid JSON format or rule violation:{rawResponse}");
                        contextManager.AddSystemMessage($"{AppConstants.TAG_FORMAT_ERROR} 你的回复格式错误或未遵循规则，已被拦截，信息未发送给用户。错误原因可能是：1. 文字与表情包未完全分离；2. 文字消息中违规包含了[MEME_MSG]占位符；3. JSON语法错误。请严格按照JSON Schema重新输出，表情包必须单独放在messages数组的一个对象中，严禁在文字中包含[MEME_MSG]。你的回复内容：{rawResponse}");
                    }
                }

                // PLUGIN_ 开头的 hid 是插件调用后的延续流程，不需要检查 LatestHandlerId
                if (!hid.StartsWith("ACTIVE_") && !hid.StartsWith("REMIND_") && !hid.StartsWith("PLUGIN_"))
                {
                    if (thisTaskCts.IsCancellationRequested || session.LatestHandlerId != hid) return;
                }
                else if (thisTaskCts.IsCancellationRequested) return;

                if (aiReply == null)
                {
                    Logger.LogError(hid, "[PROCESS_FAILURE] Failed to get valid reply after retries.", null);
                    return;
                }

                Logger.LogInfo(hid, $"[AI_REPLY_DETAILS] NeedReply={aiReply.NeedReply}, Messages={JsonConvert.SerializeObject(aiReply.Messages)}, Events={aiReply.Events?.Count}, PluginInvoke={aiReply.PluginInvoke != null}");

                if (aiReply.Events != null && aiReply.Events.Count > 0)
                {
                    foreach (var ev in aiReply.Events)
                    {
                        contextManager.AddEvent(ev);
                        Logger.LogInfo(hid, $"[EVENT_STORED] Recorded event: {ev.Name} at {ev.Time}");
                    }
                    _broadcastCallback?.Invoke(new WebSocketMessage { Type = "scheduled_events_updated", Data = contextManager.ScheduledEvents });
                }

                contextManager.RemoveFormatErrorMessages();

                if (!aiReply.NeedReply || aiReply.Messages.Count == 0)
                {
                    bool isInternal = hid.StartsWith("ACTIVE_") || hid.StartsWith("REMIND_");
                    if (!isInternal)
                    {
                        if (isPluginIntercepted)
                        {
                            Logger.LogInfo(hid, "[LLM_RESPONSE] AI reply intercepted by plugin, not sent to user.");
                            await contextManager.AddAssistantMessageAsync("[System record: AI reply intercepted by plugin]");
                        }
                        else
                        {
                            Logger.LogInfo(hid, "[LLM_RESPONSE] Model determined no response is necessary.");
                            await contextManager.AddAssistantMessageAsync("[System record: AI chose not to reply to this message]");
                        }
                    }
                    else
                        contextManager.RemoveOrphanInternalTrigger();
                    return;
                }

                Logger.LogInfo(hid, $"[LLM_RESPONSE] Generated {aiReply.Messages.Count} message(s). Commencing phased execution.");

                List<dynamic> successfullySent = new List<dynamic>();
                try
                {
                    await SendAIRepliesStepByStep(aiReply.Messages, thisTaskCts.Token, session, hid, successfullySent);
                }
                finally
                {
                    Logger.LogInfo(hid, $"[DEBUG] successfullySent.Count={successfullySent.Count}, aiReply.Messages.Count={aiReply.Messages?.Count}");
                    if (successfullySent.Count > 0)
                    {
                        // 只保存已发送的消息，但保持原始格式（不包含null字段）
                        var persistModel = new
                        {
                            reply = aiReply.NeedReply,
                            messages = successfullySent,
                            events = aiReply.Events?.Count > 0 ? aiReply.Events : null,
                            plugin_invoke = aiReply.PluginInvoke
                        };

                        string partialJson = JsonConvert.SerializeObject(persistModel, new JsonSerializerSettings
                        {
                            NullValueHandling = NullValueHandling.Ignore
                        });
                        Logger.LogInfo(hid, $"[DEBUG] Saving to context: {partialJson}");
                        await contextManager.AddAssistantMessageAsync(partialJson);

                        // 逐条添加文字消息到向量数据库（用于语义检索）- 仅在向量模式下
                        // 注意：表情包不添加到向量库，因为文件名对语义检索无意义
                        if (contextManager is IVectorContextManager vectorContextManager)
                        {
                            foreach (var msg in successfullySent)
                            {
                                // 只添加文字消息，跳过表情包
                                if (msg.content != null)
                                {
                                    string vectorContent = msg.content.ToString();
                                    if (!string.IsNullOrEmpty(vectorContent))
                                    {
                                        await vectorContextManager.AddVectorEntryAsync(vectorContent, "assistant");
                                    }
                                }
                            }
                        }

                        Logger.LogInfo(hid, $"[PERSISTENCE] Successfully recorded {successfullySent.Count}/{aiReply.Messages.Count} message(s) in context.");
                    }
                }

                // 处理插件调用（如果有）
                if (aiReply.PluginInvoke != null && !string.IsNullOrEmpty(aiReply.PluginInvoke.PluginId))
                {
                    Logger.LogInfo(hid, $"[PLUGIN_INVOKE_DETECTED] PluginId={aiReply.PluginInvoke.PluginId}, Capability={aiReply.PluginInvoke.CapabilityName}");
                    await HandlePluginInvokeAsync(aiReply.PluginInvoke, userId, hid, contextManager);
                }
                else
                {
                    Logger.LogInfo(hid, "[NO_PLUGIN_INVOKE] No plugin invocation in this reply");
                }
            }
            catch (OperationCanceledException) { Logger.LogWarning(hid, "[PROCESS_ABORT] Task cancelled."); }
            catch (Exception ex) { Logger.LogError(hid, "Error during reply generation flow.", ex); }
            finally
            {
                if (session.MasterCts == thisTaskCts)
                {
                    session.MasterCts = null;
                    Logger.LogInfo(hid, "[STATE_RESET] Reply flow ended.");
                }
            }
        }

        private async Task SendAIRepliesStepByStep(List<dynamic> replyMsgs, CancellationToken token, UserSession session, string hid, List<dynamic> successfullySent)
        {
            long userId = session.UserId;
            var chatHistoryManager = _sessionManager.GetOrCreateChatHistoryManager(userId);

            foreach (var msg in replyMsgs)
            {
                token.ThrowIfCancellationRequested();

                int delay = 2000;
                try { delay = (int)(msg.delay_ms ?? 2000); } catch (Exception ex) { Logger.LogWarning(hid, $"[DELAY_PARSE_ERROR] Failed to parse delay_ms, using default 2000ms: {ex.Message}"); }

                Logger.LogInfo(hid, $"[BEHAVIOR_SIM] Simulating activity: Delaying {delay}ms for message");
                await Task.Delay(delay, token);

                token.ThrowIfCancellationRequested();

                object payload = null;

                if (msg.content != null)
                {
                    string text = msg.content.ToString();
                    payload = new { action = "send_msg", @params = new { user_id = userId, message = text } };
                    Logger.LogInfo(hid, $"[TEXT_MSG] Preparing to send text: \"{text}\"");
                }
                else if (msg.meme != null)
                {
                    string memeFileName = msg.meme.ToString();
                    string path = "file://" + Path.Combine(Environment.CurrentDirectory, "meme", memeFileName).Replace("\\", "/");
                    payload = new { action = "send_msg", @params = new { user_id = userId, message = new[] { new { type = "image", data = new { file = path } } } } };
                    Logger.LogInfo(hid, $"[MEME_MSG] Preparing to send meme: \"{memeFileName}\"");
                }

                if (payload != null)
                {
                    await _webSocketClient.SendMessageAsync(JsonConvert.SerializeObject(payload));
                    session.IncrementTotalMessages();
                    BroadcastStats();

                    // 创建深拷贝，避免引用问题
                    var msgCopy = JsonConvert.DeserializeObject<dynamic>(JsonConvert.SerializeObject(msg));
                    successfullySent.Add(msgCopy);
                    Logger.LogInfo(hid, $"[DEBUG] Message sent and added to successfullySent. Count={successfullySent.Count}");

                    if (msg.content != null)
                        chatHistoryManager.AddMessage("ai", msg.content.ToString());
                    else if (msg.meme != null)
                        chatHistoryManager.AddMessage("ai", null, msg.meme.ToString());
                }
            }

            session.InputState.LastMessageTime = DateTime.Now;
        }

        private void InterruptionAndPhysicalCleanup(UserSession session, string hid)
        {
            session.CancelMasterCts();

            var contextManager = _sessionManager.GetOrCreateContextManager(session.UserId);
            contextManager.RemoveFormatErrorMessages();
            contextManager.RemoveOrphanInternalTrigger();
        }

        private void StartIncompleteTimeout(UserSession session, string hid)
        {
            session.DisposeTimer();
            session.IncompleteTimeoutTimer = new System.Threading.Timer(async _ => {
                if (session.LatestHandlerId != hid) return;
                Logger.LogInfo(hid, "[TIMEOUT] Completeness check timed out. Forcing reply.");
                await CommitAndReplyAsync(session, hid);
            }, null, 20000, Timeout.Infinite);
        }

        public void CheckActiveChat(object state)
        {
            int currentHour = DateTime.Now.Hour;
            if (currentHour >= 23 || currentHour < 6) return;

            foreach (var userId in _sessionManager.GetActiveUserIds())
            {
                var userConfig = _userConfigManager.GetOrCreateUserConfig(userId);
                if (!userConfig.ProactiveChatEnabled) continue;

                var session = _sessionManager.GetSession(userId);
                if (session == null) continue;

                if ((DateTime.Now - session.InputState.LastMessageTime).TotalMinutes < 5) continue;

                var contextManager = _sessionManager.GetOrCreateContextManager(userId);
                if (contextManager.HasUpcomingEventWithin(TimeSpan.FromMinutes(5))) continue;

                if (_random.Next(100) >= userConfig.ActiveChatProbability) continue;

                if (session.MasterCts != null) continue;

                string hid = "ACTIVE_" + Guid.NewGuid().ToString("N").Substring(0, 4);
                InterruptionAndPhysicalCleanup(session, hid);
                _ = Task.Run(async () =>
                {
                    _ = await contextManager.AddUserMessageAsync($"{AppConstants.TAG_PROACTIVE} [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 请基于对话上下文决定是否主动聊天。严格JSON格式。不要刷屏。");
                    session.IncrementProactiveChats();
                    BroadcastStats();
                    Logger.LogInfo(hid, $"[EVENT] Triggering proactive engagement flow for user {userId}.");
                    await TriggerAIReplyFlow(session, hid);
                });
            }
        }

        public void CheckScheduledEvents(object state)
        {
            foreach (var userId in _sessionManager.GetActiveUserIds())
            {
                var userConfig = _userConfigManager.GetOrCreateUserConfig(userId);
                if (!userConfig.ReminderEnabled) continue;

                var session = _sessionManager.GetSession(userId);
                if (session == null) continue;

                var contextManager = _sessionManager.GetOrCreateContextManager(userId);
                var result = contextManager.GetDueEvents();

                if (result.EventsUpdated)
                {
                    _broadcastCallback?.Invoke(new WebSocketMessage { Type = "scheduled_events_updated", Data = contextManager.ScheduledEvents });
                }

                foreach (var ev in result.DueEvents)
                {
                    string hid = "REMIND_" + Guid.NewGuid().ToString("N").Substring(0, 4);
                    InterruptionAndPhysicalCleanup(session, hid);
                    _ = Task.Run(async () =>
                    {
                        _ = await contextManager.AddUserMessageAsync($"{AppConstants.TAG_REMINDER} [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 约定时间到了：{ev.Name}。请自然地进行对话。");
                        session.IncrementReminders();
                        BroadcastStats();
                        await TriggerAIReplyFlow(session, hid);
                    });
                }
            }
        }

        private void BroadcastStats()
        {
            var allStats = _sessionManager.GetAllSessionStats();
            _broadcastCallback?.Invoke(new WebSocketMessage { Type = "stats_updated", Data = allStats });
        }

        public List<SessionStats> GetAllSessionStats()
        {
            return _sessionManager.GetAllSessionStats();
        }

        public UserSessionManager SessionManager => _sessionManager;

        #region 插件调用处理

        /// <summary>
        /// 处理插件调用
        /// </summary>
        private async Task HandlePluginInvokeAsync(PluginInvokeRequest invokeRequest, long userId, string hid, IContextManager contextManager)
        {
            try
            {
                Logger.LogInfo(hid, $"[PLUGIN_INVOKE] Processing plugin invoke: {invokeRequest.PluginId}.{invokeRequest.CapabilityName}");

                if (_pluginManager == null)
                {
                    Logger.LogWarning(hid, "[PLUGIN_INVOKE] PluginManager not available");
                    return;
                }

                // 执行插件调用
                var result = _pluginManager.InvokePluginCapability(
                    invokeRequest.PluginId,
                    invokeRequest.CapabilityName,
                    invokeRequest.Parameters ?? new Dictionary<string, object>());

                // 构建结果消息
                var resultMessage = BuildPluginResultMessage(invokeRequest, result);

                // 将插件执行结果以USER角色添加到上下文
                await contextManager.AddUserMessageAsync(resultMessage);

                Logger.LogInfo(hid, $"[PLUGIN_INVOKE] Plugin executed: Success={result.Success}, ExecutionTime={result.ExecutionTimeMs}ms");

                // 触发新的AI回复流程，让大模型基于插件结果继续回复
                await TriggerAIReplyFlow(_sessionManager.GetOrCreateSession(userId), $"PLUGIN_{hid}");
            }
            catch (Exception ex)
            {
                Logger.LogError(hid, $"[PLUGIN_INVOKE] Failed to invoke plugin: {ex.Message}", ex);

                // 添加错误信息到上下文（USER角色）
                var errorMessage = $"[插件调用失败] 调用 {invokeRequest.PluginId}.{invokeRequest.CapabilityName} 时发生错误: {ex.Message}";
                await contextManager.AddUserMessageAsync(errorMessage);
            }
        }

        /// <summary>
        /// 构建插件结果消息
        /// </summary>
        private string BuildPluginResultMessage(PluginInvokeRequest request, PluginCapabilityResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[插件执行结果] 插件: {request.PluginId}, 能力: {request.CapabilityName}");

            if (result.Success)
            {
                sb.AppendLine("状态: 成功");
                if (result.Data != null)
                {
                    var dataStr = result.Data is string s ? s : JsonConvert.SerializeObject(result.Data);
                    sb.AppendLine($"结果: {dataStr}");
                }
            }
            else
            {
                sb.AppendLine("状态: 失败");
                sb.AppendLine($"错误: {result.ErrorMessage}");
            }

            sb.AppendLine($"执行耗时: {result.ExecutionTimeMs}ms");
            sb.AppendLine("请基于以上插件执行结果继续回复用户。");

            return sb.ToString();
        }

        #endregion
    }
}
