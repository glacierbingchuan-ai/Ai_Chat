using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using AI_Chat.Managers;
using AI_Chat.Models;
using AI_Chat.Plugins;
using AI_Chat.Utils;
using Newtonsoft.Json;

namespace AI_Chat.Services.ControlPanel.Handlers
{
    public class WebSocketHandler
    {
        private readonly ConfigManager _configManager;
        private readonly UserSessionManager _sessionManager;
        private readonly UserConfigManager _userConfigManager;
        private readonly LLMService _llmService;
        private readonly CancellationTokenSource _globalCts;
        private readonly WebSocketManager _wsManager;
        private readonly PluginManager _pluginManager;
        private readonly PluginWebSocketHandler _pluginWebSocketHandler;
        private readonly SystemInfoHandler _systemInfoHandler;
        private readonly UserManagementHandler _userManagementHandler;
        private readonly UserConfigHandler _userConfigHandler;
        private readonly VectorDbHandler _vectorDbHandler;
        private readonly DateTime _startTime;
        private readonly long _selectedUserId;
        private readonly WebSocketClient _webSocketClient;
        private readonly VersionCheckService _versionCheckService;

        public WebSocketHandler(
            ConfigManager configManager,
            UserSessionManager sessionManager,
            UserConfigManager userConfigManager,
            LLMService llmService,
            CancellationTokenSource globalCts,
            WebSocketManager wsManager,
            PluginManager pluginManager,
            PluginWebSocketHandler pluginWebSocketHandler,
            SystemInfoHandler systemInfoHandler,
            UserManagementHandler userManagementHandler,
            UserConfigHandler userConfigHandler,
            VectorDbHandler vectorDbHandler,
            DateTime startTime,
            long selectedUserId,
            WebSocketClient webSocketClient,
            VersionCheckService versionCheckService)
        {
            _configManager = configManager;
            _sessionManager = sessionManager;
            _userConfigManager = userConfigManager;
            _llmService = llmService;
            _globalCts = globalCts;
            _wsManager = wsManager;
            _pluginManager = pluginManager;
            _pluginWebSocketHandler = pluginWebSocketHandler;
            _systemInfoHandler = systemInfoHandler;
            _userManagementHandler = userManagementHandler;
            _userConfigHandler = userConfigHandler;
            _vectorDbHandler = vectorDbHandler;
            _startTime = startTime;
            _selectedUserId = selectedUserId;
            _webSocketClient = webSocketClient;
            _versionCheckService = versionCheckService;
        }

        public async Task ProcessWebSocketMessageAsync(WebSocket webSocket, string json)
        {
            try
            {
                var message = JsonConvert.DeserializeObject<WebSocketMessage>(json);
                string messageId = message.Id;

                switch (message.Type)
                {
                    case "connect_protocol":
                        await HandleConnectProtocolAsync(webSocket, message.Data, messageId);
                        break;
                    case "disconnect_protocol":
                        await HandleDisconnectProtocolAsync(webSocket, messageId);
                        break;
                    case "start_scan":
                        await HandleStartScanAsync(webSocket, messageId);
                        break;
                    case "stop_scan":
                        await HandleStopScanAsync(webSocket, messageId);
                        break;
                    case "get_system_info":
                        await _systemInfoHandler.HandleGetSystemInfoAsync(webSocket, messageId, this);
                        break;
                    case "get_changelog":
                        await _systemInfoHandler.HandleGetChangelogAsync(webSocket, messageId);
                        break;
                    case "get_initial_data":
                        await SendInitialDataAsync(webSocket);
                        break;
                    case "get_logs":
                        await SendLogsAsync(webSocket, messageId);
                        break;
                    case "clear_logs":
                        Logger.ClearLogs();
                        BroadcastMessageToClients(new WebSocketMessage { Type = "logs_cleared", ReplyTo = messageId });
                        break;
                    case "clear_context":
                        await ClearSelectedUserContextAsync(messageId);
                        break;
                    case "clear_context_for_user":
                        await ClearContextForUserAsync(message.Data, messageId);
                        break;
                    case "config_update":
                        string errorMessage;
                        bool success = _configManager.UpdateConfig(message.Data, out errorMessage);
                        if (success)
                        {
                            _llmService.UpdateApiKey(_configManager.Config.LlmApiKey);
                            BroadcastMessageToClients(new WebSocketMessage { Type = "config_updated", Data = _configManager.Config, ReplyTo = messageId });
                        }
                        else
                        {
                            await SendResponseAsync(webSocket, "config_update_error", new { message = errorMessage }, messageId);
                        }
                        break;
                    case "get_llm_status":
                        _ = Task.Run(async () =>
                        {
                            string status = await _llmService.GetLlmStatusAsync();
                            await SendResponseAsync(webSocket, "llm_status", status, messageId);
                        });
                        break;
                    case "test_llm_connection":
                        _ = Task.Run(async () =>
                        {
                            await TestLlmConnectionAsync(webSocket, message.Data, messageId);
                        });
                        break;
                    case "get_runtime":
                        double uptime = (DateTime.Now - _startTime).TotalSeconds;
                        await SendResponseAsync(webSocket, "runtime", uptime, messageId);
                        break;
                    case "use_role_card":
                        await HandleUseRoleCardAsync(webSocket, message.Data, messageId);
                        break;
                    case "get_chat_history":
                        await BroadcastChatHistoryAsync(webSocket, message.Data, messageId);
                        break;
                    case "select_user":
                        await _userManagementHandler.HandleSelectUserAsync(webSocket, message.Data, messageId, this);
                        break;
                    case "get_users":
                        await _userManagementHandler.SendUsersListAsync(webSocket, messageId, this);
                        break;
                    case "add_allowed_user":
                        await _userManagementHandler.HandleAddAllowedUserAsync(message.Data, messageId, this);
                        break;
                    case "remove_allowed_user":
                        await _userManagementHandler.HandleRemoveAllowedUserAsync(message.Data, messageId, this);
                        break;
                    case "add_allowed_group":
                        await _userManagementHandler.HandleAddAllowedGroupAsync(message.Data, messageId, this);
                        break;
                    case "remove_allowed_group":
                        await _userManagementHandler.HandleRemoveAllowedGroupAsync(message.Data, messageId, this);
                        break;
                    case "get_user_config":
                        await _userConfigHandler.HandleGetUserConfigAsync(webSocket, message.Data, messageId, _selectedUserId, this);
                        break;
                    case "update_user_config":
                        await _userConfigHandler.HandleUpdateUserConfigAsync(message.Data, messageId, _selectedUserId, this);
                        break;
                    case "reset_user_config":
                        await _userConfigHandler.HandleResetUserConfigAsync(message.Data, messageId, _selectedUserId, this);
                        break;
                    case "confirm_version_exit":
                        await HandleVersionExitConfirmationAsync(messageId);
                        break;
                    case "reject_eula":
                        await HandleRejectEulaAsync(messageId);
                        break;
                    case "get_vector_entries":
                        await _vectorDbHandler.HandleGetVectorEntriesAsync(webSocket, message.Data, messageId, _selectedUserId, this);
                        break;
                    case "search_vectors":
                        await _vectorDbHandler.HandleSearchVectorsAsync(webSocket, message.Data, messageId, _selectedUserId, this);
                        break;
                    case "delete_vector_entry":
                        await _vectorDbHandler.HandleDeleteVectorEntryAsync(webSocket, message.Data, messageId, _selectedUserId, this);
                        break;
                    case "clear_vectors":
                        await _vectorDbHandler.HandleClearVectorsAsync(webSocket, message.Data, messageId, _selectedUserId, this);
                        break;
                    case "regenerate_vectors":
                        await _vectorDbHandler.HandleRegenerateVectorsAsync(webSocket, message.Data, messageId, _selectedUserId, this);
                        break;
                    case "save_vector_db_settings":
                        await _vectorDbHandler.HandleSaveVectorDbSettingsAsync(message.Data, messageId, this);
                        break;
                    case "check_local_embedding_status":
                        await HandleCheckLocalEmbeddingStatusAsync(webSocket, messageId);
                        break;
                    case "init_local_embedding_model":
                        await HandleInitLocalEmbeddingModelAsync(webSocket, messageId);
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
                            await _pluginWebSocketHandler.HandleMessageAsync(webSocket, message.Type, message.Data, messageId);
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

        private async Task HandleConnectProtocolAsync(WebSocket webSocket, dynamic data, string replyTo)
        {
            try
            {
                if (_webSocketClient == null)
                {
                    await SendResponseAsync(webSocket, "protocol_connection_error", new { message = "WebSocket client not initialized" }, replyTo);
                    return;
                }

                string serverUri = data?.serverUri?.ToString();
                string token = data?.token?.ToString() ?? "";
                int keepAliveInterval = 30000;

                if (data?.keepAliveInterval != null)
                {
                    int.TryParse(data.keepAliveInterval.ToString(), out keepAliveInterval);
                }

                if (string.IsNullOrEmpty(serverUri))
                {
                    await SendResponseAsync(webSocket, "protocol_connection_error", new { message = "Server URI is required" }, replyTo);
                    return;
                }

                var result = await _webSocketClient.ConnectAsync(serverUri, token, keepAliveInterval);

                if (result.success)
                {
                    await SendResponseAsync(webSocket, "protocol_connected", new { message = result.message }, replyTo);
                }
                else
                {
                    await SendResponseAsync(webSocket, "protocol_connection_error", new { message = result.message }, replyTo);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("ControlPanel", $"Error connecting to protocol: {ex.Message}");
                await SendResponseAsync(webSocket, "protocol_connection_error", new { message = ex.Message }, replyTo);
            }
        }

        private async Task HandleDisconnectProtocolAsync(WebSocket webSocket, string replyTo)
        {
            try
            {
                if (_webSocketClient != null)
                {
                    await _webSocketClient.DisconnectAsync();
                }
                await SendResponseAsync(webSocket, "protocol_disconnected", new { message = "Disconnected successfully" }, replyTo);
            }
            catch (Exception ex)
            {
                Logger.LogError("ControlPanel", $"Error disconnecting protocol: {ex.Message}");
                await SendResponseAsync(webSocket, "protocol_disconnected", new { message = ex.Message }, replyTo);
            }
        }

        private async Task HandleStartScanAsync(WebSocket webSocket, string replyTo)
        {
            try
            {
                if (_webSocketClient == null)
                {
                    await SendResponseAsync(webSocket, "scan_error", new { message = "WebSocket client not initialized" }, replyTo);
                    return;
                }

                await _webSocketClient.StartScanAsync();
                await SendResponseAsync(webSocket, "scan_started", new { message = "Scan started" }, replyTo);
            }
            catch (Exception ex)
            {
                Logger.LogError("ControlPanel", $"Error starting scan: {ex.Message}");
                await SendResponseAsync(webSocket, "scan_error", new { message = ex.Message }, replyTo);
            }
        }

        private async Task HandleStopScanAsync(WebSocket webSocket, string replyTo)
        {
            try
            {
                if (_webSocketClient != null)
                {
                    _webSocketClient.StopScan();
                }
                await SendResponseAsync(webSocket, "scan_stopped", new { message = "Scan stopped" }, replyTo);
            }
            catch (Exception ex)
            {
                Logger.LogError("ControlPanel", $"Error stopping scan: {ex.Message}");
                await SendResponseAsync(webSocket, "scan_error", new { message = ex.Message }, replyTo);
            }
        }

        public async Task SendResponseAsync(WebSocket webSocket, string type, dynamic data, string replyTo)
        {
            var response = new WebSocketMessage
            {
                Type = type,
                Data = data,
                ReplyTo = replyTo
            };

            if (webSocket == null)
            {
                BroadcastMessageToClients(response);
                return;
            }

            await _wsManager.SendServerMessageAsync(webSocket, JsonConvert.SerializeObject(response));
        }

        public void BroadcastMessageToClients(WebSocketMessage message)
        {
            _wsManager.BroadcastToServerClients(message);
        }

        private async Task SendLogsAsync(WebSocket webSocket, string replyTo = null)
        {
            var message = new WebSocketMessage { Type = "logs", Data = Logger.GetLogs(), ReplyTo = replyTo };
            await _wsManager.SendServerMessageAsync(webSocket, JsonConvert.SerializeObject(message));
        }

        private async Task TestLlmConnectionAsync(WebSocket webSocket, dynamic testConfig, string replyTo = null)
        {
            try
            {
                string modelName = testConfig?.llmModelName?.ToString();
                string apiBaseUrl = testConfig?.llmApiBaseUrl?.ToString();
                string apiKey = testConfig?.llmApiKey?.ToString();
                var result = await _llmService.CheckLlmApiStatusAsync(modelName, apiBaseUrl, apiKey);
                string message = (string)result["message"];
                await SendResponseAsync(webSocket, "llm_test_result", message, replyTo);
            }
            catch (Exception ex) { Logger.LogError("CONTROL_PANEL", "Error testing LLM connection", ex); }
        }

        private async Task HandleUseRoleCardAsync(WebSocket webSocket, dynamic data, string replyTo = null)
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
                    await SendResponseAsync(webSocket, "role_card_error", "No user selected", replyTo);
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

                await SendResponseAsync(webSocket, "role_card_used", "Role card applied successfully", replyTo);

                var contextManager = _sessionManager.GetOrCreateContextManager(targetUserId);
                var chatHistoryManager = _sessionManager.GetOrCreateChatHistoryManager(targetUserId);
                contextManager.ClearContext();
                chatHistoryManager.ClearHistory();

                BroadcastMessageToClients(new WebSocketMessage { Type = "context_cleared", ReplyTo = replyTo });

                if (targetUserId == _selectedUserId)
                {
                    BroadcastMessageToClients(new WebSocketMessage { Type = "scheduled_events_updated", Data = contextManager.ScheduledEvents, ReplyTo = replyTo });
                    var updatedUserConfig = _userConfigManager.GetUserConfig(targetUserId);
                    BroadcastMessageToClients(new WebSocketMessage { Type = "user_config_updated", Data = updatedUserConfig, ReplyTo = replyTo });
                }

                Logger.LogInfo("ROLE_CARDS", "Role card used successfully for user " + targetUserId);
            }
            catch (Exception ex)
            {
                Logger.LogError("ROLE_CARDS", "Error handling use_role_card: " + ex.Message);
                await SendResponseAsync(webSocket, "role_card_error", "Failed to apply role card: " + ex.Message, replyTo);
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

        private async Task BroadcastChatHistoryAsync(WebSocket webSocket, dynamic data, string replyTo = null)
        {
            if (_selectedUserId == 0)
            {
                await SendResponseAsync(webSocket, "chat_history", new { messages = new List<ChatMessage>(), hasMore = false }, replyTo);
                return;
            }

            var chatHistoryManager = _sessionManager.GetOrCreateChatHistoryManager(_selectedUserId);

            string beforeId = data?.beforeId?.ToString();
            DateTime? beforeTime = null;
            if (data?.beforeTime != null)
            {
                if (DateTime.TryParse(data.beforeTime.ToString(), out DateTime parsedTime))
                {
                    beforeTime = parsedTime;
                }
            }
            int limit = 20;
            if (data?.limit != null)
            {
                int.TryParse(data.limit.ToString(), out limit);
                limit = Math.Clamp(limit, 1, 100);
            }

            var (messages, hasMore) = chatHistoryManager.GetHistoryPaged(beforeId, beforeTime, limit);

            var response = new
            {
                messages = messages,
                hasMore = hasMore,
                totalCount = chatHistoryManager.GetMessageCount()
            };

            await SendResponseAsync(webSocket, "chat_history", response, replyTo);
        }

        public async Task SendInitialDataAsync(WebSocket webSocket)
        {
            try
            {
                Logger.LogInfo("INIT", "Sending initial data to client");
                
                // 等待初始连接尝试完成后再发送初始数据（最多等待 5 秒）
                // 这样可以确保 protocolStatus 中的状态是准确的
                int waitForConnectionAttempts = 0;
                while (!(_webSocketClient?.InitialConnectionAttempted ?? false) && waitForConnectionAttempts < 50)
                {
                    await Task.Delay(100);
                    waitForConnectionAttempts++;
                }
                
                Logger.LogInfo("INIT", $"Waited {waitForConnectionAttempts * 100}ms for initial connection attempt to complete");
                
                long selectedUserId = _selectedUserId;
                if (selectedUserId == 0 && _configManager.Config.AllowedUserIds.Count > 0)
                {
                    selectedUserId = _configManager.Config.AllowedUserIds[0];
                }

                var contextManager = selectedUserId > 0 ? _sessionManager.GetOrCreateContextManager(selectedUserId) : null;
                var session = selectedUserId > 0 ? _sessionManager.GetSession(selectedUserId) : null;
                var userConfig = selectedUserId > 0 ? _userConfigManager.GetOrCreateUserConfig(selectedUserId) : null;

                var protocolStatus = new
                {
                    isConnected = _webSocketClient?.IsConnected ?? false,
                    serverUri = _configManager.Config.WebsocketServerUri,
                    isScanning = _webSocketClient?.IsScanning ?? false,
                    initialConnectionAttempted = _webSocketClient?.InitialConnectionAttempted ?? false
                };

                var localEmbeddingStatus = new
                {
                    modelExists = LocalEmbeddingService.IsModelExists(),
                    modelPath = LocalEmbeddingService.GetDefaultModelPath()
                };

                var initialData = new
                {
                    logs = Logger.GetLogs(),
                    config = _configManager.Config,
                    userConfig = userConfig,
                    uptime = (DateTime.Now - _startTime).TotalSeconds,
                    scheduledEvents = contextManager?.ScheduledEvents ?? new List<EventModel>(),
                    stats = _sessionManager.GetAllSessionStats(),
                    selectedUserId = selectedUserId,
                    activeUsers = _sessionManager.GetActiveUserIds(),
                    protocolStatus = protocolStatus,
                    localEmbeddingStatus = localEmbeddingStatus
                };
                await _wsManager.SendServerMessageAsync(webSocket, JsonConvert.SerializeObject(new WebSocketMessage { Type = "init", Data = initialData }));

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

                    await _wsManager.SendServerMessageAsync(webSocket, JsonConvert.SerializeObject(new WebSocketMessage
                    {
                        Type = "version_check_result",
                        Data = versionData
                    }));
                }
            }
            catch { }
        }

        private async Task ClearSelectedUserContextAsync(string replyTo = null)
        {
            if (_selectedUserId == 0) return;

            var contextManager = _sessionManager.GetOrCreateContextManager(_selectedUserId);
            var chatHistoryManager = _sessionManager.GetOrCreateChatHistoryManager(_selectedUserId);

            contextManager.ClearContext();
            chatHistoryManager.ClearHistory();

            BroadcastMessageToClients(new WebSocketMessage { Type = "context_cleared", ReplyTo = replyTo });
            BroadcastMessageToClients(new WebSocketMessage { Type = "scheduled_events_updated", Data = contextManager.ScheduledEvents, ReplyTo = replyTo });
        }

        private async Task ClearContextForUserAsync(dynamic data, string replyTo = null)
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

                var vectorContextManager = _sessionManager.GetVectorContextManager(userId);
                if (vectorContextManager != null)
                {
                    vectorContextManager.ClearVectors();
                }

                chatHistoryManager.ClearHistory();

                BroadcastMessageToClients(new WebSocketMessage { Type = "context_cleared", Data = new { userId = userId }, ReplyTo = replyTo });
                BroadcastMessageToClients(new WebSocketMessage { Type = "scheduled_events_updated", Data = contextManager.ScheduledEvents, ReplyTo = replyTo });
                BroadcastMessageToClients(new WebSocketMessage { Type = "vector_entries_updated", ReplyTo = replyTo });

                Logger.LogInfo("ControlPanel", $"已清空用户 {userId} 的上下文和向量数据库");
            }
            catch (Exception ex)
            {
                Logger.LogError("ControlPanel", $"清空用户上下文失败: {ex.Message}");
            }
        }

        private async Task HandleVersionExitConfirmationAsync(string replyTo = null)
        {
            Logger.LogInfo("VERSION_CHECK", "用户确认退出应用程序");
            await SendResponseAsync(null, "version_exit_confirmed", new { message = "应用程序即将退出" }, replyTo);
            _ = Task.Run(async () =>
            {
                await Task.Delay(500);
                Environment.Exit(0);
            });
        }

        private async Task HandleRejectEulaAsync(string replyTo = null)
        {
            Logger.LogInfo("EULA", "用户在控制面板拒绝使用协议，应用程序即将退出");
            await SendResponseAsync(null, "eula_rejected", new { message = "应用程序即将退出" }, replyTo);
            _ = Task.Run(() =>
            {
                try
                {
                    System.Threading.Thread.Sleep(500);
                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    Logger.LogError("EULA", $"退出程序时出错: {ex.Message}");
                    System.Diagnostics.Process.GetCurrentProcess().Kill();
                }
            });
        }

        private async Task HandleCheckLocalEmbeddingStatusAsync(WebSocket webSocket, string replyTo = null)
        {
            try
            {
                LocalEmbeddingService.EnsureModelsDirectoryExists();
                bool exists = LocalEmbeddingService.IsModelExists();
                await SendResponseAsync(webSocket, "local_embedding_status", new { exists = exists }, replyTo);
            }
            catch (Exception ex)
            {
                Logger.LogError("ControlPanel", $"检查本地 Embedding 状态失败: {ex.Message}");
                await SendResponseAsync(webSocket, "local_embedding_status", new { exists = false, error = ex.Message }, replyTo);
            }
        }

        private async Task HandleInitLocalEmbeddingModelAsync(WebSocket webSocket, string replyTo = null)
        {
            try
            {
                if (LocalEmbeddingService.IsModelExists())
                {
                    await SendResponseAsync(webSocket, "local_embedding_init", new { success = true, message = "Model already exists" }, replyTo);
                    return;
                }

                await SendResponseAsync(webSocket, "local_embedding_init", new { success = true, message = "Download started" }, replyTo);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var progressLock = new SemaphoreSlim(1, 1);
                        var progress = new Progress<DownloadProgress>(async p =>
                        {
                            await progressLock.WaitAsync();
                            try
                            {
                                await SendResponseAsync(null, "local_embedding_download_progress", new
                                {
                                    status = p.Status.ToString().ToLower(),
                                    progress = p.Progress,
                                    downloadedMB = Math.Round(p.DownloadedMB, 2),
                                    totalMB = Math.Round(p.TotalMB, 2),
                                    message = p.Message
                                }, replyTo);
                            }
                            finally
                            {
                                progressLock.Release();
                            }
                        });

                        bool success = await LocalEmbeddingService.DownloadModelAsync(progress: progress);

                        if (success)
                        {
                            await SendResponseAsync(null, "local_embedding_download_progress", new
                            {
                                status = "completed",
                                progress = 100,
                                message = "Download completed"
                            }, replyTo);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError("ControlPanel", $"下载本地 Embedding 模型失败: {ex.Message}");
                        await SendResponseAsync(null, "local_embedding_download_progress", new
                        {
                            status = "error",
                            message = ex.Message
                        }, replyTo);
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.LogError("ControlPanel", $"初始化本地 Embedding 模型失败: {ex.Message}");
                await SendResponseAsync(webSocket, "local_embedding_init", new { success = false, message = ex.Message }, replyTo);
            }
        }
    }
}
