using System;
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
        private Func<string, Task> _messageHandler;

        public ClientWebSocket WebSocket => _webSocket;

        public WebSocketClient(ConfigManager configManager, CancellationTokenSource globalCts)
        {
            _configManager = configManager;
            _globalCts = globalCts;
        }

        public void SetMessageHandler(Func<string, Task> handler)
        {
            _messageHandler = handler;
        }

        public async Task StartAsync()
        {
            while (!_globalCts.IsCancellationRequested)
            {
                ClientWebSocket currentWebSocket = null;
                try
                {
                    currentWebSocket = new ClientWebSocket();
                    _webSocket = currentWebSocket;

                    if (!string.IsNullOrEmpty(_configManager.Config.WebsocketToken))
                    {
                        currentWebSocket.Options.SetRequestHeader("Authorization", "Bearer " + _configManager.Config.WebsocketToken);
                    }

                    Logger.LogInfo("WS_CLIENT", "Attempting connection to WebSocket server: " + _configManager.Config.WebsocketServerUri);
                    await currentWebSocket.ConnectAsync(new Uri(_configManager.Config.WebsocketServerUri), _globalCts.Token);
                    Logger.LogInfo("WS_CLIENT", "Connection established. Inbound message listener activated.");

                    var receiveTask = ReceiveMessagesAsync(currentWebSocket);
                    var keepAliveTask = SendKeepAliveAsync(currentWebSocket);

                    await Task.WhenAny(receiveTask, keepAliveTask);

                    Logger.LogWarning("WS_CLIENT", "WebSocket connection lost or closed by server.");
                }
                catch (Exception ex)
                {
                    Logger.LogError("WS_CLIENT", "WebSocket connection failure.", ex);
                }
                finally
                {
                    if (currentWebSocket != null)
                    {
                        try { currentWebSocket.Dispose(); } catch { }
                        if (_webSocket == currentWebSocket) _webSocket = null;
                    }

                    if (!_globalCts.IsCancellationRequested)
                    {
                        Logger.LogInfo("WS_CLIENT", "Waiting 5 seconds before reconnecting...");
                        await Task.Delay(5000);
                    }
                }
            }
        }

        private async Task ReceiveMessagesAsync(ClientWebSocket webSocket)
        {
            var buffer = new byte[1024 * 8];
            while (webSocket.State == WebSocketState.Open)
            {
                try
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _globalCts.Token);
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        _ = Task.Run(() => _messageHandler?.Invoke(json));
                    }
                }
                catch { break; }
            }
        }

        private async Task SendKeepAliveAsync(ClientWebSocket webSocket)
        {
            while (webSocket.State == WebSocketState.Open)
            {
                await Task.Delay(_configManager.Config.WebsocketKeepAliveInterval);
                await SendMessageAsync(webSocket, "{\"action\":\"get_status\"}");
            }
        }

        public async Task SendMessageAsync(ClientWebSocket webSocket, string json)
        {
            if (webSocket?.State == WebSocketState.Open)
            {
                await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }

        public async Task SendMessageAsync(string json)
        {
            var currentWs = _webSocket;
            if (currentWs != null && currentWs.State == WebSocketState.Open)
            {
                await SendMessageAsync(currentWs, json);
                Logger.LogInfo("WS_CLIENT", $"Message sent: {json.Substring(0, Math.Min(json.Length, 100))}...");
            }
        }

        public void ForceReconnect()
        {
            if (_webSocket != null)
            {
                try { _webSocket.Dispose(); } catch { }
            }
        }
    }
}
