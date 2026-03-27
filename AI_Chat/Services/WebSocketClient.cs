using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AI_Chat.Models;
using Newtonsoft.Json;

namespace AI_Chat.Services
{
    public class WebSocketClient
    {
        private ClientWebSocket _webSocket;
        private readonly ConfigManager _configManager;
        private readonly CancellationTokenSource _globalCts;
        private readonly WebSocketManager _wsManager;
        private Func<string, Task> _messageHandler;

        // 连接状态
        private bool _isConnected = false;
        private bool _isConnecting = false;
        private bool _manualDisconnect = false;
        private string _currentServerUri = null;
        private string _currentToken = null;
        private int _currentKeepAliveInterval = 30000;

        // 扫描相关
        private CancellationTokenSource _scanCts;
        private bool _isScanning = false;

        // 初始连接状态
        private bool _initialConnectionAttempted = false;

        // 机器人信息（从协议端获取）
        private string _botNickname = null;
        private long _botUserId = 0;
        private string _botAvatarUrl = null;
        private string _protocolType = "Unknown";

        // 事件
        public event Action<bool> OnConnectionStateChanged;
        public event Action<string, string> OnServiceFound; // address, name
        public event Action<bool> OnScanStateChanged;
        public event Action OnInitialConnectionAttemptCompleted; // 初始连接尝试完成（无论成功或失败）
        public event Action OnBotInfoReceived; // 机器人信息获取完成

        public ClientWebSocket WebSocket => _webSocket;
        public bool IsConnected => _isConnected;
        public bool IsConnecting => _isConnecting;
        public bool IsScanning => _isScanning;
        public bool InitialConnectionAttempted => _initialConnectionAttempted;

        // 机器人信息属性
        public string BotNickname => _botNickname ?? "Unknown";
        public long BotUserId => _botUserId;
        public string BotAvatarUrl => _botAvatarUrl;
        public string ProtocolType => _protocolType;

        public WebSocketClient(ConfigManager configManager, CancellationTokenSource globalCts, WebSocketManager wsManager = null)
        {
            _configManager = configManager;
            _globalCts = globalCts;
            _wsManager = wsManager ?? new WebSocketManager();
        }

        public void SetMessageHandler(Func<string, Task> handler)
        {
            _messageHandler = handler;
        }

        /// <summary>
        /// 初始化启动时的一次性连接尝试（不自动重连）
        /// </summary>
        public async Task<bool> TryConnectAsync()
        {
            if (_isConnected || _isConnecting)
                return _isConnected;

            // 如果没有配置协议端地址，直接返回失败
            if (string.IsNullOrEmpty(_configManager.Config.WebsocketServerUri))
            {
                Logger.LogInfo("WS_CLIENT", "No WebSocket server URI configured, skipping initial connection");
                _initialConnectionAttempted = true;
                OnInitialConnectionAttemptCompleted?.Invoke();
                return false;
            }

            try
            {
                var result = await ConnectInternalAsync(
                    _configManager.Config.WebsocketServerUri,
                    _configManager.Config.WebsocketToken,
                    _configManager.Config.WebsocketKeepAliveInterval
                );

                _initialConnectionAttempted = true;
                return result;
            }
            catch
            {
                _initialConnectionAttempted = true;
                return false;
            }
            finally
            {
                // 触发初始连接尝试完成事件
                OnInitialConnectionAttemptCompleted?.Invoke();
            }
        }

        /// <summary>
        /// 手动连接到指定的协议端
        /// </summary>
        public async Task<(bool success, string message)> ConnectAsync(string serverUri, string token, int keepAliveInterval)
        {
            if (_isConnected)
            {
                return (false, "Already connected to a protocol server");
            }

            if (_isConnecting)
            {
                return (false, "Connection is in progress");
            }

            _manualDisconnect = false;
            var success = await ConnectInternalAsync(serverUri, token, keepAliveInterval);

            if (success)
            {
                // 保存配置
                _configManager.Config.WebsocketServerUri = serverUri;
                _configManager.Config.WebsocketToken = token;
                _configManager.Config.WebsocketKeepAliveInterval = keepAliveInterval;
                _configManager.SaveConfig();

                return (true, "Connected successfully");
            }
            else
            {
                return (false, "Failed to connect to the protocol server");
            }
        }

        /// <summary>
        /// 断开协议端连接
        /// </summary>
        public async Task DisconnectAsync()
        {
            _manualDisconnect = true;
            _isConnecting = false;

            if (_webSocket != null)
            {
                try
                {
                    if (_webSocket.State == WebSocketState.Open)
                    {
                        await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Manual disconnect", CancellationToken.None);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning("WS_CLIENT", $"Error closing WebSocket: {ex.Message}");
                }
                finally
                {
                    try { _webSocket.Dispose(); } catch { }
                    _webSocket = null;
                }
            }

            if (_isConnected)
            {
                _isConnected = false;
                OnConnectionStateChanged?.Invoke(false);
            }

            Logger.LogInfo("WS_CLIENT", "Disconnected from protocol server");
        }

        /// <summary>
        /// 内部连接方法
        /// </summary>
        private async Task<bool> ConnectInternalAsync(string serverUri, string token, int keepAliveInterval)
        {
            if (_isConnecting) return false;

            _isConnecting = true;
            _currentServerUri = serverUri;
            _currentToken = token;
            _currentKeepAliveInterval = keepAliveInterval;

            ClientWebSocket currentWebSocket = null;
            try
            {
                currentWebSocket = new ClientWebSocket();
                _webSocket = currentWebSocket;

                if (!string.IsNullOrEmpty(token))
                {
                    currentWebSocket.Options.SetRequestHeader("Authorization", "Bearer " + token);
                }

                Logger.LogInfo("WS_CLIENT", "Attempting connection to WebSocket server: " + serverUri);
                await currentWebSocket.ConnectAsync(new Uri(serverUri), _globalCts.Token);
                Logger.LogInfo("WS_CLIENT", "Connection established. Inbound message listener activated.");

                _isConnected = true;
                _isConnecting = false;
                OnConnectionStateChanged?.Invoke(true);

                // 启动接收和心跳任务
                _ = Task.Run(async () => await ReceiveMessagesAsync(currentWebSocket));
                _ = Task.Run(async () => await SendKeepAliveAsync(currentWebSocket));

                // 获取机器人信息
                _ = Task.Run(async () => await GetBotInfoAsync());

                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError("WS_CLIENT", "WebSocket connection failure.", ex);
                _isConnecting = false;

                if (currentWebSocket != null)
                {
                    try { currentWebSocket.Dispose(); } catch { }
                }
                _webSocket = null;

                return false;
            }
        }

        private Func<string, Task> _llmWebSocketHandler;

        /// <summary>
        /// 设置LLMService的WebSocket响应处理器
        /// </summary>
        public void SetLLMWebSocketHandler(Func<string, Task> handler)
        {
            _llmWebSocketHandler = handler;
        }

        /// <summary>
        /// 使用 WebSocketManager 统一处理消息接收
        /// </summary>
        private async Task ReceiveMessagesAsync(ClientWebSocket webSocket)
        {
            try
            {
                await _wsManager.StartClientReceiveLoopAsync(
                    webSocket,
                    async json =>
                    {
                        // 先尝试处理机器人信息响应
                        HandleBotInfoResponse(json);

                        // 传递给LLMService处理图片响应
                        if (_llmWebSocketHandler != null)
                        {
                            await _llmWebSocketHandler.Invoke(json);
                        }

                        // 再传递给外部处理器
                        if (_messageHandler != null)
                        {
                            await _messageHandler.Invoke(json);
                        }
                    },
                    _globalCts.Token
                );
            }
            catch (Exception ex)
            {
                Logger.LogWarning("WS_CLIENT", $"Receive loop ended: {ex.Message}");
            }
            finally
            {
                // 连接断开处理
                if (!_manualDisconnect && _isConnected)
                {
                    _isConnected = false;
                    OnConnectionStateChanged?.Invoke(false);
                    Logger.LogWarning("WS_CLIENT", "WebSocket connection lost");
                }
            }
        }

        private async Task SendKeepAliveAsync(ClientWebSocket webSocket)
        {
            try
            {
                while (webSocket.State == WebSocketState.Open && _isConnected && !_globalCts.IsCancellationRequested)
                {
                    await Task.Delay(_currentKeepAliveInterval);
                    if (webSocket.State == WebSocketState.Open)
                    {
                        await _wsManager.SendClientMessageAsync(webSocket, "{\"action\":\"get_status\"}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("WS_CLIENT", $"Keep-alive task ended: {ex.Message}");
            }
        }

        /// <summary>
        /// 发送消息到当前连接的 WebSocket（统一入口，使用 WebSocketManager）
        /// </summary>
        public async Task SendMessageAsync(string json)
        {
            var currentWs = _webSocket;
            if (currentWs != null && currentWs.State == WebSocketState.Open && _isConnected)
            {
                await _wsManager.SendClientMessageAsync(currentWs, json);
                Logger.LogInfo("WS_CLIENT", $"Message sent: {json.Substring(0, Math.Min(json.Length, 100))}...");
            }
        }

        /// <summary>
        /// 发送消息到指定的 WebSocket（兼容旧代码，使用 WebSocketManager）
        /// </summary>
        public async Task SendMessageAsync(ClientWebSocket webSocket, string json)
        {
            await _wsManager.SendClientMessageAsync(webSocket, json);
        }

        /// <summary>
        /// 获取机器人信息（OneBot get_login_info API）
        /// </summary>
        private async Task GetBotInfoAsync()
        {
            try
            {
                // 等待连接稳定
                await Task.Delay(500);

                if (!_isConnected)
                {
                    Logger.LogWarning("WS_CLIENT", "Cannot get bot info: not connected");
                    return;
                }

                // 发送 get_login_info 请求
                var request = new
                {
                    action = "get_login_info",
                    @params = new { },
                    echo = Guid.NewGuid().ToString()
                };

                var json = JsonConvert.SerializeObject(request);
                await SendMessageAsync(json);
                Logger.LogInfo("WS_CLIENT", "Sent get_login_info request");

                // 同时获取版本信息
                var versionRequest = new
                {
                    action = "get_version_info",
                    @params = new { },
                    echo = Guid.NewGuid().ToString()
                };

                var versionJson = JsonConvert.SerializeObject(versionRequest);
                await SendMessageAsync(versionJson);
                Logger.LogInfo("WS_CLIENT", "Sent get_version_info request");
            }
            catch (Exception ex)
            {
                Logger.LogError("WS_CLIENT", $"Error getting bot info: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理协议端返回的机器人信息
        /// </summary>
        public void HandleBotInfoResponse(string json)
        {
            try
            {
                var response = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                if (response == null) return;

                // 检查是否是登录信息响应
                if (response.ContainsKey("data") && response.ContainsKey("echo"))
                {
                    var data = response["data"] as Newtonsoft.Json.Linq.JObject;
                    if (data == null) return;

                    // get_login_info 响应
                    if (data.ContainsKey("user_id") && data.ContainsKey("nickname"))
                    {
                        _botUserId = data["user_id"]?.ToObject<long>() ?? 0;
                        _botNickname = data["nickname"]?.ToString();

                        // 构建头像URL (OneBot 标准头像)
                        if (_botUserId > 0)
                        {
                            _botAvatarUrl = $"https://q1.qlogo.cn/g?b=qq&nk={_botUserId}&s=100";
                        }

                        Logger.LogInfo("WS_CLIENT", $"Bot info received: {_botNickname} ({_botUserId})");
                        OnBotInfoReceived?.Invoke();
                    }

                    // get_version_info 响应
                    if (data.ContainsKey("app_name"))
                    {
                        _protocolType = data["app_name"]?.ToString() ?? "Unknown";
                        Logger.LogInfo("WS_CLIENT", $"Protocol type: {_protocolType}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("WS_CLIENT", $"Error handling bot info response: {ex.Message}");
            }
        }

        public void ForceReconnect()
        {
            if (_webSocket != null)
            {
                try { _webSocket.Dispose(); } catch (Exception ex) { Logger.LogWarning("WS_CLIENT", $"Error disposing WebSocket during reconnect: {ex.Message}"); }
            }
        }

        #region 协议端扫描功能

        /// <summary>
        /// 开始扫描本机协议端
        /// </summary>
        public async Task StartScanAsync()
        {
            if (_isScanning) return;

            _isScanning = true;
            _scanCts = new CancellationTokenSource();
            OnScanStateChanged?.Invoke(true);

            Logger.LogInfo("WS_CLIENT", "Starting protocol server scan...");

            _ = Task.Run(async () => await ScanPortsAsync(_scanCts.Token));
        }

        /// <summary>
        /// 停止扫描
        /// </summary>
        public void StopScan()
        {
            if (!_isScanning) return;

            _scanCts?.Cancel();
            _isScanning = false;
            OnScanStateChanged?.Invoke(false);

            Logger.LogInfo("WS_CLIENT", "Protocol server scan stopped");
        }

        /// <summary>
        /// 扫描本机端口 (1-10000)，循环扫描直到收到停止信号
        /// </summary>
        private async Task ScanPortsAsync(CancellationToken cancellationToken)
        {
            try
            {
                int round = 0;
                while (!cancellationToken.IsCancellationRequested)
                {
                    round++;
                    Logger.LogInfo("WS_CLIENT", $"Starting port scan round #{round}...");

                    // 并行扫描 1-10000 范围内的所有端口，每次并发500个
                    int batchSize = 500;
                    int totalPorts = 10000;
                    int completedPorts = 0;

                    for (int batchStart = 1; batchStart <= totalPorts && !cancellationToken.IsCancellationRequested; batchStart += batchSize)
                    {
                        int batchEnd = Math.Min(batchStart + batchSize - 1, totalPorts);
                        var tasks = new List<Task>();

                        for (int port = batchStart; port <= batchEnd && !cancellationToken.IsCancellationRequested; port++)
                        {
                            int localPort = port;
                            tasks.Add(Task.Run(async () =>
                            {
                                try
                                {
                                    await TryConnectToPortAsync(localPort, cancellationToken);
                                }
                                catch { }
                            }, cancellationToken));
                        }

                        await Task.WhenAll(tasks);
                        completedPorts = batchEnd;
                        Logger.LogInfo("WS_CLIENT", $"Scanning progress: {completedPorts}/{totalPorts} (round #{round})");
                    }

                    if (!cancellationToken.IsCancellationRequested)
                    {
                        Logger.LogInfo("WS_CLIENT", $"Port scan round #{round} completed. Waiting 5 seconds before next round...");
                        await Task.Delay(5000, cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消，不记录错误
                Logger.LogInfo("WS_CLIENT", "Port scan cancelled");
            }
            catch (Exception ex)
            {
                Logger.LogError("WS_CLIENT", $"Error during port scan: {ex.Message}");
            }
            finally
            {
                _isScanning = false;
                OnScanStateChanged?.Invoke(false);
                Logger.LogInfo("WS_CLIENT", "Protocol server scan stopped");
            }
        }

        /// <summary>
        /// 尝试连接指定端口 - 先进行TCP测试，再进行WebSocket验证
        /// </summary>
        private async Task TryConnectToPortAsync(int port, CancellationToken cancellationToken)
        {
            // 第一步：快速TCP端口测试
            if (!await IsTcpPortOpenAsync(port, cancellationToken))
            {
                return; // TCP端口未开放，直接跳过
            }

            // 第二步：TCP端口开放，尝试WebSocket连接验证
            await TryWebSocketConnectAsync(port, cancellationToken);
        }

        /// <summary>
        /// 快速TCP端口连通性测试
        /// </summary>
        private async Task<bool> IsTcpPortOpenAsync(int port, CancellationToken cancellationToken)
        {
            try
            {
                using (var tcpClient = new TcpClient())
                {
                    var connectTask = tcpClient.ConnectAsync(IPAddress.Loopback, port);
                    var timeoutTask = Task.Delay(500, cancellationToken); // TCP测试超时500ms

                    var completedTask = await Task.WhenAny(connectTask, timeoutTask);
                    
                    if (completedTask == connectTask && tcpClient.Connected)
                    {
                        Logger.LogInfo("WS_CLIENT", $"Found open TCP port: {port}");
                        return true;
                    }
                }
            }
            catch
            {
                // 连接失败，端口未开放
            }
            return false;
        }

        /// <summary>
        /// 尝试WebSocket连接并验证协议端
        /// </summary>
        private async Task TryWebSocketConnectAsync(int port, CancellationToken cancellationToken)
        {
            try
            {
                using (var client = new ClientWebSocket())
                {
                    client.Options.SetRequestHeader("Authorization", "Bearer ");
                    client.Options.KeepAliveInterval = TimeSpan.FromSeconds(2);

                    var uri = $"ws://127.0.0.1:{port}";
                    var connectTask = client.ConnectAsync(new Uri(uri), cancellationToken);
                    var timeoutTask = Task.Delay(1000, cancellationToken);

                    var completedTask = await Task.WhenAny(connectTask, timeoutTask);

                    if (completedTask == connectTask && client.State == WebSocketState.Open)
                    {
                        // 连接成功，尝试发送心跳验证
                        try
                        {
                            var heartbeatMessage = "{\"action\":\"get_status\"}";
                            await client.SendAsync(
                                new ArraySegment<byte>(Encoding.UTF8.GetBytes(heartbeatMessage)),
                                WebSocketMessageType.Text,
                                true,
                                cancellationToken
                            );

                            // 等待响应（1秒内未收到响应则判定不是协议端）
                            var buffer = new byte[1024];
                            var receiveTask = client.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                            var responseTimeoutTask = Task.Delay(1000, cancellationToken);

                            var responseTask = await Task.WhenAny(receiveTask, responseTimeoutTask);

                            if (responseTask == receiveTask)
                            {
                                var result = await receiveTask;
                                if (result.MessageType == WebSocketMessageType.Text)
                                {
                                    var response = Encoding.UTF8.GetString(buffer, 0, result.Count);
                                    // 收到心跳响应，确认是协议端
                                    Logger.LogInfo("WS_CLIENT", $"Found protocol server at {uri}");
                                    OnServiceFound?.Invoke(uri, "OneBot Service");
                                }
                            }
                        }
                        catch { }

                        try
                        {
                            await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Scan complete", CancellationToken.None);
                        }
                        catch { }
                    }
                }
            }
            catch
            {
                // WebSocket连接失败，忽略
            }
        }

        #endregion
    }
}
