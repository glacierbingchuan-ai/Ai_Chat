using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AI_Chat.Models;
using AI_Chat.Constants;
using AI_Chat.Managers;
using Message = AI_Chat.Models.Message;

namespace AI_Chat.Services
{
    public class ContextManager : IContextManager
    {
        private readonly object _contextLock = new object();
        private readonly object _eventLock = new object();
        private readonly object _summaryLock = new object();
        private List<Message> _context = new List<Message>();
        private List<EventModel> _scheduledEvents = new List<EventModel>();
        private readonly ConfigManager _configManager;
        private readonly UserConfigManager _userConfigManager;
        private readonly LLMService _llmService;
        private readonly long _userId;
        private readonly DatabaseService _dbService;
        private bool _disposed;

        public ContextManager(ConfigManager configManager, LLMService llmService, long userId = 0, UserConfigManager userConfigManager = null)
        {
            _configManager = configManager;
            _userConfigManager = userConfigManager;
            _llmService = llmService;
            _userId = userId;
            _dbService = new DatabaseService(userId);
        }

        private string GetBaseSystemPrompt()
        {
            if (_userConfigManager != null && _userId > 0)
            {
                var userConfig = _userConfigManager.GetUserConfig(_userId);
                if (userConfig != null && !string.IsNullOrEmpty(userConfig.BaseSystemPrompt))
                {
                    return userConfig.BaseSystemPrompt;
                }
            }
            return SystemPrompts.BASE_SYSTEM_PROMPT;
        }

        public long UserId => _userId;

        public List<Message> Context
        {
            get
            {
                lock (_contextLock) return _context.ToList();
            }
        }

        public List<EventModel> ScheduledEvents
        {
            get
            {
                lock (_eventLock) return _scheduledEvents.ToList();
            }
        }

        public void LoadContextFromDisk()
        {
            try
            {
                var savedContext = _dbService.LoadContextMessages(_userId);
                if (savedContext != null && savedContext.Count > 0)
                {
                    lock (_contextLock)
                    {
                        // 按时间戳排序，确保顺序正确
                        _context = savedContext.OrderBy(m => m.Timestamp).ToList();
                        
                        // 确保 base system prompt 在最前面，然后是其他 system 消息，最后是非 system 消息
                var basePrompt = _context.FirstOrDefault(m => m.Role == "system" && !m.Content.StartsWith("对话总结："));
                var summaryMsg = _context.FirstOrDefault(m => m.Role == "system" && m.Content.StartsWith("对话总结："));
                var otherSystemMessages = _context.Where(m => m.Role == "system" && m != basePrompt && m != summaryMsg).ToList();
                var nonSystemMessages = _context.Where(m => m.Role != "system").ToList();
                
                _context = new List<Message>();
                if (basePrompt != null) _context.Add(basePrompt);
                if (summaryMsg != null) _context.Add(summaryMsg);
                _context.AddRange(otherSystemMessages);
                _context.AddRange(nonSystemMessages);
                    }
                    Logger.LogInfo("PERSISTENCE", $"Loaded {savedContext.Count} historical context entries from database (User: {_userId}), Total in memory: {_context.Count}");
                    return;
                }
                Logger.LogInfo("PERSISTENCE", $"No historical context found in database, initializing new conversation (User: {_userId})");
            }
            catch (Exception ex)
            {
                Logger.LogError("PERSISTENCE", $"Failed to load context (User: {_userId}): " + ex.Message);
            }
        }

        public void SaveContextToDisk()
        {
            try
            {
                lock (_contextLock)
                {
                    // 保存上下文到数据库（不截断，让压缩机制决定何时压缩）
                    _dbService.SaveContextMessages(_context, _userId);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("PERSISTENCE", $"Failed to save context (User: {_userId}): {ex.Message}");
            }
        }

        public void LoadEventsFromDisk()
        {
            try
            {
                var savedEvents = _dbService.LoadEvents();
                if (savedEvents != null)
                {
                    lock (_eventLock)
                    {
                        _scheduledEvents = savedEvents;
                    }
                    Logger.LogInfo("PERSISTENCE", $"Loaded {savedEvents.Count} historical scheduled events from database (User: {_userId})");
                    return;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("PERSISTENCE", $"Failed to load events (User: {_userId}): " + ex.Message);
            }
        }

        public void SaveEventsToDisk()
        {
            try
            {
                lock (_eventLock)
                {
                    _dbService.SaveEvents(_scheduledEvents);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("PERSISTENCE", $"Failed to save events (User: {_userId}): {ex.Message}");
            }
        }

        public bool AddUserMessage(string content, out string fullMessage)
        {
            bool isAppended = false;
            fullMessage = content;
            lock (_contextLock)
            {
                if (_context.Count == 0 || _context[0].Role != "system")
                    _context.Insert(0, new Message { Role = "system", Content = GetBaseSystemPrompt(), Timestamp = DateTime.Now });

                var lastMsg = _context.LastOrDefault();
                bool isInternalTrigger = lastMsg != null &&
                    (lastMsg.Content.Contains(AppConstants.TAG_PROACTIVE) || lastMsg.Content.Contains(AppConstants.TAG_REMINDER));

                if (lastMsg != null && lastMsg.Role == "user" && !isInternalTrigger)
                {
                    lastMsg.Content += " " + content;
                    lastMsg.Timestamp = DateTime.Now;  // 更新时间为当前时间
                    fullMessage = lastMsg.Content;
                    isAppended = true;
                }
                else
                {
                    _context.Add(new Message { Role = "user", Content = content, Timestamp = DateTime.Now });
                    fullMessage = content;
                }
            }
            SaveContextToDisk();
            return isAppended;
        }

        public Task<(bool isAppended, string fullMessage)> AddUserMessageAsync(string content)
        {
            var result = AddUserMessage(content, out string fullMessage);
            return Task.FromResult((result, fullMessage));
        }

        public void AddAssistantMessage(string content)
        {
            lock (_contextLock)
            {
                _context.Add(new Message { Role = "assistant", Content = content, Timestamp = DateTime.Now });
            }
            SaveContextToDisk();
        }

        public Task AddAssistantMessageAsync(string content)
        {
            AddAssistantMessage(content);
            return Task.CompletedTask;
        }



        public void AddSystemMessage(string content)
        {
            lock (_contextLock)
            {
                _context.Add(new Message { Role = "system", Content = content, Timestamp = DateTime.Now });
            }
            SaveContextToDisk();
        }

        public void InsertSystemMessage(int index, string content)
        {
            lock (_contextLock)
            {
                _context.Insert(index, new Message { Role = "system", Content = content, Timestamp = DateTime.Now });
            }
            SaveContextToDisk();
        }

        public void UpdateUserMessage(int index, string newContent)
        {
            lock (_contextLock)
            {
                if (index >= 0 && index < _context.Count && _context[index].Role == "user")
                {
                    _context[index].Content = newContent;
                }
            }
            SaveContextToDisk();
        }

        public void RemoveFormatErrorMessages()
        {
            lock (_contextLock)
            {
                _context.RemoveAll(m => m.Content.Contains(AppConstants.TAG_FORMAT_ERROR));
            }
        }

        public void RemoveOrphanInternalTrigger()
        {
            lock (_contextLock)
            {
                if (_context.Count > 0)
                {
                    var last = _context.Last();
                    if (last.Role == "user" && (last.Content.Contains(AppConstants.TAG_PROACTIVE) || last.Content.Contains(AppConstants.TAG_REMINDER)))
                    {
                        _context.RemoveAt(_context.Count - 1);
                    }
                }
            }
        }

        public void ClearContext()
        {
            lock (_contextLock)
            {
                _context.Clear();
                _context.Add(new Message { Role = "system", Content = GetBaseSystemPrompt(), Timestamp = DateTime.Now });
            }
            SaveContextToDisk();

            lock (_eventLock)
            {
                _scheduledEvents.Clear();
            }
            SaveEventsToDisk();
        }

        private bool _isSummarizing = false;

        public async Task SummarizeContextAsync(string hid)
        {
            lock (_summaryLock) { if (_isSummarizing) return; _isSummarizing = true; }

            try
            {
                List<Message> messagesToSummarize;
                int countToSummarize;

                lock (_contextLock)
                {
                    countToSummarize = _context.Count - 1;
                    if (countToSummarize <= 1) return;
                    messagesToSummarize = _context.Take(countToSummarize).ToList();
                }

                string summary = await _llmService.SummarizeContextAsync(messagesToSummarize);
                if (string.IsNullOrWhiteSpace(summary)) return;

                lock (_contextLock)
                {
                    if (_context.Count >= countToSummarize)
                    {
                        _context.RemoveRange(0, countToSummarize);
                        _context.Insert(0, new Message { Role = "system", Content = "对话总结：" + summary, Timestamp = DateTime.Now });
                        _context.Insert(0, new Message { Role = "system", Content = GetBaseSystemPrompt(), Timestamp = DateTime.Now });
                    }
                }

                SaveContextToDisk();
                Logger.LogInfo(hid, "[MEMORY_OPTIMIZATION] Context exceeded threshold. Summary compression completed.");
            }
            catch (Exception ex)
            {
                Logger.LogError(hid, $"[MEMORY_OPTIMIZATION] Failed to summarize context: {ex.Message}");
            }
            finally { lock (_summaryLock) _isSummarizing = false; }
        }

        public List<Message> GetContextForPrompt(string query, int maxRecentMessages = 10, float similarityThreshold = 0.2f)
        {
            var context = new List<Message>();

            lock (_contextLock)
            {
                // 1. 添加 base system prompt（角色设定）
                var basePrompt = _context.FirstOrDefault(m => m.Role == "system" && !m.Content.StartsWith("对话总结："));
                if (basePrompt != null)
                {
                    context.Add(basePrompt);
                }
                else
                {
                    context.Add(new Message { Role = "system", Content = GetBaseSystemPrompt(), Timestamp = DateTime.Now });
                }

                // 2. 添加对话总结（如果有）
                var summaryMsg = _context.FirstOrDefault(m => m.Role == "system" && m.Content.StartsWith("对话总结："));
                if (summaryMsg != null)
                {
                    context.Add(summaryMsg);
                }

                // 3. 添加所有非 system 消息（压缩机制已经控制了长度，不需要再限制）
                var nonSystemMessages = _context.Where(m => m.Role != "system").ToList();
                context.AddRange(nonSystemMessages);
            }

            return context;
        }

        public void AddEvent(EventModel ev)
        {
            lock (_eventLock)
            {
                if (TryParseRobustDateTime(ev.Time, out DateTime parsedTime))
                {
                    string timeKey = parsedTime.ToString("yyyy-MM-dd HH:mm");
                    ev.Time = parsedTime.ToString("yyyy-MM-dd HH:mm:ss");
                    _scheduledEvents.RemoveAll(e => TryParseRobustDateTime(e.Time, out DateTime et) && et.ToString("yyyy-MM-dd HH:mm") == timeKey);
                    _scheduledEvents.Add(ev);
                }
            }
            SaveEventsToDisk();
        }

        public GetDueEventsResult GetDueEvents()
        {
            List<EventModel> dueEvents = new List<EventModel>();
            bool eventsUpdated = false;
            lock (_eventLock)
            {
                DateTime now = DateTime.Now;
                for (int i = _scheduledEvents.Count - 1; i >= 0; i--)
                {
                    if (TryParseRobustDateTime(_scheduledEvents[i].Time, out DateTime eventTime))
                    {
                        if (now >= eventTime)
                        {
                            dueEvents.Add(_scheduledEvents[i]);
                            _scheduledEvents.RemoveAt(i);
                            eventsUpdated = true;
                        }
                    }
                    else
                    {
                        _scheduledEvents.RemoveAt(i);
                        eventsUpdated = true;
                    }
                }
            }
            SaveEventsToDisk();
            return new GetDueEventsResult { DueEvents = dueEvents, EventsUpdated = eventsUpdated };
        }

        public bool HasUpcomingEventWithin(TimeSpan timeSpan)
        {
            lock (_eventLock)
            {
                DateTime now = DateTime.Now;
                DateTime targetTime = now.Add(timeSpan);
                return _scheduledEvents.Any(ev => TryParseRobustDateTime(ev.Time, out DateTime eventTime) && eventTime > now && eventTime <= targetTime);
            }
        }

        public bool TryParseRobustDateTime(string timeStr, out DateTime result)
        {
            if (DateTime.TryParse(timeStr, out result))
            {
                if (result.Year == 1) result = DateTime.Today.Add(result.TimeOfDay);
                return true;
            }
            var match = System.Text.RegularExpressions.Regex.Match(timeStr, @"(\d{1,2})[:：](\d{1,2})[:：](\d{1,2})");
            if (match.Success)
            {
                result = DateTime.Today.Add(new TimeSpan(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value)));
                return true;
            }
            return false;
        }

        public int UserMessageCount
        {
            get
            {
                lock (_contextLock)
                {
                    return _context.Count(m => m.Role == "user" &&
                        !m.Content.Contains(AppConstants.TAG_PROACTIVE) &&
                        !m.Content.Contains(AppConstants.TAG_REMINDER));
                }
            }
        }

        public int LastUserMessageIndex
        {
            get
            {
                lock (_contextLock)
                {
                    return _context.FindLastIndex(m => m.Role == "user");
                }
            }
        }

        public int RemoveLastMessages(string role, int count)
        {
            int removed = 0;
            lock (_contextLock)
            {
                for (int i = _context.Count - 1; i >= 0 && removed < count; i--)
                {
                    if (_context[i].Role.Equals(role, StringComparison.OrdinalIgnoreCase))
                    {
                        _context.RemoveAt(i);
                        removed++;
                    }
                }
            }
            if (removed > 0)
            {
                SaveContextToDisk();
            }
            return removed;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _dbService?.Dispose();
                _disposed = true;
            }
        }
    }
}
