using Newtonsoft.Json;

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

        [JsonProperty("websocketServerUri")]
        public string WebsocketServerUri { get; set; } = "ws://localhost:3000";

        [JsonProperty("websocketToken")]
        public string WebsocketToken { get; set; } = "";

        [JsonProperty("websocketKeepAliveInterval")]
        public int WebsocketKeepAliveInterval { get; set; } = 30000;

        [JsonProperty("maxContextRounds")]
        public int MaxContextRounds { get; set; } = 10;

        [JsonProperty("targetUserId")]
        public long TargetUserId { get; set; } = 3917952948;

        [JsonProperty("activeChatProbability")]
        public int ActiveChatProbability { get; set; } = 30;

        [JsonProperty("logRootFolder")]
        public string LogRootFolder { get; set; } = "BotLogs";

        [JsonProperty("generalLogSubfolder")]
        public string GeneralLogSubfolder { get; set; } = "GeneralLogs";

        [JsonProperty("contextLogSubfolder")]
        public string ContextLogSubfolder { get; set; } = "AIContextLogs";

        [JsonProperty("proactiveChatEnabled")]
        public bool ProactiveChatEnabled { get; set; } = true;

        [JsonProperty("reminderEnabled")]
        public bool ReminderEnabled { get; set; } = true;

        [JsonProperty("intentAnalysisEnabled")]
        public bool IntentAnalysisEnabled { get; set; } = true;

        [JsonProperty("baseSystemPrompt")]
        public string BaseSystemPrompt { get; set; } = "";

        [JsonProperty("incompleteInputPrompt")]
        public string IncompleteInputPrompt { get; set; } = "";

        [JsonProperty("roleCardsApiUrl")]
        public string RoleCardsApiUrl { get; set; } = "https://gitee.com/bingchuankeji/Character_Cards/raw/main/list.json";
    }
}
