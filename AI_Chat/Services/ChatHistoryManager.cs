using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using AI_Chat.Models;
using AI_Chat.Constants;
using Newtonsoft.Json;

namespace AI_Chat.Services
{
    public class ChatHistoryManager
    {
        private readonly object _chatHistoryLock = new object();
        private List<ChatMessage> _chatHistory = new List<ChatMessage>();
        private Action<ChatMessage> _broadcastMessageCallback;
        private Action _broadcastHistoryCallback;

        public ChatHistoryManager()
        {
        }

        public void Initialize(Action<ChatMessage> broadcastMessageCallback, Action broadcastHistoryCallback)
        {
            _broadcastMessageCallback = broadcastMessageCallback;
            _broadcastHistoryCallback = broadcastHistoryCallback;
        }

        public void LoadChatHistoryFromDisk()
        {
            try
            {
                if (File.Exists(AppConstants.CHAT_HISTORY_PATH))
                {
                    string json = File.ReadAllText(AppConstants.CHAT_HISTORY_PATH);
                    var savedChatHistory = JsonConvert.DeserializeObject<List<ChatMessage>>(json);
                    if (savedChatHistory != null && savedChatHistory.Count > 0)
                    {
                        lock (_chatHistoryLock)
                        {
                            _chatHistory = savedChatHistory;
                        }
                        Logger.LogInfo("PERSISTENCE", $"Loaded {savedChatHistory.Count} chat messages from local disk");
                        return;
                    }
                }
                Logger.LogInfo("PERSISTENCE", "No chat history found or file is empty, initializing new chat history");
            }
            catch (Exception ex)
            {
                Logger.LogError("PERSISTENCE", "Failed to load chat history: " + ex.Message);
            }
        }

        public void SaveChatHistoryToDisk()
        {
            try
            {
                lock (_chatHistoryLock)
                {
                    string json = JsonConvert.SerializeObject(_chatHistory, Formatting.Indented);
                    File.WriteAllText(AppConstants.CHAT_HISTORY_PATH, json);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("PERSISTENCE", $"Failed to save chat history: {ex.Message}");
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

            SaveChatHistoryToDisk();
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

            SaveChatHistoryToDisk();
            // 不触发广播，避免前端重复显示
        }

        public void ClearHistory()
        {
            lock (_chatHistoryLock)
            {
                _chatHistory.Clear();
            }
            SaveChatHistoryToDisk();
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
            lock (_chatHistoryLock)
            {
                // 从后往前遍历，删除匹配角色的消息
                for (int i = _chatHistory.Count - 1; i >= 0 && removed < count; i--)
                {
                    if (_chatHistory[i].Role.Equals(role, StringComparison.OrdinalIgnoreCase))
                    {
                        _chatHistory.RemoveAt(i);
                        removed++;
                    }
                }
            }
            if (removed > 0)
            {
                SaveChatHistoryToDisk();
                _broadcastHistoryCallback?.Invoke();
            }
            return removed;
        }

        public List<ChatMessage> GetHistory()
        {
            lock (_chatHistoryLock) return _chatHistory.ToList();
        }

        public void BroadcastHistory()
        {
            _broadcastHistoryCallback?.Invoke();
        }
    }
}
