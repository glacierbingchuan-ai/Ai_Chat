using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AI_Chat.Models;
using AI_Chat.Constants;
using Newtonsoft.Json;

namespace AI_Chat.Services
{
    public class ConfigManager
    {
        private static readonly object _configLock = new object();
        private static readonly object _fileLock = new object();
        private ControlPanelConfig _config;

        public ConfigManager()
        {
            _config = new ControlPanelConfig();
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
                            
                            // Ensure AllowedUserIds is initialized
                            if (_config.AllowedUserIds == null)
                            {
                                _config.AllowedUserIds = new List<long>();
                            }
                        }
                        Logger.LogInfo("CONFIG", "Configuration loaded from file: " + AppConstants.CONFIG_FILE_PATH);
                    }
                }
                else
                {
                    Logger.LogInfo("CONFIG", "Configuration file not found, creating default configuration");
                    lock (_configLock)
                    {
                        // Don't add default user, leave it empty
                        _config.AllowedUserIds = new List<long>();
                    }
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
            SaveConfigAsync().ConfigureAwait(false);
        }

        public async Task SaveConfigAsync()
        {
            string json;
            lock (_configLock)
            {
                json = JsonConvert.SerializeObject(_config, Formatting.Indented);
            }
            
            await Task.Run(() =>
            {
                lock (_fileLock)
                {
                    try
                    {
                        File.WriteAllText(AppConstants.CONFIG_FILE_PATH, json);
                        Logger.LogInfo("CONFIG", "Configuration saved to file: " + AppConstants.CONFIG_FILE_PATH);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError("CONFIG", "Error saving configuration: " + ex.Message);
                    }
                }
            });
        }

        public void UpdateConfig(dynamic configData)
        {
            lock (_configLock)
            {
                if (configData is ControlPanelConfig newConfig)
                {
                    _config = newConfig;
                    SaveConfig();
                    return;
                }

                if (configData.llmModelName != null && !string.IsNullOrEmpty(configData.llmModelName?.ToString())) 
                    _config.LlmModelName = configData.llmModelName.ToString();
                if (configData.LlmModelName != null && !string.IsNullOrEmpty(configData.LlmModelName?.ToString())) 
                    _config.LlmModelName = configData.LlmModelName.ToString();
                if (configData.llmApiBaseUrl != null && !string.IsNullOrEmpty(configData.llmApiBaseUrl?.ToString())) 
                    _config.LlmApiBaseUrl = configData.llmApiBaseUrl.ToString();
                if (configData.LlmApiBaseUrl != null && !string.IsNullOrEmpty(configData.LlmApiBaseUrl?.ToString())) 
                    _config.LlmApiBaseUrl = configData.LlmApiBaseUrl.ToString();
                if (configData.llmApiKey != null && !string.IsNullOrEmpty(configData.llmApiKey?.ToString())) 
                    _config.LlmApiKey = configData.llmApiKey.ToString();
                if (configData.LlmApiKey != null && !string.IsNullOrEmpty(configData.LlmApiKey?.ToString())) 
                    _config.LlmApiKey = configData.LlmApiKey.ToString();
                if (configData.llmMaxTokens != null) _config.LlmMaxTokens = (int)configData.llmMaxTokens;
                if (configData.LlmMaxTokens != null) _config.LlmMaxTokens = (int)configData.LlmMaxTokens;
                if (configData.llmTemperature != null) _config.LlmTemperature = (double)configData.llmTemperature;
                if (configData.LlmTemperature != null) _config.LlmTemperature = (double)configData.llmTemperature;
                if (configData.llmTopP != null) _config.LlmTopP = (double)configData.llmTopP;
                if (configData.LlmTopP != null) _config.LlmTopP = (double)configData.llmTopP;
                if (configData.websocketServerUri != null) _config.WebsocketServerUri = configData.websocketServerUri.ToString();
                if (configData.WebsocketServerUri != null) _config.WebsocketServerUri = configData.WebsocketServerUri.ToString();
                if (configData.websocketToken != null) _config.WebsocketToken = configData.websocketToken.ToString();
                if (configData.WebsocketToken != null) _config.WebsocketToken = configData.WebsocketToken.ToString();
                if (configData.websocketKeepAliveInterval != null) _config.WebsocketKeepAliveInterval = (int)configData.websocketKeepAliveInterval;
                if (configData.WebsocketKeepAliveInterval != null) _config.WebsocketKeepAliveInterval = (int)configData.WebsocketKeepAliveInterval;
                if (configData.maxContextRounds != null) _config.MaxContextRounds = (int)configData.maxContextRounds;
                if (configData.MaxContextRounds != null) _config.MaxContextRounds = (int)configData.MaxContextRounds;
                
                if (configData.rateLimitTimeWindow != null) _config.RateLimitTimeWindow = (int)configData.rateLimitTimeWindow;
                if (configData.RateLimitTimeWindow != null) _config.RateLimitTimeWindow = (int)configData.RateLimitTimeWindow;
                if (configData.rateLimitMaxRequests != null) _config.RateLimitMaxRequests = (int)configData.rateLimitMaxRequests;
                if (configData.RateLimitMaxRequests != null) _config.RateLimitMaxRequests = (int)configData.RateLimitMaxRequests;

                if (configData.roleCardsApiUrl != null) _config.RoleCardsApiUrl = configData.roleCardsApiUrl.ToString();
                if (configData.RoleCardsApiUrl != null) _config.RoleCardsApiUrl = configData.RoleCardsApiUrl.ToString();
                
                if (configData.embeddingModelName != null && !string.IsNullOrEmpty(configData.embeddingModelName?.ToString())) 
                    _config.EmbeddingModelName = configData.embeddingModelName.ToString();
                if (configData.EmbeddingModelName != null && !string.IsNullOrEmpty(configData.EmbeddingModelName?.ToString())) 
                    _config.EmbeddingModelName = configData.EmbeddingModelName.ToString();
                if (configData.embeddingApiBaseUrl != null) 
                    _config.EmbeddingApiBaseUrl = configData.embeddingApiBaseUrl.ToString();
                if (configData.EmbeddingApiBaseUrl != null) 
                    _config.EmbeddingApiBaseUrl = configData.EmbeddingApiBaseUrl.ToString();
                if (configData.embeddingApiKey != null) 
                    _config.EmbeddingApiKey = configData.embeddingApiKey.ToString();
                if (configData.EmbeddingApiKey != null) 
                    _config.EmbeddingApiKey = configData.EmbeddingApiKey.ToString();

                if (configData.isFirstRun != null) _config.IsFirstRun = (bool)configData.isFirstRun;
                if (configData.IsFirstRun != null) _config.IsFirstRun = (bool)configData.IsFirstRun;
                if (configData.eulaAccepted != null) _config.EulaAccepted = (bool)configData.eulaAccepted;
                if (configData.EulaAccepted != null) _config.EulaAccepted = (bool)configData.EulaAccepted;

                if (configData.allowedUserIds != null)
                {
                    var ids = new List<long>();
                    foreach (var id in configData.allowedUserIds)
                    {
                        ids.Add((long)id);
                    }
                    _config.AllowedUserIds = ids;
                }
                if (configData.AllowedUserIds != null)
                {
                    var ids = new List<long>();
                    foreach (var id in configData.AllowedUserIds)
                    {
                        ids.Add((long)id);
                    }
                    _config.AllowedUserIds = ids;
                }

                if (configData.allowedGroupIds != null)
                {
                    var ids = new List<long>();
                    foreach (var id in configData.allowedGroupIds)
                    {
                        ids.Add((long)id);
                    }
                    _config.AllowedGroupIds = ids;
                }
                if (configData.AllowedGroupIds != null)
                {
                    var ids = new List<long>();
                    foreach (var id in configData.AllowedGroupIds)
                    {
                        ids.Add((long)id);
                    }
                    _config.AllowedGroupIds = ids;
                }

                // 处理向量上下文和对话压缩配置
                if (configData.useVectorContext != null) _config.UseVectorContext = (bool)configData.useVectorContext;
                if (configData.UseVectorContext != null) _config.UseVectorContext = (bool)configData.UseVectorContext;
                if (configData.useContextSummarization != null) _config.UseContextSummarization = (bool)configData.useContextSummarization;
                if (configData.UseContextSummarization != null) _config.UseContextSummarization = (bool)configData.UseContextSummarization;

                SaveConfig();
            }
        }
        
        public void AddAllowedUser(long userId)
        {
            lock (_configLock)
            {
                if (!_config.AllowedUserIds.Contains(userId))
                {
                    _config.AllowedUserIds.Add(userId);
                    SaveConfig();
                    Logger.LogInfo("CONFIG", $"Added user {userId} to allowed users list");
                }
            }
        }
        
        public void RemoveAllowedUser(long userId)
        {
            lock (_configLock)
            {
                if (_config.AllowedUserIds.Remove(userId))
                {
                    SaveConfig();
                    Logger.LogInfo("CONFIG", $"Removed user {userId} from allowed users list");
                    
                    // 删除用户数据目录
                    try
                    {
                        string userDataBasePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UserData");
                        string userDir = Path.Combine(userDataBasePath, userId.ToString());
                        if (Directory.Exists(userDir))
                        {
                            Directory.Delete(userDir, true);
                            Logger.LogInfo("CONFIG", $"Deleted user data directory: {userDir}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError("CONFIG", $"Failed to delete user data directory for user {userId}: {ex.Message}");
                    }
                }
            }
        }
        
        public bool IsUserAllowed(long userId)
        {
            lock (_configLock)
            {
                return _config.AllowedUserIds.Contains(userId);
            }
        }

        public void AddAllowedGroup(long groupId)
        {
            lock (_configLock)
            {
                if (!_config.AllowedGroupIds.Contains(groupId))
                {
                    _config.AllowedGroupIds.Add(groupId);
                    SaveConfig();
                    Logger.LogInfo("CONFIG", $"Added group {groupId} to allowed groups list");
                }
            }
        }

        public void RemoveAllowedGroup(long groupId)
        {
            lock (_configLock)
            {
                if (_config.AllowedGroupIds.Remove(groupId))
                {
                    SaveConfig();
                    Logger.LogInfo("CONFIG", $"Removed group {groupId} from allowed groups list");
                }
            }
        }

        public bool IsGroupAllowed(long groupId)
        {
            lock (_configLock)
            {
                return _config.AllowedGroupIds.Contains(groupId);
            }
        }

        public void AcceptEula()
        {
            lock (_configLock)
            {
                _config.EulaAccepted = true;
                _config.IsFirstRun = false;
                SaveConfig();
                Logger.LogInfo("CONFIG", "EULA accepted and saved");
            }
        }
    }
}
