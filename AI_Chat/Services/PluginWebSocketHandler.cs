using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AI_Chat.Services;
using Newtonsoft.Json;

namespace AI_Chat.Plugins
{
    /// <summary>
    /// 插件WebSocket处理器 - 处理插件相关的WebSocket消息
    /// </summary>
    public class PluginWebSocketHandler
    {
        private readonly PluginManager _pluginManager;

        public PluginWebSocketHandler(PluginManager pluginManager)
        {
            _pluginManager = pluginManager;
        }

        /// <summary>
        /// 处理插件相关消息
        /// </summary>
        public async Task HandleMessageAsync(WebSocket webSocket, string messageType, dynamic data)
        {
            Logger.LogInfo("PLUGIN_WS", $"Processing message: {messageType}");
            try
            {
                switch (messageType)
                {
                    case "get_plugins":
                        await HandleGetPluginsAsync(webSocket);
                        break;
                    case "start_plugin":
                        await HandleStartPluginAsync(webSocket, data);
                        break;
                    case "stop_plugin":
                        await HandleStopPluginAsync(webSocket, data);
                        break;
                    case "reload_plugin":
                        await HandleReloadPluginAsync(webSocket, data);
                        break;
                    case "unload_plugin":
                        await HandleUnloadPluginAsync(webSocket, data);
                        break;
                    case "get_plugin_config":
                        await HandleGetPluginConfigAsync(webSocket, data);
                        break;
                    case "set_plugin_config":
                        await HandleSetPluginConfigAsync(webSocket, data);
                        break;
                    case "execute_plugin_command":
                        await HandleExecuteCommandAsync(webSocket, data);
                        break;
                    case "get_plugin_commands":
                        await HandleGetPluginCommandsAsync(webSocket, data);
                        break;
                    case "load_plugin_from_file":
                        await HandleLoadPluginFromFileAsync(webSocket, data);
                        break;
                    case "upload_and_load_plugin":
                        await HandleUploadAndLoadPluginAsync(webSocket, data);
                        break;
                    case "get_plugin_readme":
                        await HandleGetPluginReadmeAsync(webSocket, data);
                        break;
                    case "get_plugin_permissions":
                        await HandleGetPluginPermissionsAsync(webSocket, data);
                        break;
                    default:
                        await SendErrorAsync(webSocket, $"未知的插件消息类型: {messageType}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("PLUGIN_WS", $"Failed to process plugin message {messageType}", ex);
                await SendErrorAsync(webSocket, $"Processing failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取所有插件列表
        /// </summary>
        private async Task HandleGetPluginsAsync(WebSocket webSocket)
        {
            Logger.LogInfo("PLUGIN_WS", "Getting plugin list");
            var plugins = _pluginManager.GetAllPluginInfos().Select(p => new
            {
                p.Id,
                p.Name,
                p.Version,
                p.Author,
                p.Description,
                State = p.State.ToString(),
                p.AutoStart,
                p.Priority,
                Dependencies = p.Dependencies,
                p.LoadTime
            }).ToList();

            await SendMessageAsync(webSocket, "plugins_list", new
            {
                Count = plugins.Count,
                Plugins = plugins
            });
            Logger.LogInfo("PLUGIN_WS", $"Sent plugin list, total {plugins.Count} plugins");
        }

        /// <summary>
        /// 启动插件
        /// </summary>
        private async Task HandleStartPluginAsync(WebSocket webSocket, dynamic data)
        {
            string pluginId = data?.pluginId?.ToString();
            if (string.IsNullOrEmpty(pluginId))
            {
                await SendErrorAsync(webSocket, "缺少 pluginId 参数");
                return;
            }

            bool success = _pluginManager.StartPlugin(pluginId);
            var plugin = _pluginManager.GetPluginInfo(pluginId);

            await SendMessageAsync(webSocket, "plugin_started", new
            {
                PluginId = pluginId,
                Success = success,
                State = plugin?.State.ToString(),
                Message = success ? "插件启动成功" : "插件启动失败"
            });
        }

        /// <summary>
        /// 停止插件
        /// </summary>
        private async Task HandleStopPluginAsync(WebSocket webSocket, dynamic data)
        {
            string pluginId = data?.pluginId?.ToString();
            if (string.IsNullOrEmpty(pluginId))
            {
                await SendErrorAsync(webSocket, "缺少 pluginId 参数");
                return;
            }

            bool success = _pluginManager.StopPlugin(pluginId);
            var plugin = _pluginManager.GetPluginInfo(pluginId);

            await SendMessageAsync(webSocket, "plugin_stopped", new
            {
                PluginId = pluginId,
                Success = success,
                State = plugin?.State.ToString(),
                Message = success ? "插件停止成功" : "插件停止失败"
            });
        }

        /// <summary>
        /// 重新加载插件
        /// </summary>
        private async Task HandleReloadPluginAsync(WebSocket webSocket, dynamic data)
        {
            string pluginId = data?.pluginId?.ToString();
            if (string.IsNullOrEmpty(pluginId))
            {
                await SendErrorAsync(webSocket, "缺少 pluginId 参数");
                return;
            }

            bool success = _pluginManager.ReloadPlugin(pluginId);

            await SendMessageAsync(webSocket, "plugin_reloaded", new
            {
                PluginId = pluginId,
                Success = success,
                Message = success ? "插件重新加载成功" : "插件重新加载失败"
            });
        }

        /// <summary>
        /// 卸载插件
        /// </summary>
        private async Task HandleUnloadPluginAsync(WebSocket webSocket, dynamic data)
        {
            string pluginId = data?.pluginId?.ToString();
            if (string.IsNullOrEmpty(pluginId))
            {
                await SendErrorAsync(webSocket, "缺少 pluginId 参数");
                return;
            }

            bool success = _pluginManager.UnloadPlugin(pluginId);

            await SendMessageAsync(webSocket, "plugin_unloaded", new
            {
                PluginId = pluginId,
                Success = success,
                Message = success ? "插件卸载成功" : "插件卸载失败"
            });
        }

        /// <summary>
        /// 获取插件配置
        /// </summary>
        private async Task HandleGetPluginConfigAsync(WebSocket webSocket, dynamic data)
        {
            string pluginId = data?.pluginId?.ToString();
            if (string.IsNullOrEmpty(pluginId))
            {
                await SendErrorAsync(webSocket, "缺少 pluginId 参数");
                return;
            }

            var plugin = _pluginManager.GetPlugin(pluginId);
            if (plugin == null)
            {
                await SendErrorAsync(webSocket, $"插件 {pluginId} 不存在");
                return;
            }

            var config = plugin.GetConfiguration();

            await SendMessageAsync(webSocket, "plugin_config", new
            {
                PluginId = pluginId,
                Configuration = config
            });
        }

        /// <summary>
        /// 设置插件配置
        /// </summary>
        private async Task HandleSetPluginConfigAsync(WebSocket webSocket, dynamic data)
        {
            string pluginId = data?.pluginId?.ToString();
            if (string.IsNullOrEmpty(pluginId))
            {
                await SendErrorAsync(webSocket, "缺少 pluginId 参数");
                return;
            }

            var plugin = _pluginManager.GetPlugin(pluginId);
            if (plugin == null)
            {
                await SendErrorAsync(webSocket, $"插件 {pluginId} 不存在");
                return;
            }

            try
            {
                // 将 dynamic 转换为 Dictionary<string, object>
                var config = new Dictionary<string, object>();
                var configData = data.configuration ?? data.config;
                if (configData != null)
                {
                    foreach (var prop in configData)
                    {
                        string key = prop.Name;
                        object value = prop.Value;
                        config[key] = value;
                    }
                }

                plugin.SetConfiguration(config);

                await SendMessageAsync(webSocket, "plugin_config_updated", new
                {
                    PluginId = pluginId,
                    Success = true,
                    Message = "配置更新成功"
                });
            }
            catch (Exception ex)
            {
                await SendErrorAsync(webSocket, $"配置更新失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 执行插件命令
        /// </summary>
        private async Task HandleExecuteCommandAsync(WebSocket webSocket, dynamic data)
        {
            string pluginId = data?.pluginId?.ToString();
            string command = data?.command?.ToString();

            if (string.IsNullOrEmpty(pluginId) || string.IsNullOrEmpty(command))
            {
                await SendErrorAsync(webSocket, "缺少 pluginId 或 command 参数");
                return;
            }

            try
            {
                // 将 dynamic 转换为 Dictionary<string, object>
                var parameters = new Dictionary<string, object>();
                if (data.parameters != null)
                {
                    foreach (var prop in data.parameters)
                    {
                        string key = prop.Name;
                        object value = prop.Value;
                        parameters[key] = value;
                    }
                }

                var result = _pluginManager.ExecuteCommand(pluginId, command, parameters);

                await SendMessageAsync(webSocket, "plugin_command_result", new
                {
                    PluginId = pluginId,
                    Command = command,
                    Result = result,
                    Success = true
                });
            }
            catch (Exception ex)
            {
                await SendErrorAsync(webSocket, $"命令执行失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取插件命令列表
        /// </summary>
        private async Task HandleGetPluginCommandsAsync(WebSocket webSocket, dynamic data)
        {
            string pluginId = data?.pluginId?.ToString();
            if (string.IsNullOrEmpty(pluginId))
            {
                await SendErrorAsync(webSocket, "缺少 pluginId 参数");
                return;
            }

            var plugin = _pluginManager.GetPlugin(pluginId);
            if (plugin == null)
            {
                await SendErrorAsync(webSocket, $"插件 {pluginId} 不存在");
                return;
            }

            // 通过反射获取插件命令
            var commands = new List<object>();
            var methods = plugin.GetType().GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            foreach (var method in methods)
            {
                var attr = method.GetCustomAttributes(typeof(PluginCommandAttribute), false).FirstOrDefault() as PluginCommandAttribute;
                if (attr != null)
                {
                    commands.Add(new
                    {
                        Name = attr.Name,
                        Description = attr.Description,
                        Usage = attr.Usage
                    });
                }
            }

            await SendMessageAsync(webSocket, "plugin_commands", new
            {
                PluginId = pluginId,
                Commands = commands
            });
        }

        /// <summary>
        /// 从文件加载插件
        /// </summary>
        private async Task HandleLoadPluginFromFileAsync(WebSocket webSocket, dynamic data)
        {
            string filePath = data?.filePath?.ToString();
            if (string.IsNullOrEmpty(filePath))
            {
                await SendErrorAsync(webSocket, "缺少 filePath 参数");
                return;
            }

            string finalPath = filePath;
            string copyMessage = "";

            try
            {
                // 获取插件目录
                string pluginDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
                if (!Directory.Exists(pluginDirectory))
                {
                    Directory.CreateDirectory(pluginDirectory);
                }

                // 如果文件不在插件目录中，则复制过去
                string fileName = Path.GetFileName(filePath);
                string targetPath = Path.Combine(pluginDirectory, fileName);

                if (!filePath.Equals(targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(filePath))
                    {
                        File.Copy(filePath, targetPath, true);
                        finalPath = targetPath;
                        copyMessage = "Auto-copied to plugin directory";
                        Logger.LogInfo("PLUGIN_WS", $"Plugin file copied: {filePath} -> {targetPath}");
                    }
                    else
                    {
                        await SendErrorAsync(webSocket, $"Source file does not exist: {filePath}");
                        return;
                    }
                }

                bool success = _pluginManager.LoadPlugin(finalPath);

                await SendMessageAsync(webSocket, "plugin_loaded_from_file", new
                {
                    FilePath = filePath,
                    TargetPath = finalPath,
                    Success = success,
                    Message = success ? $"Plugin loaded successfully{(!string.IsNullOrEmpty(copyMessage) ? ", " + copyMessage : "")}" : "Plugin load failed"
                });
            }
            catch (Exception ex)
            {
                Logger.LogError("PLUGIN_WS", $"Failed to load plugin: {ex.Message}", ex);
                await SendErrorAsync(webSocket, $"Failed to load plugin: {ex.Message}");
            }
        }

        /// <summary>
        /// 上传并加载插件
        /// </summary>
        private async Task HandleUploadAndLoadPluginAsync(WebSocket webSocket, dynamic data)
        {
            string fileName = data?.fileName?.ToString();
            string fileContent = data?.fileContent?.ToString();

            if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(fileContent))
            {
                await SendErrorAsync(webSocket, "缺少 fileName 或 fileContent 参数");
                return;
            }

            try
            {
                // Get plugin directory
                string pluginDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
                if (!Directory.Exists(pluginDirectory))
                {
                    Directory.CreateDirectory(pluginDirectory);
                }

                // Save file to plugin directory
                string targetPath = Path.Combine(pluginDirectory, fileName);
                byte[] fileBytes = Convert.FromBase64String(fileContent);
                File.WriteAllBytes(targetPath, fileBytes);
                Logger.LogInfo("PLUGIN_WS", $"Plugin file saved: {targetPath}");

                // Load plugin
                bool success = _pluginManager.LoadPlugin(targetPath);

                await SendMessageAsync(webSocket, "plugin_loaded_from_file", new
                {
                    FilePath = fileName,
                    TargetPath = targetPath,
                    Success = success,
                    Message = success ? $"Plugin {fileName} uploaded and loaded successfully" : "Plugin load failed"
                });
            }
            catch (Exception ex)
            {
                Logger.LogError("PLUGIN_WS", $"Failed to upload and load plugin: {ex.Message}", ex);
                await SendErrorAsync(webSocket, $"Failed to upload and load plugin: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取插件自述文档
        /// </summary>
        private async Task HandleGetPluginReadmeAsync(WebSocket webSocket, dynamic data)
        {
            string pluginId = data?.pluginId?.ToString();
            if (string.IsNullOrEmpty(pluginId))
            {
                await SendErrorAsync(webSocket, "缺少 pluginId 参数");
                return;
            }

            var plugin = _pluginManager.GetPlugin(pluginId);
            if (plugin == null)
            {
                await SendErrorAsync(webSocket, $"插件 {pluginId} 不存在");
                return;
            }

            try
            {
                string readme = plugin.GetReadme();
                await SendMessageAsync(webSocket, "plugin_readme", new
                {
                    PluginId = pluginId,
                    Readme = readme
                });
            }
            catch (Exception ex)
            {
                await SendErrorAsync(webSocket, $"获取自述文档失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取插件权限列表
        /// </summary>
        private async Task HandleGetPluginPermissionsAsync(WebSocket webSocket, dynamic data)
        {
            string pluginId = data?.pluginId?.ToString();
            if (string.IsNullOrEmpty(pluginId))
            {
                await SendErrorAsync(webSocket, "缺少 pluginId 参数");
                return;
            }

            var plugin = _pluginManager.GetPlugin(pluginId);
            if (plugin == null)
            {
                await SendErrorAsync(webSocket, $"插件 {pluginId} 不存在");
                return;
            }

            try
            {
                // 尝试调用新的 GetPermissionsInfo 方法
                PluginPermissionsInfo permInfo = null;
                if (plugin is PluginBase pluginBase)
                {
                    permInfo = pluginBase.GetPermissionsInfo();
                }

                // 如果插件没有实现 GetPermissionsInfo，回退到 GetPermissions
                if (permInfo == null)
                {
                    var permissions = plugin.GetPermissions();
                    permInfo = new PluginPermissionsInfo
                    {
                        SystemPermissions = permissions ?? new List<string>(),
                        DeclaredPermissions = new List<string>()
                    };
                }

                await SendMessageAsync(webSocket, "plugin_permissions", new
                {
                    PluginId = pluginId,
                    SystemPermissions = permInfo.SystemPermissions,
                    DeclaredPermissions = permInfo.DeclaredPermissions
                });
            }
            catch (Exception ex)
            {
                await SendErrorAsync(webSocket, $"获取权限列表失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 发送消息
        /// </summary>
        private async Task SendMessageAsync(WebSocket webSocket, string type, object data)
        {
            var message = new PluginWebSocketMessage { Type = type, Data = data };
            var json = JsonConvert.SerializeObject(message);
            var bytes = Encoding.UTF8.GetBytes(json);
            await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        /// <summary>
        /// 发送错误消息
        /// </summary>
        private async Task SendErrorAsync(WebSocket webSocket, string errorMessage)
        {
            await SendMessageAsync(webSocket, "plugin_error", new { Message = errorMessage });
        }
    }

    /// <summary>
    /// 插件WebSocket消息结构
    /// </summary>
    public class PluginWebSocketMessage
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("data")]
        public object Data { get; set; }
    }
}
