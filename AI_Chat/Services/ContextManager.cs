using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AI_Chat.Models;
using AI_Chat.Constants;
using Newtonsoft.Json;

namespace AI_Chat.Services
{
    public class ContextManager
    {
        private readonly object _contextLock = new object();
        private readonly object _eventLock = new object();
        private readonly object _summaryLock = new object();
        private List<Message> _context = new List<Message>();
        private List<EventModel> _scheduledEvents = new List<EventModel>();
        private bool _isSummarizing = false;
        private readonly ConfigManager _configManager;
        private readonly LLMService _llmService;

        public ContextManager(ConfigManager configManager, LLMService llmService)
        {
            _configManager = configManager;
            _llmService = llmService;
        }

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
                if (File.Exists(AppConstants.CONTEXT_PERSISTENCE_PATH))
                {
                    string json = File.ReadAllText(AppConstants.CONTEXT_PERSISTENCE_PATH);
                    var savedContext = JsonConvert.DeserializeObject<List<Message>>(json);
                    if (savedContext != null && savedContext.Count > 0)
                    {
                        lock (_contextLock)
                        {
                            _context = savedContext;
                        }
                        Logger.LogInfo("PERSISTENCE", $"Loaded {savedContext.Count} historical context entries from local disk");
                        return;
                    }
                }
                Logger.LogInfo("PERSISTENCE", "No historical context found or file is empty, initializing new conversation");
            }
            catch (Exception ex)
            {
                Logger.LogError("PERSISTENCE", "Failed to load context: " + ex.Message);
            }
        }

        public void SaveContextToDisk()
        {
            try
            {
                lock (_contextLock)
                {
                    string json = JsonConvert.SerializeObject(_context, Formatting.Indented);
                    File.WriteAllText(AppConstants.CONTEXT_PERSISTENCE_PATH, json);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("PERSISTENCE", $"Failed to save context: {ex.Message}");
            }
        }

        public void LoadEventsFromDisk()
        {
            try
            {
                if (File.Exists(AppConstants.EVENTS_PERSISTENCE_PATH))
                {
                    string json = File.ReadAllText(AppConstants.EVENTS_PERSISTENCE_PATH);
                    var savedEvents = JsonConvert.DeserializeObject<List<EventModel>>(json);
                    if (savedEvents != null)
                    {
                        lock (_eventLock)
                        {
                            _scheduledEvents = savedEvents;
                        }
                        Logger.LogInfo("PERSISTENCE", $"Loaded {savedEvents.Count} historical scheduled events from local disk");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("PERSISTENCE", "Failed to load events: " + ex.Message);
            }
        }

        public void SaveEventsToDisk()
        {
            try
            {
                lock (_eventLock)
                {
                    string json = JsonConvert.SerializeObject(_scheduledEvents, Formatting.Indented);
                    File.WriteAllText(AppConstants.EVENTS_PERSISTENCE_PATH, json);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("PERSISTENCE", $"Failed to save events: {ex.Message}");
            }
        }

        public bool AddUserMessage(string content, out string fullMessage)
        {
            bool isAppended = false;
            fullMessage = content;
            lock (_contextLock)
            {
                if (_context.Count == 0 || _context[0].Role != "system")
                    _context.Insert(0, new Message { Role = "system", Content = _configManager.Config.BaseSystemPrompt });

                var lastMsg = _context.LastOrDefault();
                bool isInternalTrigger = lastMsg != null &&
                    (lastMsg.Content.Contains(AppConstants.TAG_PROACTIVE) || lastMsg.Content.Contains(AppConstants.TAG_REMINDER));

                if (lastMsg != null && lastMsg.Role == "user" && !isInternalTrigger)
                {
                    lastMsg.Content += " " + content;
                    fullMessage = lastMsg.Content;
                    isAppended = true;
                }
                else
                {
                    _context.Add(new Message { Role = "user", Content = content });
                    fullMessage = content;
                }
            }
            SaveContextToDisk();
            return isAppended;
        }

        public void AddAssistantMessage(string content)
        {
            lock (_contextLock)
            {
                _context.Add(new Message { Role = "assistant", Content = content });
            }
            SaveContextToDisk();
        }

        public void AddSystemMessage(string content)
        {
            lock (_contextLock)
            {
                _context.Add(new Message { Role = "system", Content = content });
            }
            SaveContextToDisk();
        }

        public void InsertSystemMessage(int index, string content)
        {
            lock (_contextLock)
            {
                _context.Insert(index, new Message { Role = "system", Content = content });
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
                _context.Add(new Message { Role = "system", Content = _configManager.Config.BaseSystemPrompt });
            }
            SaveContextToDisk();

            lock (_eventLock)
            {
                _scheduledEvents.Clear();
            }
            SaveEventsToDisk();
        }

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
                        _context.Insert(0, new Message { Role = "system", Content = "对话总结：" + summary });
                        _context.Insert(0, new Message { Role = "system", Content = _configManager.Config.BaseSystemPrompt });
                    }
                }

                SaveContextToDisk();
                Logger.LogInfo(hid, "[MEMORY_OPTIMIZATION] Context exceeded threshold. Summary compression completed.");
            }
            catch { }
            finally { lock (_summaryLock) _isSummarizing = false; }
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
    }
}
