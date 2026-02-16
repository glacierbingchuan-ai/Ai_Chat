using System;
using System.IO;
using AI_Chat.Models;
using AI_Chat.Constants;
using Newtonsoft.Json;

namespace AI_Chat.Services
{
    public class ConfigManager
    {
        private static readonly object _configLock = new object();
        private ControlPanelConfig _config;

        public ConfigManager()
        {
            _config = new ControlPanelConfig
            {
                BaseSystemPrompt = SystemPrompts.BASE_SYSTEM_PROMPT,
                IncompleteInputPrompt = SystemPrompts.INCOMPLETE_INPUT_PROMPT
            };
        }

        public ControlPanelConfig Config => _config;

        public void LoadConfig()
        {
            try
            {
                if (File.Exists(AppConstants.CONFIG_FILE_PATH))
                {
                    string json = File.ReadAllText(AppConstants.CONFIG_FILE_PATH);
                    var loadedConfig = JsonConvert.DeserializeObject<ControlPanelConfig>(json);
                    if (loadedConfig != null)
                {
                    lock (_configLock)
                    {
                        _config = loadedConfig;
                        // 如果加载的配置中某些字段为空，保留默认值
                        if (string.IsNullOrEmpty(_config.BaseSystemPrompt))
                            _config.BaseSystemPrompt = SystemPrompts.BASE_SYSTEM_PROMPT;
                        if (string.IsNullOrEmpty(_config.IncompleteInputPrompt))
                            _config.IncompleteInputPrompt = SystemPrompts.INCOMPLETE_INPUT_PROMPT;
                    }
                    Logger.LogInfo("CONFIG", "Configuration loaded from file: " + AppConstants.CONFIG_FILE_PATH);
                }
                }
                else
                {
                    Logger.LogInfo("CONFIG", "Configuration file not found, creating default configuration");
                    SaveConfig();
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("CONFIG", "Error loading configuration: " + ex.Message);
            }
        }

        public void SaveConfig()
        {
            try
            {
                lock (_configLock)
                {
                    string json = JsonConvert.SerializeObject(_config, Formatting.Indented);
                    File.WriteAllText(AppConstants.CONFIG_FILE_PATH, json);
                }
                Logger.LogInfo("CONFIG", "Configuration saved to file: " + AppConstants.CONFIG_FILE_PATH);
            }
            catch (Exception ex)
            {
                Logger.LogError("CONFIG", "Error saving configuration: " + ex.Message);
            }
        }

        public void UpdateConfig(dynamic configData)
        {
            lock (_configLock)
            {
                // 如果传入的是 ControlPanelConfig 对象，直接替换
                if (configData is ControlPanelConfig newConfig)
                {
                    _config = newConfig;
                    SaveConfig();
                    return;
                }

                // 否则按 dynamic 对象处理（支持大小写两种属性名）
                if (configData.llmModelName != null) _config.LlmModelName = configData.llmModelName.ToString();
                if (configData.LlmModelName != null) _config.LlmModelName = configData.LlmModelName.ToString();
                if (configData.llmApiBaseUrl != null) _config.LlmApiBaseUrl = configData.llmApiBaseUrl.ToString();
                if (configData.LlmApiBaseUrl != null) _config.LlmApiBaseUrl = configData.LlmApiBaseUrl.ToString();
                if (configData.llmApiKey != null) _config.LlmApiKey = configData.llmApiKey.ToString();
                if (configData.LlmApiKey != null) _config.LlmApiKey = configData.LlmApiKey.ToString();
                if (configData.llmMaxTokens != null) _config.LlmMaxTokens = (int)configData.llmMaxTokens;
                if (configData.LlmMaxTokens != null) _config.LlmMaxTokens = (int)configData.LlmMaxTokens;
                if (configData.llmTemperature != null) _config.LlmTemperature = (double)configData.llmTemperature;
                if (configData.LlmTemperature != null) _config.LlmTemperature = (double)configData.LlmTemperature;
                if (configData.llmTopP != null) _config.LlmTopP = (double)configData.llmTopP;
                if (configData.LlmTopP != null) _config.LlmTopP = (double)configData.LlmTopP;
                if (configData.websocketServerUri != null) _config.WebsocketServerUri = configData.websocketServerUri.ToString();
                if (configData.WebsocketServerUri != null) _config.WebsocketServerUri = configData.WebsocketServerUri.ToString();
                if (configData.websocketToken != null) _config.WebsocketToken = configData.websocketToken.ToString();
                if (configData.WebsocketToken != null) _config.WebsocketToken = configData.WebsocketToken.ToString();
                if (configData.websocketKeepAliveInterval != null) _config.WebsocketKeepAliveInterval = (int)configData.websocketKeepAliveInterval;
                if (configData.WebsocketKeepAliveInterval != null) _config.WebsocketKeepAliveInterval = (int)configData.WebsocketKeepAliveInterval;
                if (configData.maxContextRounds != null) _config.MaxContextRounds = (int)configData.maxContextRounds;
                if (configData.MaxContextRounds != null) _config.MaxContextRounds = (int)configData.MaxContextRounds;
                if (configData.targetUserId != null) _config.TargetUserId = (long)configData.targetUserId;
                if (configData.TargetUserId != null) _config.TargetUserId = (long)configData.TargetUserId;
                if (configData.activeChatProbability != null) _config.ActiveChatProbability = (int)configData.activeChatProbability;
                if (configData.ActiveChatProbability != null) _config.ActiveChatProbability = (int)configData.ActiveChatProbability;
                if (configData.logRootFolder != null) _config.LogRootFolder = configData.logRootFolder.ToString();
                if (configData.LogRootFolder != null) _config.LogRootFolder = configData.LogRootFolder.ToString();
                if (configData.generalLogSubfolder != null) _config.GeneralLogSubfolder = configData.generalLogSubfolder.ToString();
                if (configData.GeneralLogSubfolder != null) _config.GeneralLogSubfolder = configData.GeneralLogSubfolder.ToString();
                if (configData.contextLogSubfolder != null) _config.ContextLogSubfolder = configData.contextLogSubfolder.ToString();
                if (configData.ContextLogSubfolder != null) _config.ContextLogSubfolder = configData.ContextLogSubfolder.ToString();
                if (configData.proactiveChatEnabled != null) _config.ProactiveChatEnabled = (bool)configData.proactiveChatEnabled;
                if (configData.ProactiveChatEnabled != null) _config.ProactiveChatEnabled = (bool)configData.ProactiveChatEnabled;
                if (configData.reminderEnabled != null) _config.ReminderEnabled = (bool)configData.reminderEnabled;
                if (configData.ReminderEnabled != null) _config.ReminderEnabled = (bool)configData.ReminderEnabled;
                if (configData.intentAnalysisEnabled != null) _config.IntentAnalysisEnabled = (bool)configData.intentAnalysisEnabled;
                if (configData.IntentAnalysisEnabled != null) _config.IntentAnalysisEnabled = (bool)configData.IntentAnalysisEnabled;
                if (configData.baseSystemPrompt != null) _config.BaseSystemPrompt = configData.baseSystemPrompt.ToString();
                if (configData.BaseSystemPrompt != null) _config.BaseSystemPrompt = configData.BaseSystemPrompt.ToString();
                if (configData.incompleteInputPrompt != null) _config.IncompleteInputPrompt = configData.incompleteInputPrompt.ToString();
                if (configData.IncompleteInputPrompt != null) _config.IncompleteInputPrompt = configData.IncompleteInputPrompt.ToString();
                if (configData.roleCardsApiUrl != null) _config.RoleCardsApiUrl = configData.roleCardsApiUrl.ToString();
                if (configData.RoleCardsApiUrl != null) _config.RoleCardsApiUrl = configData.RoleCardsApiUrl.ToString();

                SaveConfig();
            }
        }
    }
}
