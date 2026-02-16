using Newtonsoft.Json;

namespace AI_Chat.Models
{
    public class ErrorResponse
    {
        [JsonProperty("code")]
        public int Code { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("html")]
        public string Html { get; set; }
    }
}
