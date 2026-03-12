using Newtonsoft.Json;

namespace AI_Chat.Models
{
    public class WebSocketMessage
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("data")]
        public dynamic Data { get; set; }

        /// <summary>
        /// 消息ID，用于请求-响应追踪
        /// </summary>
        [JsonProperty("id")]
        public string Id { get; set; }

        /// <summary>
        /// 响应的消息ID（用于响应消息）
        /// </summary>
        [JsonProperty("replyTo")]
        public string ReplyTo { get; set; }
    }
}
