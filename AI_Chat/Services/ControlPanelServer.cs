using System;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AI_Chat.Constants;
using AI_Chat.Managers;
using AI_Chat.Models;
using AI_Chat.Plugins;
using AI_Chat.Utils;
using AI_Chat.Services.ControlPanel;
using AI_Chat.Services.ControlPanel.Handlers;
using Newtonsoft.Json;

namespace AI_Chat.Services
{
    public class ControlPanelServer
    {
        private readonly ConfigManager _configManager;
        private readonly UserSessionManager _sessionManager;
        private readonly UserConfigManager _userConfigManager;
        private readonly LLMService _llmService;
        private readonly CancellationTokenSource _globalCts;
        private readonly PluginManager _pluginManager;
        private readonly WebSocketManager _wsManager;
        private readonly PluginWebSocketHandler _pluginWebSocketHandler;
        private readonly RequestRateLimiter _requestRateLimiter;

        private WebSocketClient _webSocketClient;
        private HttpListener _httpListener;
        private VersionCheckService _versionCheckService;

        private readonly string _controlPanelKey;
        private readonly DateTime _startTime;
        private long _selectedUserId;

        // Handlers - initialized lazily
        private WebSocketHandler _webSocketHandler;
        private SystemInfoHandler _systemInfoHandler;
        private readonly UserManagementHandler _userManagementHandler;
        private readonly UserConfigHandler _userConfigHandler;
        private readonly VectorDbHandler _vectorDbHandler;
        private readonly ProxyHandler _proxyHandler;

        public string ControlPanelKey => _controlPanelKey;
        public string ControlPanelUrl => $"http://localhost:{AppConstants.CONTROL_PANEL_PORT}?key={_controlPanelKey}";
        public string ControlPanelExternalUrl => $"http://<宿主机IP>:{AppConstants.CONTROL_PANEL_PORT}?key={_controlPanelKey}";

        public ControlPanelServer(
            ConfigManager configManager,
            UserSessionManager sessionManager,
            UserConfigManager userConfigManager,
            LLMService llmService,
            MessageHandler messageHandler,
            CancellationTokenSource globalCts,
            PluginManager pluginManager = null,
            RequestRateLimiter requestRateLimiter = null,
            WebSocketManager wsManager = null)
        {
            _configManager = configManager;
            _sessionManager = sessionManager;
            _userConfigManager = userConfigManager;
            _llmService = llmService;
            _globalCts = globalCts;
            _pluginManager = pluginManager;
            _wsManager = wsManager ?? new WebSocketManager();
            _pluginWebSocketHandler = pluginManager != null ? new PluginWebSocketHandler(pluginManager, _wsManager) : null;
            _requestRateLimiter = requestRateLimiter;

            _controlPanelKey = ControlPanelHelpers.GenerateSecureKey();
            _startTime = DateTime.Now;
            _selectedUserId = 0;

            if (_configManager.Config.AllowedUserIds.Count > 0)
            {
                _selectedUserId = _configManager.Config.AllowedUserIds[0];
            }

            if (_requestRateLimiter != null)
            {
                _requestRateLimiter.OnQueueCountChanged += BroadcastQueueStatus;
            }

            // Initialize handlers that don't depend on WebSocketClient
            _userManagementHandler = new UserManagementHandler(_configManager, _sessionManager, _userConfigManager);
            _userConfigHandler = new UserConfigHandler(_userConfigManager);
            _vectorDbHandler = new VectorDbHandler(_sessionManager, _configManager);
            _proxyHandler = new ProxyHandler(_configManager, _pluginManager, _wsManager);
        }

        public void SetWebSocketClient(WebSocketClient webSocketClient)
        {
            _webSocketClient = webSocketClient;

            if (_webSocketClient != null)
            {
                _webSocketClient.OnConnectionStateChanged += OnProtocolConnectionStateChanged;
                _webSocketClient.OnServiceFound += OnServiceFound;
                _webSocketClient.OnScanStateChanged += OnScanStateChanged;
                _webSocketClient.OnInitialConnectionAttemptCompleted += OnInitialConnectionAttemptCompleted;
            }

            // Initialize handlers that depend on WebSocketClient
            InitializeHandlers();
        }

        private void InitializeHandlers()
        {
            _systemInfoHandler = new SystemInfoHandler(_configManager, _pluginManager, _webSocketClient, _wsManager, _startTime);
            _webSocketHandler = new WebSocketHandler(
                _configManager,
                _sessionManager,
                _userConfigManager,
                _llmService,
                _globalCts,
                _wsManager,
                _pluginManager,
                _pluginWebSocketHandler,
                _systemInfoHandler,
                _userManagementHandler,
                _userConfigHandler,
                _vectorDbHandler,
                _startTime,
                _selectedUserId,
                _webSocketClient,
                _versionCheckService
            );
        }

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
                    () => BroadcastChatHistoryInternal()
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

                if (!PlatformHelper.IsRunningInDocker())
                {
                    if (PlatformHelper.ShowControlPanelPrompt())
                        PlatformHelper.OpenBrowser(ControlPanelUrl);
                }

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
                            await _proxyHandler.ServeProxyAsync(context);
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

                _wsManager.RegisterServerClient(webSocket);

                BroadcastMessageToClients(new WebSocketMessage { Type = "client_count_updated", Data = _wsManager.GetServerClientCount() });

                // 如果初始连接尝试已经完成，立即向新连接的客户端发送协议状态
                // 这样即使客户端在初始连接完成后才连接，也能收到状态并请求初始数据
                if (_webSocketClient != null && _webSocketClient.InitialConnectionAttempted)
                {
                    var statusMessage = new WebSocketMessage
                    {
                        Type = "protocol_status_changed",
                        Data = new
                        {
                            isConnected = _webSocketClient.IsConnected,
                            initialConnectionAttempted = true,
                            serverUri = _configManager.Config.WebsocketServerUri,
                            shouldAutoScan = string.IsNullOrEmpty(_configManager.Config.WebsocketServerUri) ||
                                           _configManager.Config.WebsocketServerUri == "ws://localhost:3000"
                        }
                    };
                    await _wsManager.SendServerMessageAsync(webSocket, statusMessage);
                }

                // 不再自动发送初始数据，等待客户端请求 get_initial_data
                await HandleWebSocketMessagesAsync(webSocket);
            }
            catch { context.Response.Close(); }
        }

        private async Task HandleWebSocketMessagesAsync(WebSocket webSocket)
        {
            await _wsManager.StartServerReceiveLoopAsync(
                webSocket,
                json => _webSocketHandler.ProcessWebSocketMessageAsync(webSocket, json),
                _globalCts.Token,
                onDisconnected: () =>
                {
                    BroadcastMessageToClients(new WebSocketMessage { Type = "client_count_updated", Data = _wsManager.GetServerClientCount() });
                }
            );
        }

        private void OnProtocolConnectionStateChanged(bool isConnected)
        {
            var messageType = isConnected ? "protocol_connected" : "protocol_disconnected";
            BroadcastMessageToClients(new WebSocketMessage
            {
                Type = messageType,
                Data = new { isConnected = isConnected }
            });

            if (isConnected && _webSocketClient != null && _webSocketClient.IsScanning)
            {
                _webSocketClient.StopScan();
            }
        }

        private void OnServiceFound(string address, string name)
        {
            BroadcastMessageToClients(new WebSocketMessage
            {
                Type = "service_found",
                Data = new { Address = address, Name = name }
            });
        }

        private void OnScanStateChanged(bool isScanning)
        {
            BroadcastMessageToClients(new WebSocketMessage
            {
                Type = "scan_state_changed",
                Data = new { isScanning = isScanning }
            });
        }

        private void OnInitialConnectionAttemptCompleted()
        {
            bool isConnected = _webSocketClient?.IsConnected ?? false;
            bool hasValidConfig = !string.IsNullOrEmpty(_configManager.Config.WebsocketServerUri) && 
                                  _configManager.Config.WebsocketServerUri != "ws://localhost:3000"; // 默认配置不算有效配置
            
            // 广播初始连接尝试完成事件
            BroadcastMessageToClients(new WebSocketMessage
            {
                Type = "initial_connection_attempt_completed"
            });
            
            // 同时广播当前连接状态（用于页面刷新后的状态同步）
            BroadcastMessageToClients(new WebSocketMessage
            {
                Type = "protocol_status_changed",
                Data = new
                {
                    isConnected = isConnected,
                    initialConnectionAttempted = true,
                    serverUri = _configManager.Config.WebsocketServerUri,
                    shouldAutoScan = !hasValidConfig // 只有没有有效配置时才自动扫描
                }
            });
        }

        public void BroadcastMessageToClients(WebSocketMessage message)
        {
            _wsManager.BroadcastToServerClients(message);
        }

        private void BroadcastChatHistoryInternal()
        {
            if (_selectedUserId == 0) return;

            var chatHistoryManager = _sessionManager.GetOrCreateChatHistoryManager(_selectedUserId);
            var messages = chatHistoryManager.GetLatestMessages(20);
            var response = new
            {
                messages = messages,
                hasMore = chatHistoryManager.GetMessageCount() > 20
            };
            BroadcastMessageToClients(new WebSocketMessage { Type = "chat_history", Data = response });
        }

        private void BroadcastQueueStatus(int queueCount)
        {
            try
            {
                BroadcastMessageToClients(new WebSocketMessage
                {
                    Type = "queue_status",
                    Data = queueCount
                });
            }
            catch (Exception ex)
            {
                Logger.LogError("CONTROL_PANEL", $"Error broadcasting queue status: {ex.Message}", ex);
            }
        }

        private bool ValidateControlPanelAccess(HttpListenerContext context)
        {
            var key = ControlPanelHelpers.GetQueryParameter(context.Request.Url.Query, "key");
            return !string.IsNullOrEmpty(key) && key == _controlPanelKey;
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
                context.Response.ContentType = ControlPanelHelpers.GetContentType(Path.GetExtension(fullPath));
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
                try { context.Response.Close(); } catch (Exception ex) { Logger.LogWarning("CONTROL_PANEL", $"Error closing response: {ex.Message}"); }
            }
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
                string errorMessage;
                bool success = _configManager.UpdateConfig(JsonConvert.DeserializeObject<dynamic>(json), out errorMessage);
                if (success)
                {
                    _llmService.UpdateApiKey(_configManager.Config.LlmApiKey);
                }
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
                _configManager.AcceptEula();
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

                await _wsManager.SendServerMessageAsync(webSocket, errorMessage);
                await _wsManager.CloseWebSocketAsync(webSocket, WebSocketCloseStatus.NormalClosure, "Authentication failed");
            }
            catch { context.Response.Close(); }
        }
    }
}
