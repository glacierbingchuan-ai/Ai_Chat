using Newtonsoft.Json;

namespace AI_Chat.Models
{
    public class TextMessage
    {
        [JsonProperty("content")]
        public string Content { get; set; }

        [JsonProperty("delay_ms")]
        public int DelayMs { get; set; }
    }
}
