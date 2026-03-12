namespace AI_Chat.Constants
{
    public static class AppConstants
    {
        public const string LLM_AUTH_HEADER = "Authorization";
        public const string LLM_AUTH_SCHEME = "Bearer";

        public const int ACTIVE_CHAT_PROBABILITY = 30;

        public const string LOG_ROOT_FOLDER = "BotLogs";
        public const string GENERAL_LOG_SUBFOLDER = "GeneralLogs";
        public const string CONTEXT_LOG_SUBFOLDER = "AIContextLogs";
        public const string CONFIG_FILE_PATH = "config.json";

        public const int CONTROL_PANEL_PORT = 8080;
        public const string CONTROL_PANEL_PREFIX = "/ws";

        public const int LLM_STATUS_CHECK_INTERVAL = 15000;

        public const string TAG_PROACTIVE = "[Proactive Chat Triggered]";
        public const string TAG_REMINDER = "[Internal Reminder Triggered]";
        public const string TAG_FORMAT_ERROR = "[Format Error Correction]";

        public const int MAX_LOGS = 1000;
        public const int MAX_CHAT_HISTORY = 1000;
    }
}
