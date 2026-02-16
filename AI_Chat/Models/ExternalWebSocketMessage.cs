using Newtonsoft.Json;

namespace AI_Chat.Models
{
    public class ExternalWebSocketMessage
    {
        [JsonProperty("action")]
        public string Action { get; set; }

        [JsonProperty("params")]
        public dynamic Params { get; set; }
    }
}
