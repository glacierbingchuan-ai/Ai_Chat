using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using AI_Chat.Models;
using AI_Chat.Services;
using Newtonsoft.Json;

namespace AI_Chat.Managers
{
    public class UserConfigManager
    {
        private readonly ConcurrentDictionary<long, UserConfig> _userConfigs = new ConcurrentDictionary<long, UserConfig>();
        private readonly string _userDataBasePath;
        private readonly object _fileLock = new object();

        public UserConfigManager()
        {
            _userDataBasePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UserData");
            
            if (!Directory.Exists(_userDataBasePath))
            {
                Directory.CreateDirectory(_userDataBasePath);
            }
        }

        public UserConfig GetOrCreateUserConfig(long userId)
        {
            return _userConfigs.GetOrAdd(userId, id =>
            {
                var config = LoadUserConfigFromDisk(id);
                if (config == null)
                {
                    config = new UserConfig(id);
                    SaveUserConfigToDisk(id, config);
                }
                return config;
            });
        }

        public UserConfig GetUserConfig(long userId)
        {
            if (_userConfigs.TryGetValue(userId, out var config))
            {
                return config;
            }
            return LoadUserConfigFromDisk(userId);
        }

        public void UpdateUserConfig(long userId, UserConfig config)
        {
            config.UserId = userId;
            _userConfigs[userId] = config;
            SaveUserConfigToDisk(userId, config);
            Logger.LogInfo("USER_CONFIG", $"Updated config for user {userId}");
        }

        public void UpdateUserConfig(long userId, dynamic configData)
        {
            var config = GetOrCreateUserConfig(userId);

            if (configData.activeChatProbability != null)
                config.ActiveChatProbability = (int)configData.activeChatProbability;
            if (configData.ActiveChatProbability != null)
                config.ActiveChatProbability = (int)configData.ActiveChatProbability;

            if (configData.proactiveChatEnabled != null)
                config.ProactiveChatEnabled = (bool)configData.proactiveChatEnabled;
            if (configData.ProactiveChatEnabled != null)
                config.ProactiveChatEnabled = (bool)configData.ProactiveChatEnabled;

            if (configData.reminderEnabled != null)
                config.ReminderEnabled = (bool)configData.reminderEnabled;
            if (configData.ReminderEnabled != null)
                config.ReminderEnabled = (bool)configData.ReminderEnabled;

            if (configData.intentAnalysisEnabled != null)
                config.IntentAnalysisEnabled = (bool)configData.intentAnalysisEnabled;
            if (configData.IntentAnalysisEnabled != null)
                config.IntentAnalysisEnabled = (bool)configData.IntentAnalysisEnabled;

            if (configData.baseSystemPrompt != null)
                config.BaseSystemPrompt = configData.baseSystemPrompt.ToString();
            if (configData.BaseSystemPrompt != null)
                config.BaseSystemPrompt = configData.BaseSystemPrompt.ToString();

            if (configData.incompleteInputPrompt != null)
                config.IncompleteInputPrompt = configData.incompleteInputPrompt.ToString();
            if (configData.IncompleteInputPrompt != null)
                config.IncompleteInputPrompt = configData.IncompleteInputPrompt.ToString();

            _userConfigs[userId] = config;
            SaveUserConfigToDisk(userId, config);
            Logger.LogInfo("USER_CONFIG", $"Updated config for user {userId}");
        }

        public void ResetUserConfig(long userId, dynamic configData)
        {
            var config = new UserConfig(userId);

            if (configData.activeChatProbability != null)
                config.ActiveChatProbability = (int)configData.activeChatProbability;
            if (configData.ActiveChatProbability != null)
                config.ActiveChatProbability = (int)configData.ActiveChatProbability;

            if (configData.proactiveChatEnabled != null)
                config.ProactiveChatEnabled = (bool)configData.proactiveChatEnabled;
            if (configData.ProactiveChatEnabled != null)
                config.ProactiveChatEnabled = (bool)configData.ProactiveChatEnabled;

            if (configData.reminderEnabled != null)
                config.ReminderEnabled = (bool)configData.reminderEnabled;
            if (configData.ReminderEnabled != null)
                config.ReminderEnabled = (bool)configData.ReminderEnabled;

            if (configData.intentAnalysisEnabled != null)
                config.IntentAnalysisEnabled = (bool)configData.intentAnalysisEnabled;
            if (configData.IntentAnalysisEnabled != null)
                config.IntentAnalysisEnabled = (bool)configData.IntentAnalysisEnabled;

            _userConfigs[userId] = config;
            SaveUserConfigToDisk(userId, config);
            Logger.LogInfo("USER_CONFIG", $"Reset config for user {userId} to defaults");
        }



        private UserConfig LoadUserConfigFromDisk(long userId)
        {
            try
            {
                string configPath = GetUserConfigPath(userId);
                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    var config = JsonConvert.DeserializeObject<UserConfig>(json);
                    if (config != null)
                    {
                        config.UserId = userId;
                        Logger.LogInfo("USER_CONFIG", $"Loaded config for user {userId} from disk");
                        return config;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("USER_CONFIG", $"Error loading config for user {userId}: {ex.Message}");
            }
            return null;
        }

        private void SaveUserConfigToDisk(long userId, UserConfig config)
        {
            SaveUserConfigToDiskAsync(userId, config).ConfigureAwait(false);
        }

        private async Task SaveUserConfigToDiskAsync(long userId, UserConfig config)
        {
            try
            {
                string userDir = GetUserDirectory(userId);
                string configPath = GetUserConfigPath(userId);
                string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                
                await Task.Run(() =>
                {
                    lock (_fileLock)
                    {
                        if (!Directory.Exists(userDir))
                        {
                            Directory.CreateDirectory(userDir);
                        }
                        File.WriteAllText(configPath, json);
                    }
                });
                Logger.LogInfo("USER_CONFIG", $"Saved config for user {userId} to disk");
            }
            catch (Exception ex)
            {
                Logger.LogError("USER_CONFIG", $"Error saving config for user {userId}: {ex.Message}");
            }
        }

        private string GetUserDirectory(long userId)
        {
            return Path.Combine(_userDataBasePath, userId.ToString());
        }

        private string GetUserConfigPath(long userId)
        {
            return Path.Combine(GetUserDirectory(userId), "user_config.json");
        }
    }
}
