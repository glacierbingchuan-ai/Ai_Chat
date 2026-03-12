using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AI_Chat.Models;

namespace AI_Chat.Services
{
    /// <summary>
    /// 上下文管理器基础接口 - 提供消息和事件管理功能
    /// </summary>
    public interface IContextManager : IDisposable
    {
        long UserId { get; }
        List<Message> Context { get; }
        List<EventModel> ScheduledEvents { get; }
        int UserMessageCount { get; }
        int LastUserMessageIndex { get; }

        Task<(bool isAppended, string fullMessage)> AddUserMessageAsync(string content);
        Task AddAssistantMessageAsync(string content);
        void AddSystemMessage(string content);
        void InsertSystemMessage(int index, string content);
        void UpdateUserMessage(int index, string newContent);
        void RemoveFormatErrorMessages();
        void RemoveOrphanInternalTrigger();
        void ClearContext();
        void AddEvent(EventModel ev);
        GetDueEventsResult GetDueEvents();
        bool HasUpcomingEventWithin(TimeSpan timeSpan);
        bool TryParseRobustDateTime(string timeStr, out DateTime result);
        List<Message> GetContextForPrompt(string query, int maxRecentMessages = 10, float similarityThreshold = 0.2f);
        int RemoveLastMessages(string role, int count);
    }
}
