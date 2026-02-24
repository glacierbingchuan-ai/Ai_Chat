using Newtonsoft.Json;

namespace AI_Chat.Models
{
    public class StandardMessage
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("data")]
        public dynamic Data { get; set; }

        [JsonProperty("timestamp")]
        public string Timestamp { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; }
    }
}
