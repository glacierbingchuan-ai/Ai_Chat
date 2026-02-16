using Newtonsoft.Json;

namespace AI_Chat.Models
{
    public class WebSocketMessage
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("data")]
        public dynamic Data { get; set; }
    }
}
