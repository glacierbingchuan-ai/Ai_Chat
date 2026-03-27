using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
#if WINDOWS
using System.Management;
#endif
using AI_Chat.Models;
using AI_Chat.Plugins;
using Newtonsoft.Json;

namespace AI_Chat.Services.ControlPanel.Handlers
{
    public class SystemInfoHandler
    {
        private readonly ConfigManager _configManager;
        private readonly PluginManager _pluginManager;
        private readonly WebSocketClient _webSocketClient;
        private readonly DateTime _startTime;

#if WINDOWS
        private PerformanceCounter _cpuCounter;
        private DateTime _lastCpuCheck = DateTime.MinValue;
        private float _lastCpuValue = 0;
#else
        private DateTime _lastCpuCheck = DateTime.MinValue;
#endif

#if !WINDOWS
        private double _lastLinuxCpuTotal = 0;
        private double _lastLinuxCpuIdle = 0;
#endif

        private readonly WebSocketManager _wsManager;

        public SystemInfoHandler(
            ConfigManager configManager,
            PluginManager pluginManager,
            WebSocketClient webSocketClient,
            WebSocketManager wsManager,
            DateTime startTime)
        {
            _configManager = configManager;
            _pluginManager = pluginManager;
            _webSocketClient = webSocketClient;
            _wsManager = wsManager;
            _startTime = startTime;
        }

        public async Task HandleGetSystemInfoAsync(WebSocket webSocket, string replyTo, WebSocketHandler handler)
        {
            try
            {
                var process = Process.GetCurrentProcess();
                var uptime = (DateTime.Now - _startTime).TotalSeconds;

                var memoryInfo = GetSystemMemoryInfo();
                var cpuPercent = GetSystemCpuUsage();

                int runningPlugins = 0;
                int totalPlugins = 0;
                if (_pluginManager != null)
                {
                    var plugins = _pluginManager.GetAllPluginInfos();
                    totalPlugins = plugins.Count();
                    runningPlugins = plugins.Count(p => p.State == PluginState.Running);
                }

                var protocolInfo = await GetProtocolInfoAsync();

                var systemInfo = new
                {
                    currentVersion = GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0",
                    osVersion = Environment.OSVersion.ToString(),
                    dotnetVersion = Environment.Version.ToString(),
                    uptime = uptime,
                    uptimeFormatted = FormatUptime(uptime),
                    memoryUsage = memoryInfo.UsedMemoryMB,
                    memoryUsageFormatted = $"{memoryInfo.UsedMemoryMB:F1} MB / {memoryInfo.TotalMemoryMB:F1} MB",
                    memoryPercent = memoryInfo.MemoryPercent,
                    totalMemory = memoryInfo.TotalMemoryMB,
                    threadCount = process.Threads.Count,
                    processorCount = Environment.ProcessorCount,
                    cpuPercent = cpuPercent,
                    totalUsers = _configManager.Config.AllowedUserIds.Count,
                    totalGroups = _configManager.Config.AllowedGroupIds.Count,
                    runningPlugins = runningPlugins,
                    totalPlugins = totalPlugins,
                    llmModel = _configManager.Config.LlmModelName,
                    protocolInfo = protocolInfo
                };

                await handler.SendResponseAsync(webSocket, "system_info", systemInfo, replyTo);
            }
            catch (Exception ex)
            {
                Logger.LogError("ControlPanel", $"Error getting system info: {ex.Message}");
            }
        }

        public async Task HandleGetChangelogAsync(WebSocket webSocket, string replyTo)
        {
            try
            {
                string changelogPath = Path.Combine(Environment.CurrentDirectory, "ControlPanel", "public", "css", "Changelog.txt");
                string changelog = "Changelog not found";

                if (File.Exists(changelogPath))
                {
                    changelog = await File.ReadAllTextAsync(changelogPath);
                }

                var response = new WebSocketMessage
                {
                    Type = "changelog",
                    Data = changelog,
                    ReplyTo = replyTo
                };

                await _wsManager.SendServerMessageAsync(webSocket, response);
            }
            catch (Exception ex)
            {
                Logger.LogError("ControlPanel", $"Error reading changelog: {ex.Message}");
                var errorResponse = new WebSocketMessage
                {
                    Type = "changelog",
                    Data = "Error loading changelog",
                    ReplyTo = replyTo
                };
                await _wsManager.SendServerMessageAsync(webSocket, errorResponse);
            }
        }

        private (long TotalMemoryMB, long UsedMemoryMB, double MemoryPercent) GetSystemMemoryInfo()
        {
            try
            {
                long totalMemoryMB = 0;
                long usedMemoryMB = 0;

#if WINDOWS
                using (var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        var totalKB = Convert.ToUInt64(obj["TotalVisibleMemorySize"]);
                        var freeKB = Convert.ToUInt64(obj["FreePhysicalMemory"]);

                        totalMemoryMB = (long)(totalKB / 1024);
                        usedMemoryMB = (long)((totalKB - freeKB) / 1024);
                        break;
                    }
                }
#else
                if (File.Exists("/proc/meminfo"))
                {
                    var lines = File.ReadAllLines("/proc/meminfo");
                    long totalKB = 0;
                    long availableKB = 0;

                    foreach (var line in lines)
                    {
                        if (line.StartsWith("MemTotal:"))
                            totalKB = ParseMemInfoValue(line);
                        else if (line.StartsWith("MemAvailable:"))
                            availableKB = ParseMemInfoValue(line);
                    }

                    totalMemoryMB = totalKB / 1024;
                    usedMemoryMB = (totalKB - availableKB) / 1024;
                }
#endif

                double memoryPercent = totalMemoryMB > 0 ? (double)usedMemoryMB / totalMemoryMB * 100 : 0;
                return (totalMemoryMB, usedMemoryMB, memoryPercent);
            }
            catch (Exception ex)
            {
                Logger.LogError("ControlPanel", $"Error getting system memory info: {ex.Message}");
                return (0, 0, 0);
            }
        }

        private long ParseMemInfoValue(string line)
        {
            try
            {
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && long.TryParse(parts[1], out var value))
                    return value;
            }
            catch { }
            return 0;
        }

        private double GetSystemCpuUsage()
        {
            try
            {
#if WINDOWS
                if (_cpuCounter == null)
                {
                    _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                    _cpuCounter.NextValue();
                    Thread.Sleep(100);
                }

                if ((DateTime.Now - _lastCpuCheck).TotalMilliseconds < 500)
                {
                    return _lastCpuValue;
                }

                _lastCpuValue = _cpuCounter.NextValue();
                _lastCpuCheck = DateTime.Now;
                return _lastCpuValue;
#else
                if (File.Exists("/proc/stat"))
                {
                    return GetLinuxCpuUsage();
                }
                return 0;
#endif
            }
            catch (Exception ex)
            {
                Logger.LogError("ControlPanel", $"Error getting system CPU usage: {ex.Message}");
                return 0;
            }
        }

#if !WINDOWS
        private double GetLinuxCpuUsage()
        {
            try
            {
                var lines = File.ReadAllLines("/proc/stat");
                var cpuLine = lines.FirstOrDefault(l => l.StartsWith("cpu "));
                if (cpuLine == null) return 0;

                var values = cpuLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                                   .Skip(1)
                                   .Select(v => double.TryParse(v, out var d) ? d : 0)
                                   .ToArray();

                if (values.Length < 4) return 0;

                var user = values[0];
                var nice = values[1];
                var system = values[2];
                var idle = values[3];

                var total = user + nice + system + idle;
                var totalDelta = total - _lastLinuxCpuTotal;
                var idleDelta = idle - _lastLinuxCpuIdle;

                _lastLinuxCpuTotal = total;
                _lastLinuxCpuIdle = idle;

                if (totalDelta > 0)
                {
                    return (1 - idleDelta / totalDelta) * 100;
                }

                return 0;
            }
            catch { }
            return 0;
        }
#endif

        private async Task<object> GetProtocolInfoAsync()
        {
            var protocolInfo = new
            {
                isConnected = _webSocketClient?.IsConnected ?? false,
                nickname = "Unknown",
                protocolType = "Unknown",
                userId = "",
                avatarUrl = ""
            };

            if (_webSocketClient?.IsConnected != true)
                return protocolInfo;

            try
            {
                return new
                {
                    isConnected = true,
                    nickname = _webSocketClient.BotNickname,
                    protocolType = _webSocketClient.ProtocolType,
                    userId = _webSocketClient.BotUserId > 0 ? _webSocketClient.BotUserId.ToString() : "",
                    avatarUrl = _webSocketClient.BotAvatarUrl ?? ""
                };
            }
            catch (Exception ex)
            {
                Logger.LogError("ControlPanel", $"Error getting protocol info: {ex.Message}");
            }

            return protocolInfo;
        }

        private string FormatUptime(double seconds)
        {
            var timeSpan = TimeSpan.FromSeconds(seconds);
            if (timeSpan.Days >= 1)
                return $"{timeSpan.Days}天 {timeSpan.Hours}小时 {timeSpan.Minutes}分钟";
            else if (timeSpan.Hours >= 1)
                return $"{timeSpan.Hours}小时 {timeSpan.Minutes}分钟 {timeSpan.Seconds}秒";
            else if (timeSpan.Minutes >= 1)
                return $"{timeSpan.Minutes}分钟 {timeSpan.Seconds}秒";
            else
                return $"{timeSpan.Seconds}秒";
        }
    }
}
