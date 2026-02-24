using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AI_Chat.Models;
using AI_Chat.Constants;
using AI_Chat.Plugins;
using AI_Chat.Plugins.Virtualization;
using AI_Chat.Managers;
using Newtonsoft.Json;

namespace AI_Chat.Services
{
    public class ControlPanelServer
    {
        private readonly ConfigManager _configManager;
        private readonly UserSessionManager _sessionManager;
        private readonly UserConfigManager _userConfigManager;
        private readonly LLMService _llmService;
        private readonly MessageHandler _messageHandler;
        private readonly CancellationTokenSource _globalCts;
        private readonly PluginManager _pluginManager;
        private readonly PluginWebSocketHandler _pluginWebSocketHandler;
        private readonly VirtualizationWebSocketHandler _virtualizationWebSocketHandler;

        private readonly object _controlPanelLock = new object();
        private readonly ConcurrentDictionary<WebSocket, SemaphoreSlim> _clientLocks = new ConcurrentDictionary<WebSocket, SemaphoreSlim>();

        private HttpListener _httpListener;
        private List<WebSocket> _controlPanelClients = new List<WebSocket>();
        private string _controlPanelKey;
        private DateTime _startTime = DateTime.Now;
        private long _selectedUserId = 0;
        private VersionCheckService _versionCheckService;

        public ControlPanelServer(
            ConfigManager configManager,
            UserSessionManager sessionManager,
            UserConfigManager userConfigManager,
            LLMService llmService,
            MessageHandler messageHandler,
            CancellationTokenSource globalCts,
            PluginManager pluginManager = null,
            PluginVirtualizationManager virtualizationManager = null)
        {
            _configManager = configManager;
            _sessionManager = sessionManager;
            _userConfigManager = userConfigManager;
            _llmService = llmService;
            _messageHandler = messageHandler;
            _globalCts = globalCts;
            _pluginManager = pluginManager;
            _pluginWebSocketHandler = pluginManager != null ? new PluginWebSocketHandler(pluginManager, virtualizationManager) : null;
            _virtualizationWebSocketHandler = virtualizationManager != null && pluginManager != null 
                ? new VirtualizationWebSocketHandler(virtualizationManager, pluginManager) 
                : null;
            _controlPanelKey = GenerateSecureKey();
            
            // Don't auto select user if there are none
            _selectedUserId = 0;
            if (_configManager.Config.AllowedUserIds.Count > 0)
            {
                _selectedUserId = _configManager.Config.AllowedUserIds[0];
            }
        }

        public string ControlPanelKey => _controlPanelKey;
        public string ControlPanelUrl => $"http://localhost:{AppConstants.CONTROL_PANEL_PORT}?key={_controlPanelKey}";

        public void SetVersionCheckService(VersionCheckService versionCheckService)
        {
            _versionCheckService = versionCheckService;
        }

        public void InitializeBroadcastCallbacks()
        {
            Logger.Initialize(BroadcastMessageToClients);
            
            foreach (var userId in _configManager.Config.AllowedUserIds)
            {
                var chatHistoryManager = _sessionManager.GetOrCreateChatHistoryManager(userId);
                chatHistoryManager.Initialize(
                    msg => BroadcastMessageToClients(new WebSocketMessage { Type = "chat_message", Data = msg }),
                    () => BroadcastChatHistory()
                );
            }

            PluginApi.OnConfigChanged += config =>
            {
                BroadcastMessageToClients(new WebSocketMessage { Type = "config_updated", Data = config });
            };
        }

        public async Task StartAsync()
        {
            try
            {
                _httpListener = new HttpListener();
                _httpListener.Prefixes.Add($"http://*:{AppConstants.CONTROL_PANEL_PORT}/");
                _httpListener.Start();

                Logger.LogInfo("SYSTEM", $"Control Panel Access Key: {_controlPanelKey}");
                Logger.LogInfo("SYSTEM", $"Control Panel URL: {ControlPanelUrl}");

                DialogResult result = MessageBox.Show("Do you want to open the control panel?", "Control Panel", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (result == DialogResult.Yes)
                    Process.Start(new ProcessStartInfo(ControlPanelUrl) { UseShellExecute = true });

                while (!_globalCts.IsCancellationRequested)
                {
                    var context = await _httpListener.GetContextAsync();
                    _ = Task.Run(() => HandleHttpRequestAsync(context));
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("CONTROL_PANEL", $"Error starting control panel server on port {AppConstants.CONTROL_PANEL_PORT}", ex);
            }
        }

        private async Task HandleHttpRequestAsync(HttpListenerContext context)
        {
            try
            {
                string path = context.Request.Url.LocalPath;

                if (context.Request.HttpMethod == "GET" && context.Request.Url.PathAndQuery == "/health")
                    ServeHealthCheck(context);
                else if (context.Request.HttpMethod == "GET" &&
                    (context.Request.Url.PathAndQuery.StartsWith("/css/") ||
                     context.Request.Url.PathAndQuery.StartsWith("/js/") ||
                     context.Request.Url.PathAndQuery.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase)))
                    ServeStaticFile(context);
                else if (context.Request.HttpMethod == "GET" && context.Request.Url.PathAndQuery == "/unauthorized.html")
                    ServeUnauthorizedHtml(context);
                else
                {
                    if (context.Request.HttpMethod == "GET" && context.Request.Url.PathAndQuery.StartsWith(AppConstants.CONTROL_PANEL_PREFIX))
                    {
                        if (!ValidateControlPanelAccess(context))
                        {
                            await HandleUnauthorizedWebSocketRequestAsync(context);
                            return;
                        }
                        await HandleWebSocketRequestAsync(context);
                    }
                    else
                    {
                        if (!ValidateControlPanelAccess(context))
                        {
                            RedirectToUnauthorized(context);
                            return;
                        }

                        if (context.Request.HttpMethod == "GET" && (path == "/" || path == "/index.html"))
                            ServeControlPanelHtml(context);
                        else if (context.Request.HttpMethod == "GET" && path == "/api/config")
                            ServeConfig(context);
                        else if (context.Request.HttpMethod == "POST" && path == "/api/config")
                            await UpdateConfigAsync(context);
                        else if (context.Request.HttpMethod == "GET" && path == "/api/logs")
                            ServeLogs(context);
                        else if (context.Request.HttpMethod == "DELETE" && path == "/api/logs")
                            ClearLogs(context);
                        else if (context.Request.HttpMethod == "GET" && path == "/api/eula-status")
                            ServeEulaStatus(context);
                        else if (context.Request.HttpMethod == "POST" && path == "/api/accept-eula")
                            await AcceptEulaAsync(context);
                        else if (context.Request.HttpMethod == "GET" && path == "/api/proxy")
                            await ServeProxyAsync(context);
                        else
                        { context.Response.StatusCode = 404; context.Response.Close(); }
                    }
                }
            }
            catch { context.Response.Close(); }
        }

        private async Task HandleWebSocketRequestAsync(HttpListenerContext context)
        {
            try
            {
                var wsContext = await context.AcceptWebSocketAsync(null);
                var webSocket = wsContext.WebSocket;
                _clientLocks[webSocket] = new SemaphoreSlim(1, 1);
                lock (_controlPanelLock) _controlPanelClients.Add(webSocket);
                BroadcastMessageToClients(new WebSocketMessage { Type = "client_count_updated", Data = _controlPanelClients.Count });
                await SendInitialDataAsync(webSocket);
                await HandleWebSocketMessagesAsync(webSocket);
            }
            catch { context.Response.Close(); }
        }

        private async Task SendSafeAsync(WebSocket webSocket, string message)
        {
            if (webSocket == null || webSocket.State != WebSocketState.Open)
                return;

            if (!_clientLocks.TryGetValue(webSocket, out var semaphore))
                return;

            await semaphore.WaitAsync();
            try
            {
                if (webSocket.State == WebSocketState.Open)
                {
                    await webSocket.SendAsync(
                        new ArraySegment<byte>(Encoding.UTF8.GetBytes(message)),
                        WebSocketMessageType.Text,
                        true,
                        CancellationToken.None);
                }
            }
            catch (WebSocketException)
            {
            }
            catch (Exception ex)
            {
                Logger.LogError("CONTROL_PANEL", $"Error sending WebSocket message: {ex.Message}");
            }
            finally
            {
                semaphore.Release();
            }
        }

        private async Task HandleWebSocketMessagesAsync(WebSocket webSocket)
        {
            try
            {
                var buffer = new byte[1024 * 8];
                var messageBuilder = new StringBuilder();
                while (webSocket.State == WebSocketState.Open)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _globalCts.Token);
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                        if (result.EndOfMessage)
                        {
                            string json = messageBuilder.ToString();
                            messageBuilder.Clear();
                            await ProcessWebSocketMessageAsync(webSocket, json);
                        }
                    }
                    else if (result.MessageType == WebSocketMessageType.Close) break;
                }
            }
            catch { }
            finally
            {
                lock (_controlPanelLock)
                {
                    _controlPanelClients.Remove(webSocket);
                    _clientLocks.TryRemove(webSocket, out _);
                }
                BroadcastMessageToClients(new WebSocketMessage { Type = "client_count_updated", Data = _controlPanelClients.Count });
            }
        }

        private async Task ProcessWebSocketMessageAsync(WebSocket webSocket, string json)
        {
            try
            {
                var message = JsonConvert.DeserializeObject<WebSocketMessage>(json);
                switch (message.Type)
                {
                    case "get_logs":
                        await SendLogsAsync(webSocket);
                        break;
                    case "clear_logs":
                        Logger.ClearLogs();
                        BroadcastMessageToClients(new WebSocketMessage { Type = "logs_cleared" });
                        break;
                    case "clear_context":
                        await ClearSelectedUserContextAsync();
                        break;
                    case "clear_context_for_user":
                        await ClearContextForUserAsync(message.Data);
                        break;
                    case "config_update":
                        UpdateConfig(message.Data);
                        BroadcastMessageToClients(new WebSocketMessage { Type = "config_updated", Data = _configManager.Config });
                        break;
                    case "get_llm_status":
                        _ = Task.Run(async () =>
                        {
                            string status = await _llmService.GetLlmStatusAsync();
                            await SendSafeAsync(webSocket, JsonConvert.SerializeObject(new WebSocketMessage { Type = "llm_status", Data = status }));
                        });
                        break;
                    case "test_llm_connection":
                        _ = Task.Run(async () =>
                        {
                            await TestLlmConnectionAsync(webSocket, message.Data);
                        });
                        break;
                    case "get_runtime":
                        double uptime = (DateTime.Now - _startTime).TotalSeconds;
                        await SendSafeAsync(webSocket, JsonConvert.SerializeObject(new WebSocketMessage { Type = "runtime", Data = uptime }));
                        break;
                    case "use_role_card":
                        await HandleUseRoleCardAsync(webSocket, message.Data);
                        break;
                    case "get_chat_history":
                        BroadcastChatHistory();
                        break;
                    case "select_user":
                        await HandleSelectUserAsync(webSocket, message.Data);
                        break;
                    case "get_users":
                        await SendUsersListAsync(webSocket);
                        break;
                    case "add_allowed_user":
                        HandleAddAllowedUser(message.Data);
                        break;
                    case "remove_allowed_user":
                        HandleRemoveAllowedUser(message.Data);
                        break;
                    case "add_allowed_group":
                        HandleAddAllowedGroup(message.Data);
                        break;
                    case "remove_allowed_group":
                        HandleRemoveAllowedGroup(message.Data);
                        break;
                    case "get_user_config":
                        await HandleGetUserConfigAsync(webSocket, message.Data);
                        break;
                    case "update_user_config":
                        await HandleUpdateUserConfigAsync(webSocket, message.Data);
                        break;
                    case "reset_user_config":
                        await HandleResetUserConfigAsync(webSocket, message.Data);
                        break;
                    case "confirm_version_exit":
                        HandleVersionExitConfirmation();
                        break;
                    case "reject_eula":
                        HandleRejectEula();
                        break;
                    default:
                        Logger.LogInfo("ControlPanel", $"Received message: {message.Type}");
                        if (_pluginWebSocketHandler != null &&
                            (message.Type.StartsWith("plugin_") ||
                             message.Type == "get_plugins" || message.Type == "start_plugin" ||
                             message.Type == "stop_plugin" || message.Type == "reload_plugin" ||
                             message.Type == "unload_plugin" || message.Type == "get_plugin_config" ||
                             message.Type == "set_plugin_config" || message.Type == "execute_plugin_command" ||
                             message.Type == "get_plugin_commands" || message.Type == "load_plugin_from_file" ||
                             message.Type == "upload_and_load_plugin" || message.Type == "get_plugin_readme" ||
                             message.Type == "get_plugin_permissions" || message.Type == "approve_plugin"))
                        {
                            Logger.LogInfo("ControlPanel", $"Routing to plugin handler: {message.Type}");
                            await _pluginWebSocketHandler.HandleMessageAsync(webSocket, message.Type, message.Data);
                        }
                        else if (_virtualizationWebSocketHandler != null &&
                            (message.Type == "get_virtualization_data" ||
                             message.Type == "get_plugin_virtualization_data" ||
                             message.Type == "get_virtual_registry" ||
                             message.Type == "get_virtual_files" ||
                             message.Type == "get_virtualization_stats" ||
                             message.Type == "clear_plugin_virtualization" ||
                             message.Type == "toggle_virtualization" ||
                             message.Type == "delete_virtual_registry_key" ||
                             message.Type == "delete_virtual_file"))
                        {
                            Logger.LogInfo("ControlPanel", $"Routing to virtualization handler: {message.Type}");
                            await _virtualizationWebSocketHandler.HandleMessageAsync(webSocket, message.Type, message.Data);
                        }
                        else
                        {
                            Logger.LogWarning("ControlPanel", $"Unhandled message type: {message.Type}");
                        }
                        break;
                }
            }
            catch { }
        }

        private async Task HandleSelectUserAsync(WebSocket webSocket, dynamic data)
        {
            try
            {
                long userId = (long)data.userId;
                if (_configManager.Config.AllowedUserIds.Contains(userId))
                {
                    _selectedUserId = userId;
                    
                    await SendSafeAsync(webSocket, JsonConvert.SerializeObject(new WebSocketMessage { 
                        Type = "user_selecting", 
                        Data = new { userId = userId } 
                    }));
                    
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await SendInitialDataAsync(webSocket);
                            Logger.LogInfo("ControlPanel", $"Selected user: {userId}");
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError("ControlPanel", $"Error sending initial data for user {userId}: {ex.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("ControlPanel", $"Error selecting user: {ex.Message}");
            }
        }

        private async Task SendUsersListAsync(WebSocket webSocket)
        {
            try
            {
                var users = _configManager.Config.AllowedUserIds.Select(id => new
                {
                    userId = id,
                    stats = _sessionManager.GetSession(id)?.GetStats()
                }).ToList();

                var groups = _configManager.Config.AllowedGroupIds.Select(id => new
                {
                    groupId = id
                }).ToList();

                await SendSafeAsync(webSocket, JsonConvert.SerializeObject(new WebSocketMessage 
                { 
                    Type = "users_list", 
                    Data = new { users = users, groups = groups }
                }));
            }
            catch (Exception ex)
            {
                Logger.LogError("ControlPanel", $"Error sending users list: {ex.Message}");
            }
        }

        private void HandleAddAllowedUser(dynamic data)
        {
            try
            {
                long userId = (long)data.userId;
                
                // Validate QQ number is at least 5 digits
                string userIdStr = userId.ToString();
                if (userIdStr.Length < 5)
                {
                    Logger.LogWarning("ControlPanel", $"Rejected user ID {userId}: must be at least 5 digits");
                    return;
                }
                
                _configManager.AddAllowedUser(userId);
                
                // If it's the first user, select it
                if (_selectedUserId == 0 && _configManager.Config.AllowedUserIds.Count > 0)
                {
                    _selectedUserId = _configManager.Config.AllowedUserIds[0];
                }
                
                BroadcastMessageToClients(new WebSocketMessage { Type = "config_updated", Data = _configManager.Config });
            }
            catch (Exception ex)
            {
                Logger.LogError("ControlPanel", $"Error adding allowed user: {ex.Message}");
            }
        }

        private void HandleRemoveAllowedUser(dynamic data)
        {
            try
            {
                long userId = (long)data.userId;
                _configManager.RemoveAllowedUser(userId);
                if (_selectedUserId == userId)
                {
                    if (_configManager.Config.AllowedUserIds.Count > 0)
                    {
                        _selectedUserId = _configManager.Config.AllowedUserIds[0];
                    }
                    else
                    {
                        _selectedUserId = 0;
                    }
                }
                BroadcastMessageToClients(new WebSocketMessage { Type = "config_updated", Data = _configManager.Config });
            }
            catch (Exception ex)
            {
                Logger.LogError("ControlPanel", $"Error removing allowed user: {ex.Message}");
            }
        }

        private void HandleAddAllowedGroup(dynamic data)
        {
            try
            {
                long groupId = (long)data.groupId;
                
                // Validate group ID is at least 5 digits
                string groupIdStr = groupId.ToString();
                if (groupIdStr.Length < 5)
                {
                    Logger.LogWarning("ControlPanel", $"Rejected group ID {groupId}: must be at least 5 digits");
                    return;
                }
                
                _configManager.AddAllowedGroup(groupId);
                BroadcastMessageToClients(new WebSocketMessage { Type = "config_updated", Data = _configManager.Config });
            }
            catch (Exception ex)
            {
                Logger.LogError("ControlPanel", $"Error adding allowed group: {ex.Message}");
            }
        }

        private void HandleRemoveAllowedGroup(dynamic data)
        {
            try
            {
                long groupId = (long)data.groupId;
                _configManager.RemoveAllowedGroup(groupId);
                BroadcastMessageToClients(new WebSocketMessage { Type = "config_updated", Data = _configManager.Config });
            }
            catch (Exception ex)
            {
                Logger.LogError("ControlPanel", $"Error removing allowed group: {ex.Message}");
            }
        }

        private async Task HandleGetUserConfigAsync(WebSocket webSocket, dynamic data)
        {
            try
            {
                long userId = _selectedUserId;
                if (data?.userId != null)
                {
                    userId = (long)data.userId;
                }
                
                var userConfig = _userConfigManager.GetOrCreateUserConfig(userId);
                await SendSafeAsync(webSocket, JsonConvert.SerializeObject(new WebSocketMessage { Type = "user_config", Data = userConfig }));
            }
            catch (Exception ex)
            {
                Logger.LogError("ControlPanel", $"Error getting user config: {ex.Message}");
            }
        }

        private async Task HandleUpdateUserConfigAsync(WebSocket webSocket, dynamic data)
        {
            try
            {
                long userId = _selectedUserId;
                if (data?.userId != null)
                {
                    userId = (long)data.userId;
                }

                _userConfigManager.UpdateUserConfig(userId, data);
                var userConfig = _userConfigManager.GetUserConfig(userId);
                BroadcastMessageToClients(new WebSocketMessage { Type = "user_config_updated", Data = userConfig });
            }
            catch (Exception ex)
            {
                Logger.LogError("ControlPanel", $"Error updating user config: {ex.Message}");
            }
        }

        private async Task HandleResetUserConfigAsync(WebSocket webSocket, dynamic data)
        {
            try
            {
                long userId = _selectedUserId;
                if (data?.userId != null)
                {
                    userId = (long)data.userId;
                }

                _userConfigManager.ResetUserConfig(userId, data);
                var userConfig = _userConfigManager.GetUserConfig(userId);
                BroadcastMessageToClients(new WebSocketMessage { Type = "user_config_updated", Data = userConfig });
            }
            catch (Exception ex)
            {
                Logger.LogError("ControlPanel", $"Error resetting user config: {ex.Message}");
            }
        }

        private void HandleVersionExitConfirmation()
        {
            Logger.LogInfo("VERSION_CHECK", "用户确认退出应用程序");
            Task.Run(async () =>
            {
                await Task.Delay(500);
                Environment.Exit(0);
            });
        }

        private void HandleRejectEula()
        {
            Logger.LogInfo("EULA", "用户在控制面板拒绝使用协议，应用程序即将退出");
            // 立即退出，不延迟
            Task.Run(() =>
            {
                try
                {
                    // 给日志一点时间写入
                    System.Threading.Thread.Sleep(500);
                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    Logger.LogError("EULA", $"退出程序时出错: {ex.Message}");
                    // 强制退出
                    System.Diagnostics.Process.GetCurrentProcess().Kill();
                }
            });
        }

        private async Task ClearSelectedUserContextAsync()
        {
            if (_selectedUserId == 0) return;
            
            var contextManager = _sessionManager.GetOrCreateContextManager(_selectedUserId);
            var chatHistoryManager = _sessionManager.GetOrCreateChatHistoryManager(_selectedUserId);
            
            contextManager.ClearContext();
            chatHistoryManager.ClearHistory();
            
            BroadcastMessageToClients(new WebSocketMessage { Type = "context_cleared" });
            BroadcastMessageToClients(new WebSocketMessage { Type = "scheduled_events_updated", Data = contextManager.ScheduledEvents });
        }

        private async Task ClearContextForUserAsync(dynamic data)
        {
            if (data == null) return;
            
            try
            {
                long userId = 0;
                
                if (data.userId != null)
                {
                    userId = (long)data.userId;
                }
                
                if (userId == 0) return;
                
                var contextManager = _sessionManager.GetOrCreateContextManager(userId);
                var chatHistoryManager = _sessionManager.GetOrCreateChatHistoryManager(userId);
                
                contextManager.ClearContext();
                chatHistoryManager.ClearHistory();
                
                BroadcastMessageToClients(new WebSocketMessage { Type = "context_cleared", Data = new { userId = userId } });
                BroadcastMessageToClients(new WebSocketMessage { Type = "scheduled_events_updated", Data = contextManager.ScheduledEvents });
                
                Logger.LogInfo("ControlPanel", $"已清空用户 {userId} 的上下文");
            }
            catch (Exception ex)
            {
                Logger.LogError("ControlPanel", $"清空用户上下文失败: {ex.Message}");
            }
        }

        private async Task SendInitialDataAsync(WebSocket webSocket)
        {
            try
            {
                // Select user if not selected but there are available users
                if (_selectedUserId == 0 && _configManager.Config.AllowedUserIds.Count > 0)
                {
                    _selectedUserId = _configManager.Config.AllowedUserIds[0];
                }

                var contextManager = _selectedUserId > 0 ? _sessionManager.GetOrCreateContextManager(_selectedUserId) : null;
                var chatHistoryManager = _selectedUserId > 0 ? _sessionManager.GetOrCreateChatHistoryManager(_selectedUserId) : null;
                var session = _selectedUserId > 0 ? _sessionManager.GetSession(_selectedUserId) : null;
                var userConfig = _selectedUserId > 0 ? _userConfigManager.GetOrCreateUserConfig(_selectedUserId) : null;

                var initialData = new
                {
                    logs = Logger.GetLogs(),
                    config = _configManager.Config,
                    userConfig = userConfig,
                    uptime = (DateTime.Now - _startTime).TotalSeconds,
                    scheduledEvents = contextManager?.ScheduledEvents ?? new List<EventModel>(),
                    stats = _sessionManager.GetAllSessionStats(),
                    chatHistory = chatHistoryManager?.GetHistory() ?? new List<ChatMessage>(),
                    selectedUserId = _selectedUserId,
                    activeUsers = _sessionManager.GetActiveUserIds()
                };
                await SendSafeAsync(webSocket, JsonConvert.SerializeObject(new WebSocketMessage { Type = "init", Data = initialData }));
                
                // If version check results exist, send immediately to newly connected client
                var versionResult = _versionCheckService?.GetLastCheckResult();
                if (versionResult != null && (versionResult.HasUpdate || !versionResult.IsVersionAllowed))
                {
                    var versionData = new
                    {
                        hasUpdate = versionResult.HasUpdate,
                        isVersionAllowed = versionResult.IsVersionAllowed,
                        currentVersion = versionResult.CurrentVersion,
                        latestVersion = versionResult.LatestVersion,
                        minimumAllowedVersion = versionResult.MinimumAllowedVersion,
                        updateContent = versionResult.UpdateContent,
                        updateUrl = versionResult.UpdateUrl
                    };
                    
                    await SendSafeAsync(webSocket, JsonConvert.SerializeObject(new WebSocketMessage 
                    { 
                        Type = "version_check_result", 
                        Data = versionData 
                    }));
                }
            }
            catch { }
        }

        private async Task SendLogsAsync(WebSocket webSocket)
        {
            var message = new WebSocketMessage { Type = "logs", Data = Logger.GetLogs() };
            await SendSafeAsync(webSocket, JsonConvert.SerializeObject(message));
        }

        private void UpdateConfig(dynamic configData)
        {
            _configManager.UpdateConfig(configData);
            _llmService.UpdateApiKey(_configManager.Config.LlmApiKey);
        }

        private async Task TestLlmConnectionAsync(WebSocket webSocket, dynamic testConfig)
        {
            try
            {
                string modelName = testConfig?.llmModelName?.ToString();
                string apiBaseUrl = testConfig?.llmApiBaseUrl?.ToString();
                string apiKey = testConfig?.llmApiKey?.ToString();
                var result = await _llmService.CheckLlmApiStatusAsync(modelName, apiBaseUrl, apiKey);
                string message = (string)result["message"];
                await SendSafeAsync(webSocket, JsonConvert.SerializeObject(new WebSocketMessage { Type = "llm_test_result", Data = message }));
            }
            catch (Exception ex) { Logger.LogError("CONTROL_PANEL", "Error testing LLM connection", ex); }
        }

        private async Task HandleUseRoleCardAsync(WebSocket webSocket, dynamic data)
        {
            try
            {
                long targetUserId = _selectedUserId;
                if (data?.userId != null)
                {
                    targetUserId = (long)data.userId;
                }
                
                if (targetUserId == 0)
                {
                    await SendSafeAsync(webSocket, JsonConvert.SerializeObject(new WebSocketMessage { Type = "role_card_error", Data = "No user selected" }));
                    return;
                }

                string baseSystemPrompt = data?.baseSystemPrompt?.ToString();
                dynamic emojisData = data?.roleCardAvailableEmojis;

                if (!string.IsNullOrEmpty(baseSystemPrompt))
                {
                    var userConfig = _userConfigManager.GetOrCreateUserConfig(targetUserId);
                    userConfig.BaseSystemPrompt = baseSystemPrompt;
                    _userConfigManager.UpdateUserConfig(targetUserId, userConfig);
                }

                if (emojisData != null)
                {
                    List<string> emojiUrls = new List<string>();
                    try
                    {
                        foreach (var emoji in emojisData)
                        {
                            emojiUrls.Add(emoji.ToString());
                        }
                        if (emojiUrls.Count > 0)
                        {
                            await DownloadEmojisAsync(emojiUrls);
                        }
                    }
                    catch { }
                }

                await SendSafeAsync(webSocket, JsonConvert.SerializeObject(new WebSocketMessage { Type = "role_card_used", Data = "Role card applied successfully" }));

                var contextManager = _sessionManager.GetOrCreateContextManager(targetUserId);
                var chatHistoryManager = _sessionManager.GetOrCreateChatHistoryManager(targetUserId);
                contextManager.ClearContext();
                chatHistoryManager.ClearHistory();

                BroadcastMessageToClients(new WebSocketMessage { Type = "context_cleared" });
                
                if (targetUserId == _selectedUserId)
                {
                    BroadcastMessageToClients(new WebSocketMessage { Type = "scheduled_events_updated", Data = contextManager.ScheduledEvents });
                    var updatedUserConfig = _userConfigManager.GetUserConfig(targetUserId);
                    BroadcastMessageToClients(new WebSocketMessage { Type = "user_config_updated", Data = updatedUserConfig });
                }

                Logger.LogInfo("ROLE_CARDS", "Role card used successfully for user " + targetUserId);
            }
            catch (Exception ex)
            {
                Logger.LogError("ROLE_CARDS", "Error handling use_role_card: " + ex.Message);
                await SendSafeAsync(webSocket, JsonConvert.SerializeObject(new WebSocketMessage { Type = "role_card_error", Data = "Failed to apply role card: " + ex.Message }));
            }
        }

        private async Task DownloadEmojisAsync(List<string> emojiUrls)
        {
            try
            {
                string memeFolder = Path.Combine(Environment.CurrentDirectory, "meme");
                if (!Directory.Exists(memeFolder))
                {
                    Directory.CreateDirectory(memeFolder);
                    Logger.LogInfo("ROLE_CARDS", "Created meme folder: " + memeFolder);
                }

                using (var client = new System.Net.Http.HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(15);

                    foreach (string emojiUrl in emojiUrls)
                    {
                        try
                        {
                            string cleanUrl = emojiUrl.Trim().Trim('"', '`');
                            if (string.IsNullOrEmpty(cleanUrl)) continue;

                            string fileName = Path.GetFileName(cleanUrl);
                            if (string.IsNullOrEmpty(fileName)) continue;

                            string filePath = Path.Combine(memeFolder, fileName);

                            var response = await client.GetAsync(cleanUrl);
                            if (response.IsSuccessStatusCode)
                            {
                                byte[] content = await response.Content.ReadAsByteArrayAsync();
                                File.WriteAllBytes(filePath, content);
                                Logger.LogInfo("ROLE_CARDS", "Downloaded emoji: " + fileName);
                            }
                            else
                            {
                                Logger.LogWarning("ROLE_CARDS", "Failed to download emoji: " + cleanUrl);
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError("ROLE_CARDS", "Error downloading emoji " + emojiUrl + ": " + ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("ROLE_CARDS", "Error in DownloadEmojisAsync: " + ex.Message);
            }
        }

        public void BroadcastMessageToClients(WebSocketMessage message)
        {
            string json = JsonConvert.SerializeObject(message);
            byte[] buffer = Encoding.UTF8.GetBytes(json);
            lock (_controlPanelLock)
            {
                var clientsToRemove = new List<WebSocket>();
                foreach (var client in _controlPanelClients)
                {
                    if (client.State == WebSocketState.Open)
                    {
                        _ = Task.Run(async () =>
                        {
                            if (_clientLocks.TryGetValue(client, out var semaphore))
                            {
                                await semaphore.WaitAsync();
                                try
                                {
                                    if (client.State == WebSocketState.Open)
                                    {
                                        await client.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
                                    }
                                }
                                catch { }
                                finally
                                {
                                    semaphore.Release();
                                }
                            }
                        });
                    }
                    else if (client.State == WebSocketState.Aborted || client.State == WebSocketState.Closed)
                    {
                        clientsToRemove.Add(client);
                    }
                }
                
                foreach (var client in clientsToRemove)
                {
                    _controlPanelClients.Remove(client);
                    _clientLocks.TryRemove(client, out _);
                }
            }
        }

        private void BroadcastChatHistory()
        {
            if (_selectedUserId == 0) return;
            
            var chatHistoryManager = _sessionManager.GetOrCreateChatHistoryManager(_selectedUserId);
            List<ChatMessage> chatHistory = chatHistoryManager.GetHistory();
            BroadcastMessageToClients(new WebSocketMessage { Type = "chat_history", Data = chatHistory });
        }

        private static string GenerateSecureKey()
        {
            var bytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return BitConverter.ToString(bytes).Replace("-", "").ToLower();
        }

        private bool ValidateControlPanelAccess(HttpListenerContext context)
        {
            var key = GetQueryParameter(context.Request.Url.Query, "key");
            return !string.IsNullOrEmpty(key) && key == _controlPanelKey;
        }

        private static string GetQueryParameter(string query, string name)
        {
            if (string.IsNullOrEmpty(query)) return null;
            if (query.StartsWith("?")) query = query.Substring(1);
            var param = query.Split('&').Select(p => p.Split('=')).FirstOrDefault(parts => parts.Length == 2 && parts[0].Equals(name, StringComparison.OrdinalIgnoreCase));
            if (param == null) return null;
            try { return Uri.UnescapeDataString(param[1]); } catch { return param[1]; }
        }

        private void ServeControlPanelHtml(HttpListenerContext context)
        {
            try
            {
                string path = Path.Combine(Environment.CurrentDirectory, "ControlPanel", "public", "index.html");
                if (File.Exists(path))
                {
                    byte[] buffer = File.ReadAllBytes(path);
                    context.Response.ContentType = "text/html";
                    context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                }
            }
            finally { context.Response.Close(); }
        }

        private void ServeStaticFile(HttpListenerContext context)
        {
            try
            {
                string rootDir = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "ControlPanel", "public"));
                if (!rootDir.EndsWith(Path.DirectorySeparatorChar.ToString()))
                    rootDir += Path.DirectorySeparatorChar;

                string rawPath = Uri.UnescapeDataString(context.Request.Url.AbsolutePath);
                string safeRequestPath = rawPath.TrimStart('/');
                string fullPath = Path.GetFullPath(Path.Combine(rootDir, safeRequestPath));
                bool isValidPath = fullPath.StartsWith(rootDir, StringComparison.OrdinalIgnoreCase);
                bool fileExists = File.Exists(fullPath);

                if (!isValidPath || !fileExists)
                {
                    if (safeRequestPath.Equals("unauthorized.html", StringComparison.OrdinalIgnoreCase))
                    {
                        context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                        return;
                    }
                    context.Response.Redirect("/unauthorized.html");
                    return;
                }

                byte[] buffer = File.ReadAllBytes(fullPath);
                context.Response.ContentType = GetContentType(Path.GetExtension(fullPath));
                context.Response.ContentLength64 = buffer.Length;
                context.Response.StatusCode = (int)HttpStatusCode.OK;
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            }
            catch
            {
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            }
            finally
            {
                try { context.Response.Close(); } catch { }
            }
        }

        private static string GetContentType(string ext)
        {
            ext = ext.ToLower();
            return ext == ".css" ? "text/css" :
                   ext == ".js" ? "application/javascript" :
                   ext == ".ico" ? "image/x-icon" : "application/octet-stream";
        }

        private void ServeHealthCheck(HttpListenerContext context)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { status = "ok" }));
            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            context.Response.Close();
        }

        private void ServeConfig(HttpListenerContext context)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(_configManager.Config));
            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            context.Response.Close();
        }

        private async Task UpdateConfigAsync(HttpListenerContext context)
        {
            using (var r = new StreamReader(context.Request.InputStream))
            {
                string json = await r.ReadToEndAsync();
                UpdateConfig(JsonConvert.DeserializeObject<dynamic>(json));
            }
            context.Response.Close();
        }

        private void ServeLogs(HttpListenerContext context)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(Logger.GetLogs()));
            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            context.Response.Close();
        }

        private void ClearLogs(HttpListenerContext context)
        {
            Logger.ClearLogs();
            context.Response.Close();
        }

        private void ServeEulaStatus(HttpListenerContext context)
        {
            try
            {
                var response = new
                {
                    eulaAccepted = _configManager.Config.EulaAccepted,
                    isFirstRun = _configManager.Config.IsFirstRun
                };
                byte[] buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(response));
                context.Response.ContentType = "application/json";
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            }
            catch (Exception ex)
            {
                Logger.LogError("CONTROL_PANEL", $"Error serving EULA status: {ex.Message}");
                context.Response.StatusCode = 500;
                byte[] buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { error = "Internal server error" }));
                context.Response.ContentType = "application/json";
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            }
            finally
            {
                context.Response.Close();
            }
        }

        private async Task AcceptEulaAsync(HttpListenerContext context)
        {
            try
            {
                Logger.LogInfo("EULA", "Accepting EULA via API...");

                // 使用ConfigManager的AcceptEula方法
                _configManager.AcceptEula();

                // 广播配置给所有连接的客户端
                BroadcastMessageToClients(new WebSocketMessage { Type = "config_updated", Data = _configManager.Config });
                Logger.LogInfo("EULA", "Config update broadcasted to all clients");

                byte[] buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { success = true, message = "EULA accepted successfully" }));
                context.Response.ContentType = "application/json";
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                Logger.LogInfo("EULA", "User accepted EULA via API");
            }
            catch (Exception ex)
            {
                Logger.LogError("CONTROL_PANEL", $"Error accepting EULA: {ex.Message}");
                Logger.LogError("CONTROL_PANEL", $"Stack trace: {ex.StackTrace}");
                context.Response.StatusCode = 500;
                byte[] buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { error = "Internal server error", details = ex.Message }));
                context.Response.ContentType = "application/json";
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            }
            finally
            {
                context.Response.Close();
            }
        }

        private void RedirectToUnauthorized(HttpListenerContext context)
        {
            try { context.Response.Redirect("/unauthorized.html"); }
            finally { context.Response.Close(); }
        }

        private void ServeUnauthorizedHtml(HttpListenerContext context)
        {
            try
            {
                string path = Path.Combine(Environment.CurrentDirectory, "ControlPanel", "public", "unauthorized.html");
                if (File.Exists(path))
                {
                    byte[] buffer = File.ReadAllBytes(path);
                    context.Response.ContentType = "text/html";
                    context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                }
            }
            finally { context.Response.Close(); }
        }

        private async Task HandleUnauthorizedWebSocketRequestAsync(HttpListenerContext context)
        {
            try
            {
                var wsContext = await context.AcceptWebSocketAsync(null);
                var webSocket = wsContext.WebSocket;

                string unauthorizedHtml = "";
                string path = Path.Combine(Environment.CurrentDirectory, "ControlPanel", "public", "unauthorized.html");
                if (File.Exists(path))
                    unauthorizedHtml = File.ReadAllText(path);

                var errorResponse = new ErrorResponse
                {
                    Code = ErrorCodes.INVALID_ACCESS_KEY,
                    Message = "Authentication failed, please use the correct access key",
                    Html = unauthorizedHtml
                };

                var errorMessage = new WebSocketMessage
                {
                    Type = "auth_error",
                    Data = errorResponse
                };

                byte[] buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(errorMessage));
                await webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Authentication failed", CancellationToken.None);
            }
            catch { context.Response.Close(); }
        }

        private async Task ServeProxyAsync(HttpListenerContext context)
        {
            try
            {
                string query = context.Request.Url.Query;
                string action = GetQueryParameter(query, "action");

                switch (action)
                {
                    case "role-cards":
                        await ServeRoleCardsAsync(context);
                        break;
                    case "role-card-details":
                        await ServeRoleCardDetailsAsync(context);
                        break;
                    case "proxy-image":
                        await ServeProxyImageAsync(context);
                        break;
                    case "get_meme":
                        await ServeMemeAsync(context);
                        break;
                    case "plugin-market":
                        await ServePluginMarketAsync(context);
                        break;
                    case "plugin-market-details":
                        await ServePluginMarketDetailsAsync(context);
                        break;
                    case "download-plugin":
                        await ServeDownloadPluginAsync(context);
                        break;
                    default:
                        context.Response.StatusCode = 400;
                        byte[] buffer = Encoding.UTF8.GetBytes("Missing or invalid action parameter");
                        context.Response.ContentType = "text/plain";
                        context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                        context.Response.Close();
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("PROXY", "Error handling proxy request: " + ex.Message);
                context.Response.StatusCode = 500;
                byte[] buffer = Encoding.UTF8.GetBytes("Internal server error");
                context.Response.ContentType = "text/plain";
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                context.Response.Close();
            }
        }

        private async Task ServeRoleCardsAsync(HttpListenerContext context)
        {
            try
            {
                string url = _configManager.Config.RoleCardsApiUrl;
                using (var client = new System.Net.Http.HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    var response = await client.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        string content = await response.Content.ReadAsStringAsync();
                        byte[] buffer = Encoding.UTF8.GetBytes(content);
                        context.Response.ContentType = "application/json";
                        context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                    }
                    else
                    {
                        context.Response.StatusCode = 500;
                        string error = JsonConvert.SerializeObject(new { error = "Failed to fetch role cards" });
                        byte[] buffer = Encoding.UTF8.GetBytes(error);
                        context.Response.ContentType = "application/json";
                        context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("ROLE_CARDS", "Error fetching role cards: " + ex.Message);
                context.Response.StatusCode = 500;
                string error = JsonConvert.SerializeObject(new { error = "Internal server error" });
                byte[] buffer = Encoding.UTF8.GetBytes(error);
                context.Response.ContentType = "application/json";
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            }
            finally { context.Response.Close(); }
        }

        private async Task ServeRoleCardDetailsAsync(HttpListenerContext context)
        {
            try
            {
                string query = context.Request.Url.Query;
                string link = GetQueryParameter(query, "link");
                if (string.IsNullOrEmpty(link))
                {
                    context.Response.StatusCode = 400;
                    string error = JsonConvert.SerializeObject(new { error = "Missing link parameter" });
                    byte[] buffer = Encoding.UTF8.GetBytes(error);
                    context.Response.ContentType = "application/json";
                    context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                    context.Response.Close();
                    return;
                }

                using (var client = new System.Net.Http.HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    var response = await client.GetAsync(link);
                    if (response.IsSuccessStatusCode)
                    {
                        string content = await response.Content.ReadAsStringAsync();
                        byte[] buffer = Encoding.UTF8.GetBytes(content);
                        context.Response.ContentType = "application/json";
                        context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                    }
                    else
                    {
                        context.Response.StatusCode = 500;
                        string error = JsonConvert.SerializeObject(new { error = "Failed to fetch role card details" });
                        byte[] buffer = Encoding.UTF8.GetBytes(error);
                        context.Response.ContentType = "application/json";
                        context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("ROLE_CARDS", "Error fetching role card details: " + ex.Message);
                context.Response.StatusCode = 500;
                string error = JsonConvert.SerializeObject(new { error = "Internal server error" });
                byte[] buffer = Encoding.UTF8.GetBytes(error);
                context.Response.ContentType = "application/json";
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            }
            finally { context.Response.Close(); }
        }

        private async Task ServeProxyImageAsync(HttpListenerContext context)
        {
            try
            {
                string query = context.Request.Url.Query;
                string url = GetQueryParameter(query, "url");
                if (string.IsNullOrEmpty(url))
                {
                    context.Response.StatusCode = 400;
                    byte[] buffer = Encoding.UTF8.GetBytes("Missing url parameter");
                    context.Response.ContentType = "text/plain";
                    context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                    context.Response.Close();
                    return;
                }

                using (var client = new System.Net.Http.HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    var response = await client.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        byte[] imageData = await response.Content.ReadAsByteArrayAsync();
                        context.Response.ContentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
                        context.Response.OutputStream.Write(imageData, 0, imageData.Length);
                    }
                    else
                    {
                        context.Response.StatusCode = 500;
                        byte[] buffer = Encoding.UTF8.GetBytes("Failed to fetch image");
                        context.Response.ContentType = "text/plain";
                        context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("PROXY", "Error proxying image: " + ex.Message);
                context.Response.StatusCode = 500;
                byte[] buffer = Encoding.UTF8.GetBytes("Internal server error");
                context.Response.ContentType = "text/plain";
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            }
            finally { context.Response.Close(); }
        }

        private async Task ServeMemeAsync(HttpListenerContext context)
        {
            try
            {
                string query = context.Request.Url.Query;
                string memeName = GetQueryParameter(query, "name");
                if (string.IsNullOrEmpty(memeName))
                {
                    context.Response.StatusCode = 400;
                    byte[] buffer = Encoding.UTF8.GetBytes("Missing name parameter");
                    context.Response.ContentType = "text/plain";
                    context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                    context.Response.Close();
                    return;
                }

                string memePath = Path.Combine(Environment.CurrentDirectory, "meme", memeName);
                if (!File.Exists(memePath))
                {
                    context.Response.StatusCode = 404;
                    byte[] buffer = Encoding.UTF8.GetBytes("Meme not found");
                    context.Response.ContentType = "text/plain";
                    context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                    context.Response.Close();
                    return;
                }

                byte[] memeData = File.ReadAllBytes(memePath);
                context.Response.ContentType = "image/jpeg";
                context.Response.OutputStream.Write(memeData, 0, memeData.Length);
            }
            catch (Exception ex)
            {
                Logger.LogError("PROXY", "Error serving meme: " + ex.Message);
                context.Response.StatusCode = 500;
                byte[] buffer = Encoding.UTF8.GetBytes("Internal server error");
                context.Response.ContentType = "text/plain";
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            }
            finally { context.Response.Close(); }
        }

        private async Task ServePluginMarketAsync(HttpListenerContext context)
        {
            try
            {
                string url = "https://gitee.com/bingchuankeji/plugin/raw/master/list.json";
                using (var client = new System.Net.Http.HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    var response = await client.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        string content = await response.Content.ReadAsStringAsync();
                        byte[] buffer = Encoding.UTF8.GetBytes(content);
                        context.Response.ContentType = "application/json";
                        context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                    }
                    else
                    {
                        context.Response.StatusCode = 500;
                        string error = JsonConvert.SerializeObject(new { error = "Failed to fetch plugin market list" });
                        byte[] buffer = Encoding.UTF8.GetBytes(error);
                        context.Response.ContentType = "application/json";
                        context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("PLUGIN_MARKET", "Error fetching plugin market list: " + ex.Message);
                context.Response.StatusCode = 500;
                string error = JsonConvert.SerializeObject(new { error = "Internal server error" });
                byte[] buffer = Encoding.UTF8.GetBytes(error);
                context.Response.ContentType = "application/json";
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            }
            finally { context.Response.Close(); }
        }

        private async Task ServePluginMarketDetailsAsync(HttpListenerContext context)
        {
            try
            {
                string query = context.Request.Url.Query;
                string link = GetQueryParameter(query, "link");
                if (string.IsNullOrEmpty(link))
                {
                    context.Response.StatusCode = 400;
                    string error = JsonConvert.SerializeObject(new { error = "Missing link parameter" });
                    byte[] buffer = Encoding.UTF8.GetBytes(error);
                    context.Response.ContentType = "application/json";
                    context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                    context.Response.Close();
                    return;
                }

                using (var client = new System.Net.Http.HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    var response = await client.GetAsync(link);
                    if (response.IsSuccessStatusCode)
                    {
                        string content = await response.Content.ReadAsStringAsync();
                        byte[] buffer = Encoding.UTF8.GetBytes(content);
                        context.Response.ContentType = "application/json";
                        context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                    }
                    else
                    {
                        context.Response.StatusCode = 500;
                        string error = JsonConvert.SerializeObject(new { error = "Failed to fetch plugin details" });
                        byte[] buffer = Encoding.UTF8.GetBytes(error);
                        context.Response.ContentType = "application/json";
                        context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("PLUGIN_MARKET", "Error fetching plugin details: " + ex.Message);
                context.Response.StatusCode = 500;
                string error = JsonConvert.SerializeObject(new { error = "Internal server error" });
                byte[] buffer = Encoding.UTF8.GetBytes(error);
                context.Response.ContentType = "application/json";
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            }
            finally { context.Response.Close(); }
        }

        private async Task ServeDownloadPluginAsync(HttpListenerContext context)
        {
            try
            {
                string query = context.Request.Url.Query;
                string url = GetQueryParameter(query, "url");
                string fileName = GetQueryParameter(query, "fileName");
                string pluginName = GetQueryParameter(query, "pluginName");

                if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(fileName))
                {
                    context.Response.StatusCode = 400;
                    string error = JsonConvert.SerializeObject(new { error = "Missing url or fileName parameter" });
                    byte[] buffer = Encoding.UTF8.GetBytes(error);
                    context.Response.ContentType = "application/json";
                    context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                    context.Response.Close();
                    return;
                }

                fileName = Path.GetFileName(fileName);
                if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    fileName += ".dll";
                }

                string pluginPath = Path.Combine(_pluginManager.PluginDirectory, fileName);

                if (File.Exists(pluginPath))
                {
                    context.Response.StatusCode = 409;
                    string error = JsonConvert.SerializeObject(new { error = "Plugin file already exists" });
                    byte[] buffer = Encoding.UTF8.GetBytes(error);
                    context.Response.ContentType = "application/json";
                    context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                    context.Response.Close();
                    return;
                }

                using (var client = new System.Net.Http.HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(5);
                    
                    var headResponse = await client.SendAsync(new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Head, url));
                    long? totalBytes = headResponse.Content.Headers.ContentLength;

                    var response = await client.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                    if (!response.IsSuccessStatusCode)
                    {
                        context.Response.StatusCode = 500;
                        string error = JsonConvert.SerializeObject(new { error = "Failed to download plugin" });
                        byte[] buffer = Encoding.UTF8.GetBytes(error);
                        context.Response.ContentType = "application/json";
                        context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                        context.Response.Close();
                        return;
                    }

                    BroadcastMessageToClients(new WebSocketMessage 
                    { 
                        Type = "plugin_download_start", 
                        Data = new { pluginName = pluginName, fileName = fileName, totalBytes = totalBytes }
                    });

                    using (var stream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(pluginPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        byte[] buffer = new byte[8192];
                        long downloadedBytes = 0;
                        int bytesRead;
                        DateTime lastProgressUpdate = DateTime.Now;

                        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            downloadedBytes += bytesRead;

                            if ((DateTime.Now - lastProgressUpdate).TotalMilliseconds >= 100)
                            {
                                int progress = totalBytes.HasValue ? (int)((downloadedBytes * 100) / totalBytes.Value) : 0;
                                BroadcastMessageToClients(new WebSocketMessage 
                                { 
                                    Type = "plugin_download_progress", 
                                    Data = new { 
                                        pluginName = pluginName, 
                                        fileName = fileName, 
                                        downloadedBytes = downloadedBytes,
                                        totalBytes = totalBytes,
                                        progress = progress
                                    }
                                });
                                lastProgressUpdate = DateTime.Now;
                            }
                        }
                    }

                    BroadcastMessageToClients(new WebSocketMessage 
                    { 
                        Type = "plugin_download_complete", 
                        Data = new { pluginName = pluginName, fileName = fileName, path = pluginPath }
                    });

                    byte[] responseBuffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { 
                        success = true, 
                        message = $"Plugin {pluginName} downloaded successfully",
                        fileName = fileName,
                        path = pluginPath
                    }));
                    context.Response.ContentType = "application/json";
                    context.Response.OutputStream.Write(responseBuffer, 0, responseBuffer.Length);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("PLUGIN_MARKET", "Error downloading plugin: " + ex.Message);
                context.Response.StatusCode = 500;
                string error = JsonConvert.SerializeObject(new { error = "Internal server error: " + ex.Message });
                byte[] buffer = Encoding.UTF8.GetBytes(error);
                context.Response.ContentType = "application/json";
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);

                BroadcastMessageToClients(new WebSocketMessage 
                { 
                    Type = "plugin_download_error", 
                    Data = new { error = ex.Message }
                });
            }
            finally { context.Response.Close(); }
        }
    }
}
