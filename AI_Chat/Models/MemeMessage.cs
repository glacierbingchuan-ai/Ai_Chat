using Newtonsoft.Json;

namespace AI_Chat.Models
{
    public class MemeMessage
    {
        [JsonProperty("meme")]
        public string Meme { get; set; }

        [JsonProperty("delay_ms")]
        public int DelayMs { get; set; }
    }
}
