using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AI_Chat.Models;
using Newtonsoft.Json;

namespace AI_Chat.Services
{
    /// <summary>
    /// 统一的 WebSocket 管理器 - 管理所有 WebSocket 连接的发送和接收
    /// 提供线程安全的消息发送机制和统一的锁管理
    /// </summary>
    public class WebSocketManager
    {
        // 客户端 WebSocket 锁（用于 WebSocketClient）
        private readonly SemaphoreSlim _clientSendLock = new SemaphoreSlim(1, 1);

        // 服务器端 WebSocket 锁字典（用于 ControlPanelServer 和 PluginWebSocketHandler）
        private readonly ConcurrentDictionary<WebSocket, SemaphoreSlim> _serverClientLocks = new ConcurrentDictionary<WebSocket, SemaphoreSlim>();

        // 保护服务器客户端列表的锁
        private readonly object _serverClientsLock = new object();

        // 服务器端 WebSocket 列表
        private readonly List<WebSocket> _serverClients = new List<WebSocket>();

        /// <summary>
        /// 发送消息到客户端 WebSocket（WebSocketClient 使用）
        /// </summary>
        public async Task SendClientMessageAsync(ClientWebSocket webSocket, string message)
        {
            if (webSocket?.State != WebSocketState.Open)
                return;

            await _clientSendLock.WaitAsync();
            try
            {
                if (webSocket.State == WebSocketState.Open)
                {
                    var bytes = Encoding.UTF8.GetBytes(message);
                    await webSocket.SendAsync(
                        new ArraySegment<byte>(bytes),
                        WebSocketMessageType.Text,
                        true,
                        CancellationToken.None);
                }
            }
            catch (WebSocketException)
            {
                // 忽略 WebSocket 异常
            }
            catch (Exception ex)
            {
                Logger.LogError("WS_MANAGER", $"Error sending client message: {ex.Message}");
            }
            finally
            {
                _clientSendLock.Release();
            }
        }

        /// <summary>
        /// 发送消息到服务器端 WebSocket（单个客户端）
        /// </summary>
        public async Task SendServerMessageAsync(WebSocket webSocket, string message)
        {
            if (webSocket?.State != WebSocketState.Open)
                return;

            // 获取或创建客户端锁
            var semaphore = _serverClientLocks.GetOrAdd(webSocket, _ => new SemaphoreSlim(1, 1));

            await semaphore.WaitAsync();
            try
            {
                if (webSocket.State == WebSocketState.Open)
                {
                    var bytes = Encoding.UTF8.GetBytes(message);
                    await webSocket.SendAsync(
                        new ArraySegment<byte>(bytes),
                        WebSocketMessageType.Text,
                        true,
                        CancellationToken.None);
                }
            }
            catch (WebSocketException)
            {
                // 忽略 WebSocket 异常
            }
            catch (Exception ex)
            {
                Logger.LogError("WS_MANAGER", $"Error sending server message: {ex.Message}");
            }
            finally
            {
                semaphore.Release();
            }
        }

        /// <summary>
        /// 发送消息到服务器端 WebSocket（使用 WebSocketMessage 对象）
        /// </summary>
        public async Task SendServerMessageAsync(WebSocket webSocket, WebSocketMessage message)
        {
            var json = JsonConvert.SerializeObject(message);
            await SendServerMessageAsync(webSocket, json);
        }

        /// <summary>
        /// 广播消息到所有服务器端客户端
        /// </summary>
        public void BroadcastToServerClients(WebSocketMessage message)
        {
            var json = JsonConvert.SerializeObject(message);
            BroadcastToServerClients(json);
        }

        /// <summary>
        /// 广播消息到所有服务器端客户端
        /// </summary>
        public void BroadcastToServerClients(string message)
        {
            List<WebSocket> clientsSnapshot;
            lock (_serverClientsLock)
            {
                // 创建客户端列表的快照，避免在遍历时修改集合
                clientsSnapshot = new List<WebSocket>(_serverClients);
            }

            var clientsToRemove = new List<WebSocket>();

            foreach (var client in clientsSnapshot)
            {
                if (client.State == WebSocketState.Open)
                {
                    _ = Task.Run(async () =>
                    {
                        await SendServerMessageAsync(client, message);
                    });
                }
                else if (client.State == WebSocketState.Aborted || client.State == WebSocketState.Closed)
                {
                    clientsToRemove.Add(client);
                }
            }

            // 清理断开的客户端
            if (clientsToRemove.Count > 0)
            {
                lock (_serverClientsLock)
                {
                    foreach (var client in clientsToRemove)
                    {
                        _serverClients.Remove(client);
                        _serverClientLocks.TryRemove(client, out _);
                    }
                }
            }
        }

        /// <summary>
        /// 注册服务器端 WebSocket 客户端
        /// </summary>
        public void RegisterServerClient(WebSocket webSocket)
        {
            // 为客户端创建锁
            _serverClientLocks[webSocket] = new SemaphoreSlim(1, 1);

            lock (_serverClientsLock)
            {
                _serverClients.Add(webSocket);
            }
        }

        /// <summary>
        /// 注销服务器端 WebSocket 客户端
        /// </summary>
        public void UnregisterServerClient(WebSocket webSocket)
        {
            lock (_serverClientsLock)
            {
                _serverClients.Remove(webSocket);
            }
            _serverClientLocks.TryRemove(webSocket, out _);
        }

        /// <summary>
        /// 获取服务器端客户端数量
        /// </summary>
        public int GetServerClientCount()
        {
            lock (_serverClientsLock)
            {
                return _serverClients.Count;
            }
        }

        /// <summary>
        /// 获取服务器端客户端列表快照
        /// </summary>
        public List<WebSocket> GetServerClientsSnapshot()
        {
            lock (_serverClientsLock)
            {
                return new List<WebSocket>(_serverClients);
            }
        }

        /// <summary>
        /// 接收消息（通用方法）
        /// </summary>
        public async Task<string> ReceiveMessageAsync(WebSocket webSocket, byte[] buffer, CancellationToken cancellationToken)
        {
            var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

            if (result.MessageType == WebSocketMessageType.Text)
            {
                return Encoding.UTF8.GetString(buffer, 0, result.Count);
            }

            return null;
        }

        /// <summary>
        /// 接收完整消息（处理分片消息）
        /// </summary>
        public async Task<string> ReceiveFullMessageAsync(WebSocket webSocket, byte[] buffer, CancellationToken cancellationToken)
        {
            var messageBuilder = new StringBuilder();

            while (true)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                    if (result.EndOfMessage)
                    {
                        return messageBuilder.ToString();
                    }
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// 启动服务器端 WebSocket 消息接收循环（统一处理方法）
        /// </summary>
        public async Task StartServerReceiveLoopAsync(WebSocket webSocket, Func<string, Task> messageHandler, CancellationToken cancellationToken, Action onDisconnected = null)
        {
            try
            {
                var buffer = new byte[1024 * 8];
                var messageBuilder = new StringBuilder();

                while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                        if (result.MessageType == WebSocketMessageType.Text)
                        {
                            messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                            if (result.EndOfMessage)
                            {
                                string json = messageBuilder.ToString();
                                messageBuilder.Clear();

                                // 在后台处理消息，不阻塞接收循环
                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                        await messageHandler?.Invoke(json);
                                    }
                                    catch (Exception ex)
                                    {
                                        Logger.LogError("WS_MANAGER", $"Error handling message: {ex.Message}");
                                    }
                                }, cancellationToken);
                            }
                        }
                        else if (result.MessageType == WebSocketMessageType.Close)
                        {
                            break;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (WebSocketException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError("WS_MANAGER", $"Error receiving message: {ex.Message}");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("WS_MANAGER", $"Error in receive loop: {ex.Message}");
            }
            finally
            {
                // 清理客户端资源
                UnregisterServerClient(webSocket);
                onDisconnected?.Invoke();
            }
        }

        /// <summary>
        /// 启动客户端 WebSocket 消息接收循环（统一处理方法）
        /// </summary>
        public async Task StartClientReceiveLoopAsync(ClientWebSocket webSocket, Func<string, Task> messageHandler, CancellationToken cancellationToken)
        {
            var buffer = new byte[1024 * 8];

            while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string json = Encoding.UTF8.GetString(buffer, 0, result.Count);

                        // 在后台处理消息，不阻塞接收循环
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await messageHandler?.Invoke(json);
                            }
                            catch (Exception ex)
                            {
                                Logger.LogError("WS_MANAGER", $"Error handling client message: {ex.Message}");
                            }
                        }, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (WebSocketException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.LogError("WS_MANAGER", $"Error receiving client message: {ex.Message}");
                    break;
                }
            }
        }

        /// <summary>
        /// 关闭 WebSocket 连接（安全关闭）
        /// </summary>
        public async Task CloseWebSocketAsync(WebSocket webSocket, WebSocketCloseStatus closeStatus, string statusDescription)
        {
            try
            {
                if (webSocket?.State == WebSocketState.Open)
                {
                    await webSocket.CloseAsync(closeStatus, statusDescription, CancellationToken.None);
                }
            }
            catch (WebSocketException)
            {
                // 忽略关闭时的异常
            }
            catch (Exception ex)
            {
                Logger.LogError("WS_MANAGER", $"Error closing WebSocket: {ex.Message}");
            }
        }
    }
}
