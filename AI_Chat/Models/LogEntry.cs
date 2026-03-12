using Newtonsoft.Json;

namespace AI_Chat.Models
{
    public class LogEntry
    {
        [JsonProperty("timestamp")]
        public string Timestamp { get; set; }

        [JsonProperty("level")]
        public string Level { get; set; }

        [JsonProperty("source")]
        public string Source { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }
    }
}
