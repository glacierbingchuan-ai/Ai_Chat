using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using AI_Chat.Models;
using AI_Chat.Constants;

namespace AI_Chat.Services
{
    public class ChatHistoryManager
    {
        private readonly object _chatHistoryLock = new object();
        private List<ChatMessage> _chatHistory = new List<ChatMessage>();
        private Action<ChatMessage> _broadcastMessageCallback;
        private Action _broadcastHistoryCallback;
        private readonly long _userId;
        private readonly DatabaseService _databaseService;

        public ChatHistoryManager(long userId = 0)
        {
            _userId = userId;
            _databaseService = new DatabaseService(userId);
        }

        public long UserId => _userId;

        public void Initialize(Action<ChatMessage> broadcastMessageCallback, Action broadcastHistoryCallback)
        {
            _broadcastMessageCallback = broadcastMessageCallback;
            _broadcastHistoryCallback = broadcastHistoryCallback;
        }

        /// <summary>
        /// 从数据库加载聊天历史
        /// </summary>
        public void LoadChatHistoryFromDisk()
        {
            try
            {
                var messages = _databaseService.LoadChatMessages(_userId, AppConstants.MAX_CHAT_HISTORY);
                lock (_chatHistoryLock)
                {
                    _chatHistory = messages;
                }
                Logger.LogInfo("PERSISTENCE", $"Loaded {messages.Count} chat messages from database (User: {_userId})");
            }
            catch (Exception ex)
            {
                Logger.LogError("PERSISTENCE", $"Failed to load chat history (User: {_userId}): " + ex.Message);
            }
        }

        public void AddMessage(string role, string content = null, string meme = null)
        {
            string safeContent = content != null ? WebUtility.HtmlEncode(content) : null;
            string safeMeme = meme != null ? WebUtility.HtmlEncode(meme) : null;

            var chatMessage = new ChatMessage
            {
                Id = Guid.NewGuid().ToString("N"),
                Role = role,
                Content = safeContent,
                Meme = safeMeme,
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };

            lock (_chatHistoryLock)
            {
                _chatHistory.Add(chatMessage);
                if (_chatHistory.Count > AppConstants.MAX_CHAT_HISTORY)
                {
                    _chatHistory = _chatHistory.Skip(_chatHistory.Count - AppConstants.MAX_CHAT_HISTORY).ToList();
                }
            }

            _databaseService.SaveChatMessage(chatMessage, _userId);
            _broadcastMessageCallback?.Invoke(chatMessage);
        }

        /// <summary>
        /// 添加消息到历史记录，但不触发广播（用于消息已在前端显示的场景）
        /// </summary>
        public void AddMessageWithoutBroadcast(string role, string content = null, string meme = null)
        {
            string safeContent = content != null ? WebUtility.HtmlEncode(content) : null;
            string safeMeme = meme != null ? WebUtility.HtmlEncode(meme) : null;

            var chatMessage = new ChatMessage
            {
                Id = Guid.NewGuid().ToString("N"),
                Role = role,
                Content = safeContent,
                Meme = safeMeme,
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };

            lock (_chatHistoryLock)
            {
                _chatHistory.Add(chatMessage);
                if (_chatHistory.Count > AppConstants.MAX_CHAT_HISTORY)
                {
                    _chatHistory = _chatHistory.Skip(_chatHistory.Count - AppConstants.MAX_CHAT_HISTORY).ToList();
                }
            }

            _databaseService.SaveChatMessage(chatMessage, _userId);
        }

        public void ClearHistory()
        {
            lock (_chatHistoryLock)
            {
                _chatHistory.Clear();
            }

            _databaseService.ClearChatMessages(_userId);
            _broadcastHistoryCallback?.Invoke();
        }

        /// <summary>
        /// 删除指定角色的最后N条消息
        /// </summary>
        /// <param name="role">角色：user, assistant, system</param>
        /// <param name="count">删除数量</param>
        /// <returns>实际删除的数量</returns>
        public int RemoveLastMessages(string role, int count)
        {
            int removed = 0;
            List<string> removedIds = new List<string>();

            lock (_chatHistoryLock)
            {
                for (int i = _chatHistory.Count - 1; i >= 0 && removed < count; i--)
                {
                    if (_chatHistory[i].Role.Equals(role, StringComparison.OrdinalIgnoreCase))
                    {
                        removedIds.Add(_chatHistory[i].Id);
                        _chatHistory.RemoveAt(i);
                        removed++;
                    }
                }
            }

            if (removed > 0)
            {
                foreach (var id in removedIds)
                {
                    _databaseService.DeleteChatMessage(id, _userId);
                }

                _broadcastHistoryCallback?.Invoke();
            }
            return removed;
        }

        /// <summary>
        /// 获取所有历史记录（兼容旧代码）
        /// </summary>
        public List<ChatMessage> GetHistory()
        {
            lock (_chatHistoryLock) return _chatHistory.ToList();
        }

        /// <summary>
        /// 分页获取聊天历史
        /// </summary>
        /// <param name="beforeId">从此ID之前加载（用于分页）</param>
        /// <param name="beforeTime">从此时间之前加载</param>
        /// <param name="limit">每次加载条数</param>
        /// <returns>消息列表和是否有更多</returns>
        public (List<ChatMessage> messages, bool hasMore) GetHistoryPaged(string beforeId = null, DateTime? beforeTime = null, int limit = 20)
        {
            return _databaseService.GetChatMessagesPaged(_userId, beforeId, beforeTime, limit);
        }

        /// <summary>
        /// 获取最新消息（用于初始化显示）
        /// </summary>
        /// <param name="count">消息数量</param>
        public List<ChatMessage> GetLatestMessages(int count = 20)
        {
            var (messages, _) = _databaseService.GetChatMessagesPaged(_userId, null, null, count);
            return messages;
        }

        /// <summary>
        /// 获取消息总数
        /// </summary>
        public int GetMessageCount()
        {
            return _databaseService.GetChatMessageCount(_userId);
        }

        public void BroadcastHistory()
        {
            _broadcastHistoryCallback?.Invoke();
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            _databaseService?.Dispose();
        }
    }
}
