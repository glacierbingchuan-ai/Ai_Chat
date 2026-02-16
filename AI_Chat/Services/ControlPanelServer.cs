using System;
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
using Newtonsoft.Json;

namespace AI_Chat.Services
{
    public class ControlPanelServer
    {
        private readonly ConfigManager _configManager;
        private readonly ContextManager _contextManager;
        private readonly LLMService _llmService;
        private readonly ChatHistoryManager _chatHistoryManager;
        private readonly MessageHandler _messageHandler;
        private readonly CancellationTokenSource _globalCts;
        private readonly PluginManager _pluginManager;
        private readonly PluginWebSocketHandler _pluginWebSocketHandler;

        private readonly object _controlPanelLock = new object();

        private HttpListener _httpListener;
        private List<WebSocket> _controlPanelClients = new List<WebSocket>();
        private string _controlPanelKey;
        private DateTime _startTime = DateTime.Now;

        public ControlPanelServer(
            ConfigManager configManager,
            ContextManager contextManager,
            LLMService llmService,
            ChatHistoryManager chatHistoryManager,
            MessageHandler messageHandler,
            CancellationTokenSource globalCts,
            PluginManager pluginManager = null)
        {
            _configManager = configManager;
            _contextManager = contextManager;
            _llmService = llmService;
            _chatHistoryManager = chatHistoryManager;
            _messageHandler = messageHandler;
            _globalCts = globalCts;
            _pluginManager = pluginManager;
            _pluginWebSocketHandler = pluginManager != null ? new PluginWebSocketHandler(pluginManager) : null;
            _controlPanelKey = GenerateSecureKey();
        }

        public string ControlPanelKey => _controlPanelKey;
        public string ControlPanelUrl => $"http://localhost:{AppConstants.CONTROL_PANEL_PORT}?key={_controlPanelKey}";

        public void InitializeBroadcastCallbacks()
        {
            Logger.Initialize(BroadcastMessageToClients);
            _chatHistoryManager.Initialize(
                msg => BroadcastMessageToClients(new WebSocketMessage { Type = "chat_message", Data = msg }),
                () => BroadcastChatHistory()
            );

            // 订阅插件配置变更事件
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

                        string path = context.Request.Url.LocalPath;
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
                lock (_controlPanelLock) _controlPanelClients.Add(webSocket);
                BroadcastMessageToClients(new WebSocketMessage { Type = "client_count_updated", Data = _controlPanelClients.Count });
                await SendInitialDataAsync(webSocket);
                await HandleWebSocketMessagesAsync(webSocket);
            }
            catch { context.Response.Close(); }
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
                lock (_controlPanelLock) _controlPanelClients.Remove(webSocket);
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
                        _contextManager.ClearContext();
                        _chatHistoryManager.ClearHistory();
                        BroadcastMessageToClients(new WebSocketMessage { Type = "context_cleared" });
                        BroadcastMessageToClients(new WebSocketMessage { Type = "scheduled_events_updated", Data = _contextManager.ScheduledEvents });
                        break;
                    case "config_update":
                        UpdateConfig(message.Data);
                        BroadcastMessageToClients(new WebSocketMessage { Type = "config_updated", Data = _configManager.Config });
                        break;
                    case "get_llm_status":
                        string status = await _llmService.GetLlmStatusAsync();
                        await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new WebSocketMessage { Type = "llm_status", Data = status }))), WebSocketMessageType.Text, true, CancellationToken.None);
                        break;
                    case "test_llm_connection":
                        await TestLlmConnectionAsync(webSocket, message.Data);
                        break;
                    case "get_runtime":
                        double uptime = (DateTime.Now - _startTime).TotalSeconds;
                        await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new WebSocketMessage { Type = "runtime", Data = uptime }))), WebSocketMessageType.Text, true, CancellationToken.None);
                        break;
                    case "test_connection":
                        await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new WebSocketMessage { Type = "connection_test", Data = "Connection test successful" }))), WebSocketMessageType.Text, true, CancellationToken.None);
                        break;
                    case "use_role_card":
                        await HandleUseRoleCardAsync(webSocket, message.Data);
                        break;
                    case "get_chat_history":
                        BroadcastChatHistory();
                        break;
                    default:
                        // Handle plugin-related messages
                        Logger.LogInfo("ControlPanel", $"Received message: {message.Type}");
                        if (_pluginWebSocketHandler != null &&
                            (message.Type.StartsWith("plugin_") ||
                             message.Type == "get_plugins" || message.Type == "start_plugin" ||
                             message.Type == "stop_plugin" || message.Type == "reload_plugin" ||
                             message.Type == "unload_plugin" || message.Type == "get_plugin_config" ||
                             message.Type == "set_plugin_config" || message.Type == "execute_plugin_command" ||
                             message.Type == "get_plugin_commands" || message.Type == "load_plugin_from_file" ||
                             message.Type == "upload_and_load_plugin" || message.Type == "get_plugin_readme" ||
                             message.Type == "get_plugin_permissions"))
                        {
                            Logger.LogInfo("ControlPanel", $"Routing to plugin handler: {message.Type}");
                            await _pluginWebSocketHandler.HandleMessageAsync(webSocket, message.Type, message.Data);
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

        private async Task SendInitialDataAsync(WebSocket webSocket)
        {
            try
            {
                List<EventModel> events = _contextManager.ScheduledEvents;
                List<ChatMessage> chatHistory = _chatHistoryManager.GetHistory();
                var initialData = new
                {
                    logs = Logger.GetLogs(),
                    config = _configManager.Config,
                    uptime = (DateTime.Now - _startTime).TotalSeconds,
                    scheduledEvents = events,
                    stats = new { totalMessages = _messageHandler.TotalMessages, proactiveChats = _messageHandler.ProactiveChats, reminders = _messageHandler.Reminders },
                    chatHistory = chatHistory
                };
                await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new WebSocketMessage { Type = "init", Data = initialData }))), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch { }
        }

        private async Task SendLogsAsync(WebSocket webSocket)
        {
            var message = new WebSocketMessage { Type = "logs", Data = Logger.GetLogs() };
            await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message))), WebSocketMessageType.Text, true, CancellationToken.None);
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
                await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new WebSocketMessage { Type = "llm_test_result", Data = message }))), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch (Exception ex) { Logger.LogError("CONTROL_PANEL", "Error testing LLM connection", ex); }
        }

        private async Task HandleUseRoleCardAsync(WebSocket webSocket, dynamic data)
        {
            try
            {
                string baseSystemPrompt = data?.baseSystemPrompt?.ToString();
                dynamic emojisData = data?.roleCardAvailableEmojis;

                // 直接更新配置，而不是创建新对象
                var currentConfig = _configManager.Config;
                if (!string.IsNullOrEmpty(baseSystemPrompt))
                {
                    currentConfig.BaseSystemPrompt = baseSystemPrompt;
                }

                _configManager.UpdateConfig(currentConfig);

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

                await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new WebSocketMessage { Type = "role_card_used", Data = "Role card applied successfully" }))), WebSocketMessageType.Text, true, CancellationToken.None);

                _contextManager.ClearContext();
                _chatHistoryManager.ClearHistory();

                BroadcastMessageToClients(new WebSocketMessage { Type = "context_cleared" });
                BroadcastMessageToClients(new WebSocketMessage { Type = "scheduled_events_updated", Data = _contextManager.ScheduledEvents });
                BroadcastMessageToClients(new WebSocketMessage { Type = "config_updated", Data = _configManager.Config });

                Logger.LogInfo("ROLE_CARDS", "Role card used successfully");
            }
            catch (Exception ex)
            {
                Logger.LogError("ROLE_CARDS", "Error handling use_role_card: " + ex.Message);
                await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new WebSocketMessage { Type = "role_card_error", Data = "Failed to apply role card: " + ex.Message }))), WebSocketMessageType.Text, true, CancellationToken.None);
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
            byte[] buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message));
            lock (_controlPanelLock)
            {
                foreach (var client in _controlPanelClients.Where(c => c.State == WebSocketState.Open))
                    _ = client.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }

        private void BroadcastChatHistory()
        {
            List<ChatMessage> chatHistory = _chatHistoryManager.GetHistory();
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

                // 清理文件名
                fileName = Path.GetFileName(fileName);
                if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    fileName += ".dll";
                }

                string pluginPath = Path.Combine(_pluginManager.PluginDirectory, fileName);

                // 检查文件是否已存在
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

                // 开始下载
                using (var client = new System.Net.Http.HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(5);
                    
                    // 获取文件大小
                    var headResponse = await client.SendAsync(new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Head, url));
                    long? totalBytes = headResponse.Content.Headers.ContentLength;

                    // 下载文件
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

                    // 广播下载开始
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

                            // 每100ms更新一次进度
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

                    // 广播下载完成，通知前端加载插件
                    BroadcastMessageToClients(new WebSocketMessage 
                    { 
                        Type = "plugin_download_complete", 
                        Data = new { pluginName = pluginName, fileName = fileName, path = pluginPath }
                    });

                    // 返回成功响应
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

                // 广播下载错误
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
