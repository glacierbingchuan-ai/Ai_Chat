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

        /// <summary>
        /// 插件调用请求 - 大模型决定调用插件时使用
        /// </summary>
        [JsonProperty("plugin_invoke")]
        public PluginInvokeRequest PluginInvoke { get; set; }
    }

    /// <summary>
    /// 插件调用请求
    /// </summary>
    public class PluginInvokeRequest
    {
        [JsonProperty("plugin_id")]
        public string PluginId { get; set; }

        [JsonProperty("capability")]
        public string CapabilityName { get; set; }

        [JsonProperty("parameters")]
        public Dictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();
    }
}
