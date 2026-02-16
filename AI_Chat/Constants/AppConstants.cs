namespace AI_Chat.Constants
{
    public static class AppConstants
    {
        public const string LLM_MODEL_NAME = "your_model_name";
        public const string LLM_API_BASE_URL = "your_api";
        public const string LLM_API_KEY = "your_apikey";
        public const string LLM_AUTH_HEADER = "Authorization";
        public const string LLM_AUTH_SCHEME = "Bearer";

        public const string WEBSOCKET_SERVER_URI = "ws://localhost:3000";
        public const int WEBSOCKET_KEEP_ALIVE_INTERVAL = 30000;

        public const int MAX_CONTEXT_ROUNDS = 10;
        public const int LLM_MAX_TOKENS = 1024;
        public const double LLM_TEMPERATURE = 0.9;
        public const double LLM_TOP_P = 0.85;

        public const long TARGET_USER_ID = 3917952948;
        public const int ACTIVE_CHAT_PROBABILITY = 30;
        public const int MIN_SAFE_DELAY = 1200;

        public const string LOG_ROOT_FOLDER = "BotLogs";
        public const string GENERAL_LOG_SUBFOLDER = "GeneralLogs";
        public const string CONTEXT_LOG_SUBFOLDER = "AIContextLogs";
        public const string CONFIG_FILE_PATH = "config.json";
        public const string CONTEXT_PERSISTENCE_PATH = "context_persistence.json";
        public const string EVENTS_PERSISTENCE_PATH = "events_persistence.json";
        public const string CHAT_HISTORY_PATH = "chat_history.json";

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
