using Newtonsoft.Json;

namespace AI_Chat.Models
{
    public class ChatMessage
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("role")]
        public string Role { get; set; }

        [JsonProperty("content")]
        public string Content { get; set; }

        [JsonProperty("meme")]
        public string Meme { get; set; }

        [JsonProperty("timestamp")]
        public string Timestamp { get; set; }
    }
}
