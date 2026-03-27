using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using AI_Chat.Models;
using AI_Chat.Plugins;
using AI_Chat.Utils;
using Newtonsoft.Json;

namespace AI_Chat.Services.ControlPanel.Handlers
{
    public class ProxyHandler
    {
        private readonly ConfigManager _configManager;
        private readonly PluginManager _pluginManager;
        private readonly WebSocketManager _wsManager;

        public ProxyHandler(ConfigManager configManager, PluginManager pluginManager, WebSocketManager wsManager)
        {
            _configManager = configManager;
            _pluginManager = pluginManager;
            _wsManager = wsManager;
        }

        public async Task ServeProxyAsync(HttpListenerContext context)
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
                    case "get_image":
                        await ServeUserImageAsync(context);
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

        private async Task ServeUserImageAsync(HttpListenerContext context)
        {
            try
            {
                string query = context.Request.Url.Query;
                string fileName = GetQueryParameter(query, "filename");
                string userIdStr = GetQueryParameter(query, "userId");

                if (string.IsNullOrEmpty(fileName))
                {
                    context.Response.StatusCode = 400;
                    byte[] buffer = Encoding.UTF8.GetBytes("Missing filename parameter");
                    context.Response.ContentType = "text/plain";
                    context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                    context.Response.Close();
                    return;
                }

                if (string.IsNullOrEmpty(userIdStr) || !long.TryParse(userIdStr, out long userId))
                {
                    context.Response.StatusCode = 400;
                    byte[] buffer = Encoding.UTF8.GetBytes("Missing or invalid userId parameter");
                    context.Response.ContentType = "text/plain";
                    context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                    context.Response.Close();
                    return;
                }

                string imagePath = Path.Combine(PathUtils.GetUserDirectory(userId), "images", fileName);

                if (!File.Exists(imagePath))
                {
                    context.Response.StatusCode = 404;
                    byte[] buffer = Encoding.UTF8.GetBytes("Image not found");
                    context.Response.ContentType = "text/plain";
                    context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                    context.Response.Close();
                    return;
                }

                byte[] imageData = File.ReadAllBytes(imagePath);
                string extension = Path.GetExtension(fileName).ToLowerInvariant();
                string contentType = extension switch
                {
                    ".png" => "image/png",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".gif" => "image/gif",
                    ".webp" => "image/webp",
                    ".bmp" => "image/bmp",
                    _ => "image/jpeg"
                };

                context.Response.ContentType = contentType;
                context.Response.OutputStream.Write(imageData, 0, imageData.Length);
                Logger.LogInfo("PROXY", $"Served image: {fileName} for user {userId}");
            }
            catch (Exception ex)
            {
                Logger.LogError("PROXY", "Error serving user image: " + ex.Message);
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
                                    Data = new
                                    {
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

                    byte[] responseBuffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new
                    {
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

        private void BroadcastMessageToClients(WebSocketMessage message)
        {
            _wsManager.BroadcastToServerClients(message);
        }

        private static string GetQueryParameter(string query, string name)
        {
            if (string.IsNullOrEmpty(query)) return null;
            if (query.StartsWith("?")) query = query.Substring(1);
            var param = query.Split('&').Select(p => p.Split('=')).FirstOrDefault(parts => parts.Length == 2 && parts[0].Equals(name, StringComparison.OrdinalIgnoreCase));
            if (param == null) return null;
            try { return Uri.UnescapeDataString(param[1]); } catch { return param[1]; }
        }
    }
}
