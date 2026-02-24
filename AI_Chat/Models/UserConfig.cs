using Newtonsoft.Json;
using AI_Chat.Constants;

namespace AI_Chat.Models
{
    public class UserConfig
    {
        [JsonProperty("userId")]
        public long UserId { get; set; }

        [JsonProperty("activeChatProbability")]
        public int ActiveChatProbability { get; set; } = AppConstants.ACTIVE_CHAT_PROBABILITY;

        [JsonProperty("proactiveChatEnabled")]
        public bool ProactiveChatEnabled { get; set; } = true;

        [JsonProperty("reminderEnabled")]
        public bool ReminderEnabled { get; set; } = true;

        [JsonProperty("intentAnalysisEnabled")]
        public bool IntentAnalysisEnabled { get; set; } = true;

        [JsonProperty("baseSystemPrompt")]
        public string BaseSystemPrompt { get; set; } = SystemPrompts.BASE_SYSTEM_PROMPT;

        [JsonProperty("incompleteInputPrompt")]
        public string IncompleteInputPrompt { get; set; } = SystemPrompts.INCOMPLETE_INPUT_PROMPT;

        public UserConfig()
        {
        }

        public UserConfig(long userId)
        {
            UserId = userId;
        }

        public UserConfig Clone()
        {
            return new UserConfig
            {
                UserId = this.UserId,
                ActiveChatProbability = this.ActiveChatProbability,
                ProactiveChatEnabled = this.ProactiveChatEnabled,
                ReminderEnabled = this.ReminderEnabled,
                IntentAnalysisEnabled = this.IntentAnalysisEnabled,
                BaseSystemPrompt = this.BaseSystemPrompt,
                IncompleteInputPrompt = this.IncompleteInputPrompt
            };
        }
    }
}
