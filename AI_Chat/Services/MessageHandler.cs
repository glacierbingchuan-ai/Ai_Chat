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
using Newtonsoft.Json;
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

            var chatHistoryManager = _sessionManager.GetOrCreateChatHistoryManager(userId);
            chatHistoryManager.AddMessage("user", rawContent);

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

            session.AppendToAccumulatedMessage(rawContent);

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

            bool isAppended = contextManager.AddUserMessage(finalizedMessage, out string fullMessage);
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
                if (contextManager.Context.Count > _configManager.Config.MaxContextRounds * 2)
                    await contextManager.SummarizeContextAsync(hid);

                AIReplyModel aiReply = null;
                int retryCount = 0;
                const int MAX_RETRIES = 6;
                bool isPluginIntercepted = false;

                while (retryCount < MAX_RETRIES)
                {
                    List<Message> contextCopy = contextManager.Context;
                    Logger.LogAIContext(hid, contextCopy);
                    Logger.LogInfo(hid, $"[LLM_REQUEST] Requesting reply (Attempt {retryCount + 1}/{MAX_RETRIES})...");

                    string lastUserMessage = contextCopy
                        .LastOrDefault(m => m.Role == "user")?.Content?.ToString();

                    string rawResponse = await _llmService.GetRawLLMResponseAsync(contextCopy, thisTaskCts.Token, lastUserMessage, userId);
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
                        break;
                    }
                    else
                    {
                        retryCount++;
                        Logger.LogWarning(hid, $"[SELF_CHECK_FAILED] Invalid JSON format or rule violation:{rawResponse}");
                        contextManager.AddSystemMessage($"{AppConstants.TAG_FORMAT_ERROR} 你的回复格式错误或未遵循规则，已被拦截，信息未发送给用户。错误原因可能是：1. 文字与表情包未完全分离；2. 文字消息中违规包含了[MEME_MSG]占位符；3. JSON语法错误。请严格按照JSON Schema重新输出，表情包必须单独放在messages数组的一个对象中，严禁在文字中包含[MEME_MSG]。你的回复内容：{rawResponse}");
                    }
                }

                if (!hid.StartsWith("ACTIVE_") && !hid.StartsWith("REMIND_"))
                {
                    if (thisTaskCts.IsCancellationRequested || session.LatestHandlerId != hid) return;
                }
                else if (thisTaskCts.IsCancellationRequested) return;

                if (aiReply == null)
                {
                    Logger.LogError(hid, "[PROCESS_FAILURE] Failed to get valid reply after retries.", null);
                    return;
                }

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
                            contextManager.AddAssistantMessage("[System record: AI reply intercepted by plugin]");
                        }
                        else
                        {
                            Logger.LogInfo(hid, "[LLM_RESPONSE] Model determined no response is necessary.");
                            contextManager.AddAssistantMessage("[System record: AI chose not to reply to this message]");
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
                    if (successfullySent.Count > 0)
                    {
                        var persistModel = new AIReplyModel
                        {
                            NeedReply = aiReply.NeedReply,
                            Events = aiReply.Events,
                            Messages = successfullySent
                        };

                        string partialJson = JsonConvert.SerializeObject(persistModel);
                        contextManager.AddAssistantMessage(partialJson);
                        Logger.LogInfo(hid, $"[PERSISTENCE] Successfully recorded {successfullySent.Count}/{aiReply.Messages.Count} message(s) in context.");
                    }
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
                try { delay = (int)(msg.delay_ms ?? 2000); } catch { }

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

                    successfullySent.Add(msg);

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
                contextManager.AddUserMessage($"{AppConstants.TAG_PROACTIVE} [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 请基于对话上下文决定是否主动聊天。严格JSON格式。不要刷屏。", out _);
                session.IncrementProactiveChats();
                BroadcastStats();
                Logger.LogInfo(hid, $"[EVENT] Triggering proactive engagement flow for user {userId}.");
                _ = Task.Run(() => TriggerAIReplyFlow(session, hid));
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
                    contextManager.AddUserMessage($"{AppConstants.TAG_REMINDER} [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 约定时间到了：{ev.Name}。请自然地进行对话。", out _);
                    session.IncrementReminders();
                    BroadcastStats();
                    _ = Task.Run(() => TriggerAIReplyFlow(session, hid));
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
    }
}
