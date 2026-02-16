using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AI_Chat.Models;
using AI_Chat.Constants;
using Newtonsoft.Json;
using AI_Chat.Plugins;

namespace AI_Chat.Services
{
    public class MessageHandler
    {
        private readonly ConfigManager _configManager;
        private readonly ContextManager _contextManager;
        private readonly LLMService _llmService;
        private readonly WebSocketClient _webSocketClient;
        private readonly ChatHistoryManager _chatHistoryManager;
        private readonly CancellationTokenSource _globalCts;
        private readonly Random _random = new Random();

        private readonly object _ctsLock = new object();
        private readonly object _inputStateLock = new object();
        private readonly object _processedMessagesLock = new object();

        private CancellationTokenSource _masterCts;
        private UserInputState _userInputState = new UserInputState();
        private HashSet<string> _processedMessages = new HashSet<string>();
        private string _latestHandlerId = "";
        private System.Threading.Timer _incompleteTimeoutTimer;

        private int _totalMessages = 0;
        private int _proactiveChats = 0;
        private int _reminders = 0;

        private Action<WebSocketMessage> _broadcastCallback;
        private PluginManager _pluginManager;
        private PluginApi _pluginApi;

        public MessageHandler(
            ConfigManager configManager,
            ContextManager contextManager,
            LLMService llmService,
            WebSocketClient webSocketClient,
            ChatHistoryManager chatHistoryManager,
            CancellationTokenSource globalCts)
        {
            _configManager = configManager;
            _contextManager = contextManager;
            _llmService = llmService;
            _webSocketClient = webSocketClient;
            _chatHistoryManager = chatHistoryManager;
            _globalCts = globalCts;
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

        public int TotalMessages => _totalMessages;
        public int ProactiveChats => _proactiveChats;
        public int Reminders => _reminders;

        public async Task HandleMessageAsync(string json)
        {
            string hid = Guid.NewGuid().ToString("N").Substring(0, 8);
            try
            {
                dynamic msgData = JsonConvert.DeserializeObject(json);
                if (msgData?.post_type != "message" || msgData?.message_type != "private" || (long)msgData?.user_id != _configManager.Config.TargetUserId) return;

                string messageId = msgData.message_id?.ToString();
                if (!string.IsNullOrEmpty(messageId))
                {
                    lock (_processedMessagesLock) { if (!_processedMessages.Add(messageId)) return; }
                }

                lock (_inputStateLock) { _latestHandlerId = hid; }

                string rawContent = msgData.raw_message?.ToString() ?? "";
                Logger.LogInfo(hid, $"[RECEPTION] Raw message fragment: \"{rawContent}\"");

                // 【重要】立即推送到前端显示并保存到历史记录（即使后续被插件拦截也要显示）
                _chatHistoryManager.AddMessage("user", rawContent);

                // 调用 IPluginApi 的 PreMerge 处理器（合并前处理原始消息）
                if (_pluginApi != null)
                {
                    var preMergeContext = new PreMergeMessageContext
                    {
                        RawMessage = rawContent,
                        Source = msgData.user_id?.ToString(),
                        Timestamp = DateTime.Now
                    };
                    
                    var preMergeResult = _pluginApi.HandlePreMergeMessage(preMergeContext);
                    
                    if (preMergeResult.IsIntercepted)
                    {
                        // Plugin intercepted the message, return response directly
                        Logger.LogInfo(hid, "[PLUGIN] Raw message intercepted by plugin");

                        // [IMPORTANT] Clear accumulated messages to prevent intercepted messages from affecting subsequent merges
                        lock (_inputStateLock)
                        {
                            if (_userInputState.AccumulatedMessage.Length > 0)
                            {
                                Logger.LogInfo(hid, "[PLUGIN] Clearing accumulated message buffer to avoid affecting subsequent message merging");
                                _userInputState.AccumulatedMessage.Clear();
                            }
                        }

                        if (preMergeResult.Response != null)
                        {
                            await SendPluginResponseAsync(preMergeResult.Response, hid);
                        }
                        return;
                    }

                    if (preMergeResult.IsModified)
                    {
                        // Plugin modified the message
                        rawContent = preMergeResult.ModifiedMessage;
                        Logger.LogInfo(hid, $"[PLUGIN] Raw message modified by plugin: \"{rawContent}\"");
                    }
                }

                InterruptionAndPhysicalCleanup(hid);

                lock (_inputStateLock)
                {
                    if (_userInputState.AccumulatedMessage.Length > 0) _userInputState.AccumulatedMessage.Append(" ");
                    _userInputState.AccumulatedMessage.Append(rawContent);
                    _userInputState.LastMessageTime = DateTime.Now;
                }

                string draft;
                lock (_inputStateLock) { draft = _userInputState.AccumulatedMessage.ToString(); }

                CompletenessLevel level = CompletenessLevel.Complete;
                if (_configManager.Config.IntentAnalysisEnabled)
                {
                    Logger.LogInfo(hid, "[INTENT_ANALYSIS] Invoking LLM for message completeness verification...");
                    level = await _llmService.IsUserMessageCompleteAsync(draft, hid);
                    Logger.LogInfo(hid, $"[ANALYSIS_RESULT] Determined status: {level}");
                }
                else
                {
                    Logger.LogInfo(hid, "[INTENT_ANALYSIS] Intent analysis disabled. Skipping message completeness verification.");
                }

                if (level == CompletenessLevel.Incomplete)
                {
                    Logger.LogInfo(hid, "[STATE_UPDATE] Completeness: INCOMPLETE. Buffering draft and awaiting further input.");
                    StartIncompleteTimeout(hid);
                    return;
                }

                if (level == CompletenessLevel.Uncertain)
                {
                    Logger.LogInfo(hid, "[STATE_UPDATE] Completeness: UNCERTAIN. Commencing 5000ms observation window...");
                    DateTime waitStart = DateTime.Now;
                    while (DateTime.Now - waitStart < TimeSpan.FromSeconds(5))
                    {
                        await Task.Delay(200);
                        lock (_inputStateLock)
                        {
                            if (_userInputState.LastMessageTime > waitStart || _latestHandlerId != hid)
                            {
                                Logger.LogInfo(hid, "[OBSERVATION] Newer message or task priority detected. Aborting current handler.");
                                return;
                            }
                        }
                    }
                    Logger.LogInfo(hid, "[OBSERVATION] Observation window closed with no new input. Proceeding to reply.");
                }

                lock (_inputStateLock) { if (_latestHandlerId != hid) return; }

                await CommitAndReplyAsync(hid);
            }
            catch (Exception ex) { Logger.LogError(hid, "Critical error during message handling pipeline.", ex); }
        }

        private async Task CommitAndReplyAsync(string hid)
        {
            string finalizedMessage = "";
            lock (_inputStateLock)
            {
                finalizedMessage = _userInputState.AccumulatedMessage.ToString().Trim();
                if (string.IsNullOrEmpty(finalizedMessage)) return;
                _userInputState.AccumulatedMessage.Clear();
                _incompleteTimeoutTimer?.Dispose();
                _incompleteTimeoutTimer = null;
            }

            // 调用 IPluginApi 的 PostMerge 处理器（合并后处理完整消息）
            if (_pluginApi != null)
            {
                var postMergeContext = new PostMergeMessageContext
                {
                    FullMessage = finalizedMessage,
                    Source = "user",
                    Timestamp = DateTime.Now,
                    MessageFragments = new List<string> { finalizedMessage }
                };
                
                var postMergeResult = _pluginApi.HandlePostMergeMessage(postMergeContext);
                
                if (postMergeResult.IsIntercepted)
                {
                    // Plugin intercepted the message, return response directly
                    Logger.LogInfo(hid, "[PLUGIN] Full message intercepted by plugin");
                    if (postMergeResult.Response != null)
                    {
                        await SendPluginResponseAsync(postMergeResult.Response, hid);
                    }
                    return;
                }

                if (postMergeResult.IsModified)
                {
                    // Plugin modified the message
                    finalizedMessage = postMergeResult.ModifiedMessage;
                    Logger.LogInfo(hid, $"[PLUGIN] Full message modified by plugin: \"{finalizedMessage}\"");
                }
            }

            bool isAppended = _contextManager.AddUserMessage(finalizedMessage, out string fullMessage);
            if (isAppended)
            {
                Logger.LogInfo(hid, $"[CONTEXT_FUSION] Appended message to existing user turn: \"{fullMessage}\"");
                
                // 调用插件处理追加完成的消息
                if (_pluginApi != null)
                {
                    int msgIndex = _contextManager.LastUserMessageIndex;
                    var appendedContext = new AI_Chat.Plugins.MessageAppendedContext
                    {
                        OriginalMessage = fullMessage.Substring(0, fullMessage.Length - finalizedMessage.Length - 1),
                        AppendedContent = finalizedMessage,
                        FullMessage = fullMessage,
                        MessageIndex = msgIndex
                    };
                    
                    var appendedResult = _pluginApi.HandleMessageAppended(appendedContext);
                    
                    // Check if intercepted
                    if (appendedResult.IsIntercepted)
                    {
                        Logger.LogInfo(hid, $"[PLUGIN] Appended message intercepted by plugin");
                        if (!string.IsNullOrEmpty(appendedResult.Response))
                        {
                            await SendPluginResponseAsync(appendedResult.Response, hid);
                        }
                        return;
                    }

                    if (appendedResult.IsModified)
                    {
                        _contextManager.UpdateUserMessage(msgIndex, appendedResult.ModifiedMessage);
                        fullMessage = appendedResult.ModifiedMessage;
                        Logger.LogInfo(hid, $"[PLUGIN] Appended message modified by plugin: \"{fullMessage}\"");
                    }
                }
            }
            else
            {
                Logger.LogInfo(hid, $"[CONTEXT_COMMIT] New user dialogue turn recorded: \"{fullMessage}\"");
            }

            // 注意：消息在收到时已经立即推送到前端显示并保存到历史记录，这里不需要任何操作

            Logger.LogAIContext(hid, _contextManager.Context);
            await TriggerAIReplyFlow(hid);
        }

        /// <summary>
        /// Send plugin response
        /// </summary>
        private async Task SendPluginResponseAsync(object response, string hid)
        {
            try
            {
                string text = response?.ToString();
                if (!string.IsNullOrEmpty(text))
                {
                    Logger.LogInfo(hid, $"[PLUGIN_RESPONSE] Sending plugin response: {text}");

                    var payload = new { action = "send_msg", @params = new { user_id = _configManager.Config.TargetUserId, message = text } };
                    await _webSocketClient.SendMessageAsync(JsonConvert.SerializeObject(payload));

                    _totalMessages++;
                    _broadcastCallback?.Invoke(new WebSocketMessage { Type = "stats_updated", Data = new { totalMessages = _totalMessages, proactiveChats = _proactiveChats, reminders = _reminders } });
                    _chatHistoryManager.AddMessage("ai", text);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(hid, $"[PLUGIN_RESPONSE] Failed to send plugin response: {ex.Message}", ex);
            }
        }

        private async Task TriggerAIReplyFlow(string hid)
        {
            CancellationTokenSource thisTaskCts;
            lock (_ctsLock)
            {
                _masterCts = new CancellationTokenSource();
                thisTaskCts = _masterCts;
            }

            try
            {
                if (_contextManager.Context.Count > _configManager.Config.MaxContextRounds * 2)
                    await _contextManager.SummarizeContextAsync(hid);

                AIReplyModel aiReply = null;
                int retryCount = 0;
                const int MAX_RETRIES = 6;
                bool isPluginIntercepted = false; // 标记是否被插件拦截

                while (retryCount < MAX_RETRIES)
                {
                    List<Message> contextCopy = _contextManager.Context;
                    Logger.LogAIContext(hid, contextCopy);
                    Logger.LogInfo(hid, $"[LLM_REQUEST] Requesting reply (Attempt {retryCount + 1}/{MAX_RETRIES})...");

                    string rawResponse = await _llmService.GetRawLLMResponseAsync(contextCopy, thisTaskCts.Token);
                    if (string.IsNullOrEmpty(rawResponse))
                    {
                        Logger.LogWarning(hid, "[LLM_REQUEST] LLM API returned empty response, triggering retry");
                        await Task.Delay(1000, thisTaskCts.Token);
                        retryCount++;
                        continue;
                    }

                    // 调用 IPluginApi 的 LLMResponse 处理器
                    if (_pluginApi != null)
                    {
                        var llmContext = new LLMResponseContext
                        {
                            RawResponse = rawResponse,
                            RequestId = hid
                        };
                        
                        var llmResult = _pluginApi.HandleLLMResponse(llmContext);
                        
                        if (llmResult.IsIntercepted)
                        {
                            // Plugin intercepted the response
                            Logger.LogInfo(hid, "[PLUGIN] LLM response intercepted by plugin");
                            if (!string.IsNullOrEmpty(llmResult.AlternativeResponse))
                            {
                                // Use alternative response provided by plugin
                                rawResponse = llmResult.AlternativeResponse;
                            }
                            else
                            {
                                // Plugin intercepted but did not provide alternative response, skip this reply
                                aiReply = new AIReplyModel { NeedReply = false, Messages = new List<dynamic>(), Events = new List<EventModel>() };
                                isPluginIntercepted = true; // Mark as plugin intercepted
                                break;
                            }
                        }
                        else if (llmResult.IsModified && !string.IsNullOrEmpty(llmResult.AlternativeResponse))
                        {
                            // Plugin modified the response, use modified JSON
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
                        _contextManager.AddSystemMessage($"{AppConstants.TAG_FORMAT_ERROR} 你的回复格式错误或未遵循规则，已被拦截，信息未发送给用户。错误原因可能是：1. 文字与表情包未完全分离；2. 文字消息中违规包含了[MEME_MSG]占位符；3. JSON语法错误。请严格按照JSON Schema重新输出，表情包必须单独放在messages数组的一个对象中，严禁在文字中包含[MEME_MSG]。你的回复内容：{rawResponse}");
                    }
                }

                lock (_inputStateLock)
                {
                    if (!hid.StartsWith("ACTIVE_") && !hid.StartsWith("REMIND_"))
                    {
                        if (thisTaskCts.IsCancellationRequested || _latestHandlerId != hid) return;
                    }
                    else if (thisTaskCts.IsCancellationRequested) return;
                }

                if (aiReply == null)
                {
                    Logger.LogError(hid, "[PROCESS_FAILURE] Failed to get valid reply after retries.", null);
                    return;
                }

                if (aiReply.Events != null && aiReply.Events.Count > 0)
                {
                    foreach (var ev in aiReply.Events)
                    {
                        _contextManager.AddEvent(ev);
                        Logger.LogInfo(hid, $"[EVENT_STORED] Recorded event: {ev.Name} at {ev.Time}");
                    }
                    _broadcastCallback?.Invoke(new WebSocketMessage { Type = "scheduled_events_updated", Data = _contextManager.ScheduledEvents });
                }

                _contextManager.RemoveFormatErrorMessages();

                if (!aiReply.NeedReply || aiReply.Messages.Count == 0)
                {
                    bool isInternal = hid.StartsWith("ACTIVE_") || hid.StartsWith("REMIND_");
                    if (!isInternal)
                    {
                        // Distinguish between plugin interception and AI choosing not to reply
                        if (isPluginIntercepted)
                        {
                            Logger.LogInfo(hid, "[LLM_RESPONSE] AI reply intercepted by plugin, not sent to user.");
                            _contextManager.AddAssistantMessage("[System record: AI reply intercepted by plugin]");
                        }
                        else
                        {
                            Logger.LogInfo(hid, "[LLM_RESPONSE] Model determined no response is necessary.");
                            _contextManager.AddAssistantMessage("[System record: AI chose not to reply to this message]");
                        }
                    }
                    else
                        _contextManager.RemoveOrphanInternalTrigger();
                    return;
                }

                Logger.LogInfo(hid, $"[LLM_RESPONSE] Generated {aiReply.Messages.Count} message(s). Commencing phased execution.");

                List<dynamic> successfullySent = new List<dynamic>();
                try
                {
                    await SendAIRepliesStepByStep(aiReply.Messages, thisTaskCts.Token, hid, successfullySent);
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
                        _contextManager.AddAssistantMessage(partialJson);
                        Logger.LogInfo(hid, $"[PERSISTENCE] Successfully recorded {successfullySent.Count}/{aiReply.Messages.Count} message(s) in context.");
                    }
                }
            }
            catch (OperationCanceledException) { Logger.LogWarning(hid, "[PROCESS_ABORT] Task cancelled."); }
            catch (Exception ex) { Logger.LogError(hid, "Error during reply generation flow.", ex); }
            finally
            {
                lock (_ctsLock)
                {
                    if (_masterCts == thisTaskCts)
                    {
                        _masterCts.Dispose();
                        _masterCts = null;
                        Logger.LogInfo(hid, "[STATE_RESET] Reply flow ended.");
                    }
                }
            }
        }

        private async Task SendAIRepliesStepByStep(List<dynamic> replyMsgs, CancellationToken token, string hid, List<dynamic> successfullySent)
        {
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
                    payload = new { action = "send_msg", @params = new { user_id = _configManager.Config.TargetUserId, message = text } };
                    Logger.LogInfo(hid, $"[TEXT_MSG] Preparing to send text: \"{text}\"");
                }
                else if (msg.meme != null)
                {
                    string memeFileName = msg.meme.ToString();
                    string path = "file://" + Path.Combine(Environment.CurrentDirectory, "meme", memeFileName).Replace("\\", "/");
                    payload = new { action = "send_msg", @params = new { user_id = _configManager.Config.TargetUserId, message = new[] { new { type = "image", data = new { file = path } } } } };
                    Logger.LogInfo(hid, $"[MEME_MSG] Preparing to send meme: \"{memeFileName}\"");
                }

                if (payload != null)
                {
                    await _webSocketClient.SendMessageAsync(JsonConvert.SerializeObject(payload));
                    _totalMessages++;
                    _broadcastCallback?.Invoke(new WebSocketMessage { Type = "stats_updated", Data = new { totalMessages = _totalMessages, proactiveChats = _proactiveChats, reminders = _reminders } });

                    successfullySent.Add(msg);

                    if (msg.content != null)
                        _chatHistoryManager.AddMessage("ai", msg.content.ToString());
                    else if (msg.meme != null)
                        _chatHistoryManager.AddMessage("ai", null, msg.meme.ToString());
                }
            }

            lock (_inputStateLock) { _userInputState.LastMessageTime = DateTime.Now; }
        }

        private void InterruptionAndPhysicalCleanup(string hid)
        {
            lock (_ctsLock)
            {
                if (_masterCts != null)
                {
                    Logger.LogWarning(hid, "[INTERRUPT] User concurrency detected. Terminating pending response generation.");
                    _masterCts.Cancel();
                    _masterCts = null;
                }
            }

            _contextManager.RemoveFormatErrorMessages();
            _contextManager.RemoveOrphanInternalTrigger();
        }

        private void StartIncompleteTimeout(string hid)
        {
            _incompleteTimeoutTimer?.Dispose();
            _incompleteTimeoutTimer = new System.Threading.Timer(async _ => {
                lock (_inputStateLock) { if (_latestHandlerId != hid) return; }
                Logger.LogInfo(hid, "[TIMEOUT] Completeness check timed out. Forcing reply.");
                await CommitAndReplyAsync(hid);
            }, null, 20000, Timeout.Infinite);
        }

        public void CheckActiveChat(object state)
        {
            int currentHour = DateTime.Now.Hour;
            if (currentHour >= 23 || currentHour < 6) return;

            if (!_configManager.Config.ProactiveChatEnabled) return;

            lock (_inputStateLock)
            {
                if ((DateTime.Now - _userInputState.LastMessageTime).TotalMinutes < 5) return;
            }

            if (_contextManager.HasUpcomingEventWithin(TimeSpan.FromMinutes(5))) return;

            if (_random.Next(100) >= _configManager.Config.ActiveChatProbability) return;

            lock (_ctsLock) { if (_masterCts != null) return; }

            string hid = "ACTIVE_" + Guid.NewGuid().ToString("N").Substring(0, 4);
            InterruptionAndPhysicalCleanup(hid);
            _contextManager.AddUserMessage($"{AppConstants.TAG_PROACTIVE} [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 请基于对话上下文决定是否主动聊天。严格JSON格式。不要刷屏。", out _);
            _proactiveChats++;
            _broadcastCallback?.Invoke(new WebSocketMessage { Type = "stats_updated", Data = new { totalMessages = _totalMessages, proactiveChats = _proactiveChats, reminders = _reminders } });
            Logger.LogInfo(hid, "[EVENT] Triggering proactive engagement flow.");
            _ = Task.Run(() => TriggerAIReplyFlow(hid));
        }

        public void CheckScheduledEvents(object state)
        {
            if (!_configManager.Config.ReminderEnabled) return;

            var result = _contextManager.GetDueEvents();

            if (result.EventsUpdated)
            {
                _broadcastCallback?.Invoke(new WebSocketMessage { Type = "scheduled_events_updated", Data = _contextManager.ScheduledEvents });
            }

            foreach (var ev in result.DueEvents)
            {
                string hid = "REMIND_" + Guid.NewGuid().ToString("N").Substring(0, 4);
                InterruptionAndPhysicalCleanup(hid);
                _contextManager.AddUserMessage($"{AppConstants.TAG_REMINDER} [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 约定时间到了：{ev.Name}。请自然地进行对话。", out _);
                _reminders++;
                _broadcastCallback?.Invoke(new WebSocketMessage { Type = "stats_updated", Data = new { totalMessages = _totalMessages, proactiveChats = _proactiveChats, reminders = _reminders } });
                _ = Task.Run(() => TriggerAIReplyFlow(hid));
            }
        }

    }
}
