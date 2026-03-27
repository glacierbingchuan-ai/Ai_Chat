using Newtonsoft.Json;
using System.Collections.Generic;

namespace AI_Chat.Models
{
    public class ControlPanelConfig
    {
        [JsonProperty("llmModelName")]
        public string LlmModelName { get; set; } = "your_model_name";

        [JsonProperty("llmApiBaseUrl")]
        public string LlmApiBaseUrl { get; set; } = "your_api";

        [JsonProperty("llmApiKey")]
        public string LlmApiKey { get; set; } = "your_apikey";

        [JsonProperty("llmMaxTokens")]
        public int LlmMaxTokens { get; set; } = 1024;

        [JsonProperty("llmTemperature")]
        public double LlmTemperature { get; set; } = 0.9;

        [JsonProperty("llmTopP")]
        public double LlmTopP { get; set; } = 0.85;

        [JsonProperty("embeddingModelName")]
        public string EmbeddingModelName { get; set; } = "text-embedding-3-small";

        [JsonProperty("embeddingApiBaseUrl")]
        public string EmbeddingApiBaseUrl { get; set; } = "";

        [JsonProperty("embeddingApiKey")]
        public string EmbeddingApiKey { get; set; } = "";

        [JsonProperty("websocketServerUri")]
        public string WebsocketServerUri { get; set; } = "ws://localhost:3000";

        [JsonProperty("websocketToken")]
        public string WebsocketToken { get; set; } = "";

        [JsonProperty("websocketKeepAliveInterval")]
        public int WebsocketKeepAliveInterval { get; set; } = 30000;

        [JsonProperty("maxContextRounds")]
        public int MaxContextRounds { get; set; } = 10;

        [JsonProperty("allowedUserIds")]
        public List<long> AllowedUserIds { get; set; } = new List<long>();

        [JsonProperty("allowedGroupIds")]
        public List<long> AllowedGroupIds { get; set; } = new List<long>();

        [JsonProperty("roleCardsApiUrl")]
        public string RoleCardsApiUrl { get; set; } = "https://gitee.com/bingchuankeji/Character_Cards/raw/main/list.json";

        [JsonProperty("isFirstRun")]
        public bool IsFirstRun { get; set; } = true;

        [JsonProperty("eulaAccepted")]
        public bool EulaAccepted { get; set; } = false;

        [JsonProperty("vectorDbSimilarityThreshold")]
        public float VectorDbSimilarityThreshold { get; set; } = 0.2f;

        [JsonProperty("vectorDbTopK")]
        public int VectorDbTopK { get; set; } = 10;

        [JsonProperty("rateLimitTimeWindow")]
        public int RateLimitTimeWindow { get; set; } = 1;

        [JsonProperty("rateLimitMaxRequests")]
        public int RateLimitMaxRequests { get; set; } = 1;

        [JsonProperty("useVectorContext")]
        public bool UseVectorContext { get; set; } = false;

        [JsonProperty("useContextSummarization")]
        public bool UseContextSummarization { get; set; } = true;

        [JsonProperty("useLocalEmbedding")]
        public bool UseLocalEmbedding { get; set; } = false;

        [JsonProperty("localEmbeddingModelPath")]
        public string LocalEmbeddingModelPath { get; set; } = "";
    }
}
