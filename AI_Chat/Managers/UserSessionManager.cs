using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AI_Chat.Models;
using AI_Chat.Services;

namespace AI_Chat.Managers
{
    public class UserSessionManager
    {
        private readonly ConcurrentDictionary<long, UserSession> _sessions = new ConcurrentDictionary<long, UserSession>();
        private readonly ConcurrentDictionary<long, IContextManager> _contextManagers = new ConcurrentDictionary<long, IContextManager>();
        private readonly ConcurrentDictionary<long, ChatHistoryManager> _chatHistoryManagers = new ConcurrentDictionary<long, ChatHistoryManager>();
        
        private readonly ConfigManager _configManager;
        private readonly UserConfigManager _userConfigManager;
        private readonly LLMService _llmService;
        private readonly EmbeddingService _embeddingService;
        private readonly string _userDataBasePath;

        private readonly object _creationLock = new object();

        public UserSessionManager(ConfigManager configManager, LLMService llmService, UserConfigManager userConfigManager = null, RequestRateLimiter requestRateLimiter = null)
        {
            _configManager = configManager;
            _llmService = llmService;
            _userConfigManager = userConfigManager;
            _embeddingService = new EmbeddingService(configManager, requestRateLimiter);
            _userDataBasePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UserData");
            
            if (!Directory.Exists(_userDataBasePath))
            {
                Directory.CreateDirectory(_userDataBasePath);
            }
        }

        public UserSession GetOrCreateSession(long userId)
        {
            return _sessions.GetOrAdd(userId, id => new UserSession(id));
        }

        public UserSession GetSession(long userId)
        {
            _sessions.TryGetValue(userId, out var session);
            return session;
        }

        public IContextManager GetOrCreateContextManager(long userId)
        {
            return _contextManagers.GetOrAdd(userId, id =>
            {
                lock (_creationLock)
                {
                    IContextManager contextManager;
                    if (_configManager.Config.UseVectorContext)
                    {
                        var vectorManager = new VectorContextManager(_configManager, _llmService, _embeddingService, id, _userConfigManager);
                        vectorManager.LoadAllFromDisk();
                        contextManager = vectorManager;
                    }
                    else
                    {
                        var normalManager = new ContextManager(_configManager, _llmService, id, _userConfigManager);
                        normalManager.LoadContextFromDisk();
                        normalManager.LoadEventsFromDisk();
                        contextManager = normalManager;
                    }
                    return contextManager;
                }
            });
        }

        /// <summary>
        /// 获取 IVectorContextManager（仅在向量模式下使用，用于向量特定操作）
        /// </summary>
        public IVectorContextManager GetVectorContextManager(long userId)
        {
            var manager = GetOrCreateContextManager(userId);
            if (manager is IVectorContextManager vectorManager)
            {
                return vectorManager;
            }
            return null;
        }

        public ChatHistoryManager GetOrCreateChatHistoryManager(long userId)
        {
            return _chatHistoryManagers.GetOrAdd(userId, id =>
            {
                lock (_creationLock)
                {
                    var chatHistoryManager = new ChatHistoryManager(id);
                    chatHistoryManager.LoadChatHistoryFromDisk();
                    return chatHistoryManager;
                }
            });
        }

        public string GetUserDirectory(long userId)
        {
            string userDir = Path.Combine(_userDataBasePath, userId.ToString());
            if (!Directory.Exists(userDir))
            {
                Directory.CreateDirectory(userDir);
            }
            return userDir;
        }

        public List<long> GetActiveUserIds()
        {
            return _sessions.Keys.ToList();
        }

        public List<SessionStats> GetAllSessionStats()
        {
            return _sessions.Values.Select(s => s.GetStats()).ToList();
        }
    }
}
