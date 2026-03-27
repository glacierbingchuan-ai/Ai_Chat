using System;
using System.Collections.Generic;
using System.Linq;
using AI_Chat.Models;
using AI_Chat.Services;
using Newtonsoft.Json;

namespace AI_Chat.Utils
{
    /// <summary>
    /// 消息处理工具类 - 提供统一的消息解析和处理方法
    /// </summary>
    public static class MessageUtils
    {
        /// <summary>
        /// 从 AIReplyModel JSON 中提取纯文本内容
        /// </summary>
        /// <param name="content">可能包含 JSON 的消息内容</param>
        /// <returns>提取的纯文本内容</returns>
        public static string ExtractContentFromAIReply(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return content;

            if (!content.Trim().StartsWith("{"))
                return content;

            try
            {
                var parsed = JsonConvert.DeserializeObject<AIReplyModel>(content);
                if (parsed != null && parsed.Messages != null)
                {
                    var items = parsed.Messages.Select(item =>
                    {
                        if (item.content != null) return item.content.ToString();
                        if (item.meme != null) return $"[表情包:{item.meme}]";
                        return "";
                    });
                    return string.Join(" ", items);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("MessageUtils", $"Failed to parse AI reply content: {ex.Message}");
            }

            return content;
        }

        /// <summary>
        /// 将 AIReplyModel 消息列表转换为纯文本内容列表
        /// </summary>
        public static List<string> ExtractContentPartsFromAIReply(string content)
        {
            var result = new List<string>();

            if (string.IsNullOrWhiteSpace(content) || !content.Trim().StartsWith("{"))
            {
                if (!string.IsNullOrWhiteSpace(content))
                    result.Add(content);
                return result;
            }

            try
            {
                var parsed = JsonConvert.DeserializeObject<AIReplyModel>(content);
                if (parsed?.Messages != null)
                {
                    foreach (var msg in parsed.Messages)
                    {
                        if (msg.content != null)
                        {
                            result.Add(msg.content.ToString());
                        }
                        else if (msg.meme != null)
                        {
                            result.Add($"[表情包:{msg.meme}]");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("MessageUtils", $"Failed to extract content parts: {ex.Message}");
                result.Add(content);
            }

            return result;
        }

        /// <summary>
        /// 将消息列表格式化为对话历史字符串
        /// </summary>
        public static string FormatMessagesToHistory(List<Message> messages)
        {
            if (messages == null || messages.Count == 0)
                return string.Empty;

            return string.Join("\n", messages
                .Where(m => !string.IsNullOrWhiteSpace(m.Content))
                .Select(m => $"{m.Role}: {ExtractContentFromAIReply(m.Content)}"));
        }
    }
}
