using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AI_Chat.Models;
using AI_Chat.Constants;
using AI_Chat.Managers;
using AI_Chat.Utils;
using Newtonsoft.Json;
using Message = AI_Chat.Models.Message;

namespace AI_Chat.Services
{
    public class VectorContextManager : IVectorContextManager
    {
        private readonly object _vectorLock = new object();
        private readonly object _eventLock = new object();
        private readonly object _messageLock = new object();
        
        private List<VectorEntry> _vectorEntries = new List<VectorEntry>();
        private List<Message> _recentMessages = new List<Message>();
        private List<EventModel> _scheduledEvents = new List<EventModel>();
        
        private readonly UserConfigManager _userConfigManager;
        private readonly LLMService _llmService;
        private readonly EmbeddingService _embeddingService;
        private readonly long _userId;
        private readonly DatabaseService _dbService;
        private bool _disposed;

        public VectorContextManager(
            ConfigManager configManager,
            LLMService llmService,
            EmbeddingService embeddingService,
            long userId = 0,
            UserConfigManager userConfigManager = null)
        {
            _userConfigManager = userConfigManager;
            _llmService = llmService;
            _embeddingService = embeddingService;
            _userId = userId;
            _dbService = new DatabaseService(userId);
        }

        private string GetBaseSystemPrompt()
        {
            return ConfigUtils.GetBaseSystemPrompt(_userConfigManager, _userId);
        }

        public long UserId => _userId;

        public List<Message> Context
        {
            get
            {
                lock (_messageLock) return _recentMessages.ToList();
            }
        }

        public List<EventModel> ScheduledEvents
        {
            get
            {
                lock (_eventLock) return _scheduledEvents.ToList();
            }
        }

        public List<VectorEntry> VectorEntries
        {
            get
            {
                lock (_vectorLock) return _vectorEntries.ToList();
            }
        }

        /// <summary>
        /// 获取分页的向量条目
        /// </summary>
        public (List<VectorEntry> Entries, int TotalCount) GetVectorEntriesPaged(int page, int pageSize)
        {
            lock (_vectorLock)
            {
                int totalCount = _vectorEntries.Count;
                var entries = _vectorEntries
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();
                return (entries, totalCount);
            }
        }

        public void LoadVectorFromDisk()
        {
            try
            {
                Logger.LogInfo("VECTOR_DB", $"Loading vectors from database (User: {_userId})");
                var savedVectors = _dbService.LoadVectorEntries();
                if (savedVectors != null && savedVectors.Count > 0)
                {
                    lock (_vectorLock)
                    {
                        _vectorEntries = savedVectors;
                    }
                    Logger.LogInfo("VECTOR_DB", $"Successfully loaded {savedVectors.Count} vector entries from database (User: {_userId})");
                }
                else
                {
                    Logger.LogInfo("VECTOR_DB", $"No vector entries found in database (User: {_userId})");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("VECTOR_DB", $"Failed to load vectors (User: {_userId}): " + ex.Message, ex);
            }
        }

        public void SaveVectorToDisk()
        {
            try
            {
                int count;
                lock (_vectorLock)
                {
                    count = _vectorEntries.Count;
                    _dbService.SaveVectorEntries(_vectorEntries);
                }
                Logger.LogInfo("VECTOR_DB", $"Saved {count} vector entries to database (User: {_userId})");
            }
            catch (Exception ex)
            {
                Logger.LogError("VECTOR_DB", $"Failed to save vectors (User: {_userId}): {ex.Message}", ex);
            }
        }

        public void LoadMessagesFromDisk()
        {
            try
            {
                var savedMessages = _dbService.LoadContextMessages(_userId);
                if (savedMessages != null && savedMessages.Count > 0)
                {
                    lock (_messageLock)
                    {
                        // 按时间戳排序，确保顺序正确
                        _recentMessages = savedMessages.OrderBy(m => m.Timestamp).ToList();
                    }
                    Logger.LogInfo("VECTOR_DB", $"Loaded {savedMessages.Count} recent messages from database (User: {_userId})");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("VECTOR_DB", $"Failed to load messages (User: {_userId}): " + ex.Message);
            }
        }

        public void SaveMessagesToDisk()
        {
            try
            {
                lock (_messageLock)
                {
                    // 保存消息到数据库（向量模式不需要截断，检索时只取最近N条）
                    _dbService.SaveContextMessages(_recentMessages, _userId);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("VECTOR_DB", $"Failed to save messages (User: {_userId}): {ex.Message}");
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
                    Logger.LogInfo("VECTOR_DB", $"Loaded {savedEvents.Count} historical scheduled events from database (User: {_userId})");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("VECTOR_DB", $"Failed to load events (User: {_userId}): " + ex.Message);
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
                Logger.LogError("VECTOR_DB", $"Failed to save events (User: {_userId}): {ex.Message}");
            }
        }

        public async Task<(bool isAppended, string fullMessage)> AddUserMessageAsync(string content)
        {
            bool isAppended = false;
            string fullMessage = content;
            string oldContent = null;
            bool isInternalTrigger = content.Contains(AppConstants.TAG_PROACTIVE) || content.Contains(AppConstants.TAG_REMINDER);
            
            lock (_messageLock)
            {
                if (_recentMessages.Count == 0 || _recentMessages[0].Role != "system")
                    _recentMessages.Insert(0, new Message { Role = "system", Content = GetBaseSystemPrompt(), Timestamp = DateTime.Now });

                var lastMsg = _recentMessages.LastOrDefault();
                bool lastMsgIsInternalTrigger = lastMsg != null &&
                    (lastMsg.Content.Contains(AppConstants.TAG_PROACTIVE) || lastMsg.Content.Contains(AppConstants.TAG_REMINDER));

                if (lastMsg != null && lastMsg.Role == "user" && !lastMsgIsInternalTrigger)
                {
                    oldContent = lastMsg.Content;  // 保存旧内容用于删除向量
                    lastMsg.Content += " " + content;
                    lastMsg.Timestamp = DateTime.Now;  // 更新时间为当前时间
                    fullMessage = lastMsg.Content;
                    isAppended = true;
                }
                else
                {
                    _recentMessages.Add(new Message { Role = "user", Content = content, Timestamp = DateTime.Now });
                    fullMessage = content;
                }
            }

            // 内部触发消息不存入向量数据库
            if (!isInternalTrigger)
            {
                if (isAppended && oldContent != null)
                {
                    // 消息被合并：删除旧向量，添加新向量
                    DeleteVectorEntryByContent(oldContent);
                    await AddVectorEntryAsync(fullMessage, "user");
                }
                else
                {
                    // 新消息：直接添加
                    await AddVectorEntryAsync(content, "user");
                }
            }
            SaveMessagesToDisk();
            return (isAppended, fullMessage);
        }

        public async Task AddAssistantMessageAsync(string content)
        {
            lock (_messageLock)
            {
                _recentMessages.Add(new Message { Role = "assistant", Content = content, Timestamp = DateTime.Now });
            }

            SaveMessagesToDisk();
        }

        public async Task AddAssistantMessageWithVectorAsync(string content)
        {
            lock (_messageLock)
            {
                _recentMessages.Add(new Message { Role = "assistant", Content = content, Timestamp = DateTime.Now });
            }

            await AddVectorEntryAsync(content, "assistant");
            SaveMessagesToDisk();
        }

        public void AddSystemMessage(string content)
        {
            lock (_messageLock)
            {
                _recentMessages.Add(new Message { Role = "system", Content = content, Timestamp = DateTime.Now });
            }
            SaveMessagesToDisk();
        }

        public void InsertSystemMessage(int index, string content)
        {
            lock (_messageLock)
            {
                _recentMessages.Insert(index, new Message { Role = "system", Content = content, Timestamp = DateTime.Now });
            }
            SaveMessagesToDisk();
        }

        public void UpdateUserMessage(int index, string newContent)
        {
            lock (_messageLock)
            {
                if (index >= 0 && index < _recentMessages.Count && _recentMessages[index].Role == "user")
                {
                    _recentMessages[index].Content = newContent;
                }
            }
            SaveMessagesToDisk();
        }

        public void RemoveFormatErrorMessages()
        {
            lock (_messageLock)
            {
                _recentMessages.RemoveAll(m => m.Content.Contains(AppConstants.TAG_FORMAT_ERROR));
            }
        }

        public void RemoveOrphanInternalTrigger()
        {
            lock (_messageLock)
            {
                if (_recentMessages.Count > 0)
                {
                    var last = _recentMessages.Last();
                    if (last.Role == "user" && (last.Content.Contains(AppConstants.TAG_PROACTIVE) || last.Content.Contains(AppConstants.TAG_REMINDER)))
                    {
                        _recentMessages.RemoveAt(_recentMessages.Count - 1);
                    }
                }
            }
        }

        public void ClearContext()
        {
            lock (_messageLock)
            {
                _recentMessages.Clear();
                _recentMessages.Add(new Message { Role = "system", Content = GetBaseSystemPrompt(), Timestamp = DateTime.Now });
            }
            
            lock (_vectorLock)
            {
                _vectorEntries.Clear();
            }
            
            lock (_eventLock)
            {
                _scheduledEvents.Clear();
            }
            
            SaveMessagesToDisk();
            SaveVectorToDisk();
            SaveEventsToDisk();
        }

        public async Task AddVectorEntryAsync(string content, string role)
        {
            // 过滤图片标签（CQ:image 和 [IMG:]），只保留纯文本内容用于向量检索
            string filteredContent = ImageService.FilterImageTags(content);

            // 如果过滤后内容为空，则不添加
            if (string.IsNullOrWhiteSpace(filteredContent))
            {
                Logger.LogInfo("VECTOR_DB", $"Skipping vector entry for user {_userId}, role: {role} - content is empty after filtering image tags");
                return;
            }

            var vector = await _embeddingService.GenerateEmbeddingAsync(filteredContent);

            // 如果向量生成失败，不添加到数据库
            if (vector == null)
            {
                Logger.LogError("VECTOR_DB", $"Failed to generate vector for user {_userId}, role: {role}, skipping entry");
                return;
            }

            var entry = new VectorEntry
            {
                Content = filteredContent,
                Role = role,
                UserId = _userId,
                Vector = vector
            };

            lock (_vectorLock)
            {
                _vectorEntries.Add(entry);
                _dbService.AddVectorEntry(entry);
            }

            Logger.LogInfo("VECTOR_DB", $"Added vector entry for user {_userId}, role: {role}");
        }

        public List<VectorEntry> SearchSimilar(string query, int topK = 5, float similarityThreshold = 0.2f)
        {
            var queryVector = _embeddingService.GenerateEmbedding(query);
            
            // 如果查询向量生成失败，返回空结果
            if (queryVector == null)
            {
                Logger.LogError("VECTOR_DB", "Failed to generate query vector, returning empty results");
                return new List<VectorEntry>();
            }
            
            Logger.LogInfo("VECTOR_DB", $"Searching vectors, query vector dimension: {queryVector.Length}, Total entries: {_vectorEntries.Count}, Threshold: {similarityThreshold:F4}");
            
            List<VectorEntry> results;
            lock (_vectorLock)
            {
                var entriesWithSimilarity = _vectorEntries
                    .Select(e => new { Entry = e, Similarity = _embeddingService.CosineSimilarity(queryVector, e.Vector) })
                    .OrderByDescending(x => x.Similarity)
                    .ToList();
                
                foreach (var item in entriesWithSimilarity.Take(Math.Min(10, entriesWithSimilarity.Count)))
                {
                    string status = item.Similarity >= similarityThreshold ? "✓" : "✗";
                    Logger.LogInfo("VECTOR_DB", $"  {status} Content: '{item.Entry.Content.Substring(0, Math.Min(30, item.Entry.Content.Length))}...', Similarity: {item.Similarity:F4}");
                }
                
                results = entriesWithSimilarity
                    .Where(x => x.Similarity >= similarityThreshold)
                    .Take(topK)
                    .Select(x => x.Entry)
                    .ToList();
            }
            
            Logger.LogInfo("VECTOR_DB", $"Returning {results.Count} results (above threshold)");
            return results;
        }

        public List<Message> GetContextForPrompt(string query, int maxRecentMessages = 10, float similarityThreshold = 0.2f)
        {
            var context = new List<Message>();
            List<Message> recentMessagesCopy;
            
            lock (_messageLock)
            {
                Logger.LogInfo("VECTOR_DB", $"Total recent messages in memory: {_recentMessages.Count}, maxRecentMessages: {maxRecentMessages}");
                recentMessagesCopy = _recentMessages.ToList();
            }
            
            // 1. 添加 base system prompt（角色设定）- 始终第一位
            var basePrompt = recentMessagesCopy.FirstOrDefault(m => m.Role == "system" && !m.Content.StartsWith("对话总结："));
            if (basePrompt != null)
            {
                context.Add(basePrompt);
            }
            else
            {
                context.Add(new Message { Role = "system", Content = GetBaseSystemPrompt(), Timestamp = DateTime.Now });
            }
            
            // 2. 获取非 system 消息用于去重
            var nonSystemMessages = recentMessagesCopy.Where(m => m.Role != "system").ToList();
            int takeCount = maxRecentMessages;
            int startIndex = 0;
            if (nonSystemMessages.Count > takeCount)
            {
                startIndex = nonSystemMessages.Count - takeCount;
            }
            var recentMessagesContents = nonSystemMessages.Skip(startIndex)
                .SelectMany(m => ExtractContentForComparison(m))
                .ToHashSet();
            
            // 3. 向量检索相关历史
            var similarEntries = SearchSimilar(query, 10, similarityThreshold);
            Logger.LogInfo("VECTOR_DB", $"Context retrieval found {similarEntries.Count} relevant entries (above threshold {similarityThreshold:F4})");
            
            var addedContents = new HashSet<string>();
            var relatedContexts = new List<string>();
            foreach (var entry in similarEntries)
            {
                // 排除最近对话中已有的内容
                if (!string.IsNullOrWhiteSpace(entry.Content) 
                    && !addedContents.Contains(entry.Content)
                    && !recentMessagesContents.Contains(entry.Content))
                {
                    string roleLabel = entry.Role == "user" ? "用户" : "助手";
                    relatedContexts.Add($"{roleLabel}: {entry.Content}");
                    addedContents.Add(entry.Content);
                }
            }
            
            // 4. 添加向量检索结果（作为 system 消息，放在 base prompt 之后）
            if (relatedContexts.Count > 0)
            {
                string combinedContext = "相关历史上下文:\n" + string.Join("\n", relatedContexts);
                context.Add(new Message 
                { 
                    Role = "system", 
                    Content = combinedContext,
                    Timestamp = DateTime.Now
                });
            }
            
            // 5. 添加最近的消息（最多 maxRecentMessages 条）
            lock (_messageLock)
            {
                var recentNonSystem = _recentMessages.Where(m => m.Role != "system").ToList();
                startIndex = 0;
                if (recentNonSystem.Count > maxRecentMessages)
                {
                    startIndex = recentNonSystem.Count - maxRecentMessages;
                }
                var messagesToAdd = recentNonSystem.Skip(startIndex).ToList();
                Logger.LogInfo("VECTOR_DB", $"Adding {messagesToAdd.Count} recent messages to context");
                context.AddRange(messagesToAdd);
            }
            
            Logger.LogInfo("VECTOR_DB", $"Final context has {context.Count} messages total");
            return context;
        }

        /// <summary>
        /// 提取消息内容用于比较（解析助手JSON消息）
        /// 过滤表情包，只保留纯文本内容列表，与向量数据库中的存储格式保持一致
        /// </summary>
        private List<string> ExtractContentForComparison(Message message)
        {
            if (message.Role != "assistant")
                return new List<string> { message.Content };

            // 对于助手消息，过滤表情包，只保留纯文本内容（与向量数据库中存储的格式一致）
            var contentParts = MessageUtils.ExtractContentPartsFromAIReply(message.Content);
            // 过滤掉表情包（格式为 [表情包:xxx]），只保留纯文本
            return contentParts.Where(p => !p.StartsWith("[表情包:")).ToList();
        }

        public void DeleteVectorEntry(string id)
        {
            lock (_vectorLock)
            {
                _vectorEntries.RemoveAll(e => e.Id == id);
                _dbService.DeleteVectorEntry(id);
            }
        }

        public void DeleteVectorEntryByContent(string content)
        {
            lock (_vectorLock)
            {
                var entriesToRemove = _vectorEntries.Where(e => e.Content == content).ToList();
                foreach (var entry in entriesToRemove)
                {
                    _vectorEntries.RemoveAll(e => e.Id == entry.Id);
                    _dbService.DeleteVectorEntry(entry.Id);
                }
            }
        }

        public void ClearVectors()
        {
            lock (_vectorLock)
            {
                _vectorEntries.Clear();
                _dbService.ClearVectors();
            }
        }

        public async Task RegenerateAllVectorsAsync()
        {
            Logger.LogInfo("VECTOR_DB", $"Starting to regenerate all vectors for user {_userId}");
            
            List<VectorEntry> oldEntries;
            lock (_vectorLock)
            {
                oldEntries = new List<VectorEntry>(_vectorEntries);
            }

            var newEntries = new List<VectorEntry>();
            int successCount = 0;
            int failCount = 0;

            foreach (var entry in oldEntries)
            {
                try
                {
                    var vector = await _embeddingService.GenerateEmbeddingAsync(entry.Content);
                    
                    // 如果向量生成失败，跳过该条目
                    if (vector == null)
                    {
                        failCount++;
                        Logger.LogError("VECTOR_DB", $"Failed to regenerate vector for: '{entry.Content.Substring(0, Math.Min(30, entry.Content.Length))}...' - embedding returned null");
                        continue;
                    }
                    
                    var newEntry = new VectorEntry
                    {
                        Content = entry.Content,
                        Role = entry.Role,
                        UserId = entry.UserId,
                        Vector = vector,
                        Metadata = entry.Metadata
                    };
                    newEntries.Add(newEntry);
                    successCount++;
                    Logger.LogInfo("VECTOR_DB", $"Regenerated vector for: '{entry.Content.Substring(0, Math.Min(30, entry.Content.Length))}...'");
                }
                catch (Exception ex)
                {
                    failCount++;
                    Logger.LogError("VECTOR_DB", $"Failed to regenerate vector for: '{entry.Content.Substring(0, Math.Min(30, entry.Content.Length))}...', Error: {ex.Message}");
                }
            }

            lock (_vectorLock)
            {
                _vectorEntries = newEntries;
            }

            SaveVectorToDisk();
            Logger.LogInfo("VECTOR_DB", $"Finished regenerating vectors. Success: {successCount}, Failed: {failCount}");
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
            return DateTimeUtils.TryParseRobustDateTime(timeStr, out result);
        }

        public int UserMessageCount
        {
            get
            {
                lock (_messageLock)
                {
                    return _recentMessages.Count(m => m.Role == "user" &&
                        !m.Content.Contains(AppConstants.TAG_PROACTIVE) &&
                        !m.Content.Contains(AppConstants.TAG_REMINDER));
                }
            }
        }

        public int LastUserMessageIndex
        {
            get
            {
                lock (_messageLock)
                {
                    return _recentMessages.FindLastIndex(m => m.Role == "user");
                }
            }
        }

        public int RemoveLastMessages(string role, int count)
        {
            int removed = 0;
            lock (_messageLock)
            {
                for (int i = _recentMessages.Count - 1; i >= 0 && removed < count; i--)
                {
                    if (_recentMessages[i].Role.Equals(role, StringComparison.OrdinalIgnoreCase))
                    {
                        _recentMessages.RemoveAt(i);
                        removed++;
                    }
                }
            }
            if (removed > 0)
            {
                SaveMessagesToDisk();
            }
            return removed;
        }

        public void LoadAllFromDisk()
        {
            LoadVectorFromDisk();
            LoadMessagesFromDisk();
            LoadEventsFromDisk();
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
