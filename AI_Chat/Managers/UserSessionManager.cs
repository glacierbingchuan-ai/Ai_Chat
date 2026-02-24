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
        private readonly ConcurrentDictionary<long, ContextManager> _contextManagers = new ConcurrentDictionary<long, ContextManager>();
        private readonly ConcurrentDictionary<long, ChatHistoryManager> _chatHistoryManagers = new ConcurrentDictionary<long, ChatHistoryManager>();
        
        private readonly ConfigManager _configManager;
        private readonly UserConfigManager _userConfigManager;
        private readonly LLMService _llmService;
        private readonly string _userDataBasePath;

        private readonly object _creationLock = new object();

        public UserSessionManager(ConfigManager configManager, LLMService llmService, UserConfigManager userConfigManager = null)
        {
            _configManager = configManager;
            _llmService = llmService;
            _userConfigManager = userConfigManager;
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

        public ContextManager GetOrCreateContextManager(long userId)
        {
            return _contextManagers.GetOrAdd(userId, id =>
            {
                lock (_creationLock)
                {
                    var contextManager = new ContextManager(_configManager, _llmService, id, _userConfigManager);
                    contextManager.LoadContextFromDisk();
                    contextManager.LoadEventsFromDisk();
                    return contextManager;
                }
            });
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

        public string GetUserContextPath(long userId)
        {
            string userDir = GetUserDirectory(userId);
            return Path.Combine(userDir, "context_persistence.json");
        }

        public string GetUserEventsPath(long userId)
        {
            string userDir = GetUserDirectory(userId);
            return Path.Combine(userDir, "events_persistence.json");
        }

        public string GetUserChatHistoryPath(long userId)
        {
            string userDir = GetUserDirectory(userId);
            return Path.Combine(userDir, "chat_history.json");
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
