using System.Collections.Generic;
using Newtonsoft.Json;

namespace AI_Chat.Models
{
    public class AIReplyModel
    {
        [JsonProperty("reply")]
        public bool NeedReply { get; set; } = true;

        [JsonProperty("messages")]
        public List<dynamic> Messages { get; set; } = new List<dynamic>();

        [JsonProperty("events")]
        public List<EventModel> Events { get; set; } = new List<EventModel>();
    }
}
