using System;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using AI_Chat.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Chat.Services
{
    public class VersionCheckResult
    {
        public bool HasUpdate { get; set; }
        public bool IsVersionAllowed { get; set; }
        public string LatestVersion { get; set; }
        public string MinimumAllowedVersion { get; set; }
        public string CurrentVersion { get; set; }
        public string[] UpdateContent { get; set; }
        public string UpdateUrl { get; set; }
        public string ErrorMessage { get; set; }
    }

    public class VersionCheckService
    {
        private const string VERSION_URL = "https://gitee.com/bingchuankeji/Character_Cards/raw/main/version.json";
        private const string CURRENT_VERSION = "1.3.1";
        private readonly HttpClient _httpClient;
        private readonly ControlPanelServer _controlPanelServer;
        private VersionCheckResult _lastCheckResult;

        public VersionCheckService(ControlPanelServer controlPanelServer)
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
            _controlPanelServer = controlPanelServer;
        }

        public string CurrentVersion => CURRENT_VERSION;

        public async Task<VersionCheckResult> CheckVersionAsync()
        {
            try
            {
                var response = await _httpClient.GetStringAsync(VERSION_URL);
                var versionData = JObject.Parse(response);

                var latestVersion = versionData["latest_version"]?.ToString();
                var minimumAllowedVersion = versionData["minimum_allowed_version"]?.ToString();
                var updateContent = versionData["latest_update_content"]?.ToObject<string[]>();
                var updateUrl = versionData["update_url"]?.ToString();

                if (string.IsNullOrEmpty(latestVersion) || string.IsNullOrEmpty(minimumAllowedVersion))
                {
                    return new VersionCheckResult
                {
                    HasUpdate = false,
                    IsVersionAllowed = true,
                    CurrentVersion = CURRENT_VERSION,
                    UpdateUrl = "https://gitee.com/bingchuankeji/Ai_Chat",
                    ErrorMessage = "Version information format error"
                };
                }

                var currentVersion = new Version(CURRENT_VERSION);
                var latest = new Version(latestVersion);
                var minimum = new Version(minimumAllowedVersion);

                var hasUpdate = currentVersion < latest;
                var isVersionAllowed = currentVersion >= minimum;

                var result = new VersionCheckResult
                {
                    HasUpdate = hasUpdate,
                    IsVersionAllowed = isVersionAllowed,
                    LatestVersion = latestVersion,
                    MinimumAllowedVersion = minimumAllowedVersion,
                    CurrentVersion = CURRENT_VERSION,
                    UpdateContent = updateContent ?? new string[0],
                    UpdateUrl = updateUrl ?? "https://gitee.com/bingchuankeji/Ai_Chat"
                };

                _lastCheckResult = result;
                return result;
            }
            catch (Exception ex)
            {
                Logger.LogError("VERSION_CHECK", $"Version check failed: {ex.Message}");
                return new VersionCheckResult
                {
                    HasUpdate = false,
                    IsVersionAllowed = true,
                    CurrentVersion = CURRENT_VERSION,
                    UpdateUrl = "https://gitee.com/bingchuankeji/Ai_Chat",
                    ErrorMessage = $"Version check failed: {ex.Message}"
                };
            }
        }

        public async Task PerformVersionCheckAndNotifyAsync()
        {
            Logger.LogInfo("VERSION_CHECK", "Checking for version updates...");
            
            var result = await CheckVersionAsync();
            
            if (!string.IsNullOrEmpty(result.ErrorMessage) && !result.ErrorMessage.Contains("格式错误"))
            {
                Logger.LogWarning("VERSION_CHECK", result.ErrorMessage);
                return;
            }

            Logger.LogInfo("VERSION_CHECK", $"Current version: {result.CurrentVersion}, Latest version: {result.LatestVersion}, Minimum allowed version: {result.MinimumAllowedVersion}");

            if (result.HasUpdate || !result.IsVersionAllowed)
            {
                var versionData = new
                {
                    hasUpdate = result.HasUpdate,
                    isVersionAllowed = result.IsVersionAllowed,
                    currentVersion = result.CurrentVersion,
                    latestVersion = result.LatestVersion,
                    minimumAllowedVersion = result.MinimumAllowedVersion,
                    updateContent = result.UpdateContent,
                    updateUrl = result.UpdateUrl
                };

                var message = new WebSocketMessage
                {
                    Type = "version_check_result",
                    Data = versionData
                };

                _controlPanelServer.BroadcastMessageToClients(message);
                Logger.LogInfo("VERSION_CHECK", "版本检查结果已发送至前端");
            }
            else
            {
                Logger.LogInfo("VERSION_CHECK", "Current version is up to date, no update needed");
            }
        }

        public VersionCheckResult GetLastCheckResult()
        {
            return _lastCheckResult;
        }
    }
}
