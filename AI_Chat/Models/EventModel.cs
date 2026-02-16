using Newtonsoft.Json;

namespace AI_Chat.Models
{
    public class EventModel
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("time")]
        public string Time { get; set; }
    }
}
