using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace AI_Chat.Models
{
    public class VectorEntry
    {
        [JsonProperty("id")]
        public string Id { get; set; }
        
        [JsonProperty("vector")]
        public float[] Vector { get; set; }
        
        [JsonProperty("content")]
        public string Content { get; set; }
        
        [JsonProperty("role")]
        public string Role { get; set; }
        
        [JsonProperty("userId")]
        public long UserId { get; set; }
        
        [JsonProperty("timestamp")]
        public DateTime Timestamp { get; set; }
        
        [JsonProperty("metadata")]
        public Dictionary<string, object> Metadata { get; set; }

        public VectorEntry()
        {
            Id = Guid.NewGuid().ToString();
            Timestamp = DateTime.Now;
            Metadata = new Dictionary<string, object>();
        }
    }
}
