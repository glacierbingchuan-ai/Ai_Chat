using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AI_Chat.Models;
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
        public async Task HandleMessageAsync(WebSocket webSocket, string messageType, dynamic data, string replyTo = null)
        {
            Logger.LogInfo("PLUGIN_WS", $"Processing message: {messageType}, replyTo: {replyTo}");
            try
            {
                switch (messageType)
                {
                    case "get_plugins":
                        await HandleGetPluginsAsync(webSocket, replyTo);
                        break;
                    case "start_plugin":
                        await HandleStartPluginAsync(webSocket, data, replyTo);
                        break;
                    case "stop_plugin":
                        await HandleStopPluginAsync(webSocket, data, replyTo);
                        break;
                    case "reload_plugin":
                        await HandleReloadPluginAsync(webSocket, data, replyTo);
                        break;
                    case "unload_plugin":
                        await HandleUnloadPluginAsync(webSocket, data, replyTo);
                        break;
                    case "get_plugin_config":
                        await HandleGetPluginConfigAsync(webSocket, data, replyTo);
                        break;
                    case "set_plugin_config":
                        await HandleSetPluginConfigAsync(webSocket, data, replyTo);
                        break;
                    case "execute_plugin_command":
                        await HandleExecuteCommandAsync(webSocket, data, replyTo);
                        break;
                    case "get_plugin_commands":
                        await HandleGetPluginCommandsAsync(webSocket, data, replyTo);
                        break;
                    case "load_plugin_from_file":
                        await HandleLoadPluginFromFileAsync(webSocket, data, replyTo);
                        break;
                    case "upload_and_load_plugin":
                        await HandleUploadAndLoadPluginAsync(webSocket, data, replyTo);
                        break;
                    case "get_plugin_readme":
                        await HandleGetPluginReadmeAsync(webSocket, data, replyTo);
                        break;
                    case "get_plugin_permissions":
                        await HandleGetPluginPermissionsAsync(webSocket, data, replyTo);
                        break;
                    default:
                        await SendErrorAsync(webSocket, $"未知的插件消息类型: {messageType}", replyTo);
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
        private async Task HandleGetPluginsAsync(WebSocket webSocket, string replyTo = null)
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
                p.LoadTime
            }).ToList();

            await SendMessageAsync(webSocket, "plugins_list", new
            {
                Count = plugins.Count,
                Plugins = plugins
            }, replyTo);
            Logger.LogInfo("PLUGIN_WS", $"Sent plugin list, total {plugins.Count} plugins");
        }

        /// <summary>
        /// 启动插件
        /// </summary>
        private async Task HandleStartPluginAsync(WebSocket webSocket, dynamic data, string replyTo = null)
        {
            string pluginId = data?.pluginId?.ToString();
            if (string.IsNullOrEmpty(pluginId))
            {
                await SendErrorAsync(webSocket, "缺少 pluginId 参数", replyTo);
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
            }, replyTo);
        }

        /// <summary>
        /// 停止插件
        /// </summary>
        private async Task HandleStopPluginAsync(WebSocket webSocket, dynamic data, string replyTo = null)
        {
            string pluginId = data?.pluginId?.ToString();
            if (string.IsNullOrEmpty(pluginId))
            {
                await SendErrorAsync(webSocket, "缺少 pluginId 参数", replyTo);
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
            }, replyTo);
        }

        /// <summary>
        /// 重新加载插件
        /// </summary>
        private async Task HandleReloadPluginAsync(WebSocket webSocket, dynamic data, string replyTo = null)
        {
            string pluginId = data?.pluginId?.ToString();
            if (string.IsNullOrEmpty(pluginId))
            {
                await SendErrorAsync(webSocket, "缺少 pluginId 参数", replyTo);
                return;
            }

            bool success = _pluginManager.ReloadPlugin(pluginId);

            await SendMessageAsync(webSocket, "plugin_reloaded", new
            {
                PluginId = pluginId,
                Success = success,
                Message = success ? "插件重新加载成功" : "插件重新加载失败"
            }, replyTo);
        }

        /// <summary>
        /// 卸载插件
        /// </summary>
        private async Task HandleUnloadPluginAsync(WebSocket webSocket, dynamic data, string replyTo = null)
        {
            string pluginId = data?.pluginId?.ToString();
            if (string.IsNullOrEmpty(pluginId))
            {
                await SendErrorAsync(webSocket, "缺少 pluginId 参数", replyTo);
                return;
            }

            bool success = _pluginManager.UnloadPlugin(pluginId);

            await SendMessageAsync(webSocket, "plugin_unloaded", new
            {
                PluginId = pluginId,
                Success = success,
                Message = success ? "插件卸载成功" : "插件卸载失败"
            }, replyTo);
        }

        /// <summary>
        /// 获取插件配置
        /// </summary>
        private async Task HandleGetPluginConfigAsync(WebSocket webSocket, dynamic data, string replyTo = null)
        {
            string pluginId = data?.pluginId?.ToString();
            if (string.IsNullOrEmpty(pluginId))
            {
                await SendErrorAsync(webSocket, "缺少 pluginId 参数", replyTo);
                return;
            }

            var plugin = _pluginManager.GetPlugin(pluginId);
            if (plugin == null)
            {
                await SendErrorAsync(webSocket, $"插件 {pluginId} 不存在", replyTo);
                return;
            }

            // 通过 PluginManager 获取插件实例，然后访问 Data 获取配置
            var pluginInstance = _pluginManager.GetPluginInstance(pluginId);
            var config = pluginInstance?.Data?.GetAll() ?? new Dictionary<string, object>();

            await SendMessageAsync(webSocket, "plugin_config", new
            {
                PluginId = pluginId,
                Configuration = config
            }, replyTo);
        }

        /// <summary>
        /// 设置插件配置
        /// </summary>
        private async Task HandleSetPluginConfigAsync(WebSocket webSocket, dynamic data, string replyTo = null)
        {
            string pluginId = data?.pluginId?.ToString();
            if (string.IsNullOrEmpty(pluginId))
            {
                await SendErrorAsync(webSocket, "缺少 pluginId 参数", replyTo);
                return;
            }

            var plugin = _pluginManager.GetPlugin(pluginId);
            if (plugin == null)
            {
                await SendErrorAsync(webSocket, $"插件 {pluginId} 不存在", replyTo);
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

                // 优先尝试调用插件的 SetConfig 命令，让插件自己处理配置更新
                // 这样可以确保插件的字段变量与配置保持同步
                try
                {
                    var result = _pluginManager.ExecuteCommand(pluginId, "SetConfig", config);
                    if (result != null)
                    {
                        // 检查命令执行结果
                        var resultDict = result as Dictionary<string, object>;
                        if (resultDict != null && resultDict.TryGetValue("Success", out var successObj))
                        {
                            bool success = Convert.ToBoolean(successObj);
                            if (!success)
                            {
                                string errorMessage = resultDict.TryGetValue("Error", out var errorObj) 
                                    ? errorObj?.ToString() 
                                    : "配置更新失败";
                                await SendErrorAsync(webSocket, errorMessage, replyTo);
                                return;
                            }
                        }
                    }
                }
                catch (NotSupportedException)
                {
                    // 插件没有实现 SetConfig 命令，回退到直接操作 Data
                    var pluginInstance = _pluginManager.GetPluginInstance(pluginId);
                    if (pluginInstance?.Data != null)
                    {
                        pluginInstance.Data.SetAll(config);
                        pluginInstance.Data.SaveConfig();
                    }
                }

                await SendMessageAsync(webSocket, "plugin_config_updated", new
                {
                    PluginId = pluginId,
                    Success = true,
                    Message = "Configuration updated successfully"
                }, replyTo);
            }
            catch (Exception ex)
            {
                await SendErrorAsync(webSocket, $"Configuration update failed: {ex.Message}", replyTo);
            }
        }

        /// <summary>
        /// 执行插件命令
        /// </summary>
        private async Task HandleExecuteCommandAsync(WebSocket webSocket, dynamic data, string replyTo = null)
        {
            string pluginId = data?.pluginId?.ToString();
            string command = data?.command?.ToString();

            if (string.IsNullOrEmpty(pluginId) || string.IsNullOrEmpty(command))
            {
                await SendErrorAsync(webSocket, "缺少 pluginId 或 command 参数", replyTo);
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
                }, replyTo);
            }
            catch (Exception ex)
            {
                await SendErrorAsync(webSocket, $"命令执行失败: {ex.Message}", replyTo);
            }
        }

        /// <summary>
        /// 获取插件命令列表
        /// </summary>
        private async Task HandleGetPluginCommandsAsync(WebSocket webSocket, dynamic data, string replyTo = null)
        {
            string pluginId = data?.pluginId?.ToString();
            if (string.IsNullOrEmpty(pluginId))
            {
                await SendErrorAsync(webSocket, "缺少 pluginId 参数", replyTo);
                return;
            }

            var plugin = _pluginManager.GetPlugin(pluginId);
            if (plugin == null)
            {
                await SendErrorAsync(webSocket, $"插件 {pluginId} 不存在", replyTo);
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
            }, replyTo);
        }

        /// <summary>
        /// 从文件加载插件
        /// </summary>
        private async Task HandleLoadPluginFromFileAsync(WebSocket webSocket, dynamic data, string replyTo = null)
        {
            string filePath = data?.filePath?.ToString();
            if (string.IsNullOrEmpty(filePath))
            {
                await SendErrorAsync(webSocket, "缺少 filePath 参数", replyTo);
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

                var pluginInfo = _pluginManager.DiscoverPlugin(finalPath);
                bool success = false;
                
                if (pluginInfo != null)
                {
                    success = _pluginManager.InitializeDiscoveredPlugin(pluginInfo);
                }

                await SendMessageAsync(webSocket, "plugin_loaded_from_file", new
                {
                    FilePath = filePath,
                    TargetPath = finalPath,
                    Success = success,
                    Message = success ? $"Plugin loaded successfully{(!string.IsNullOrEmpty(copyMessage) ? ", " + copyMessage : "")}" : "Plugin load failed"
                }, replyTo);
            }
            catch (Exception ex)
            {
                Logger.LogError("PLUGIN_WS", $"Failed to load plugin: {ex.Message}", ex);
                await SendErrorAsync(webSocket, $"Failed to load plugin: {ex.Message}", replyTo);
            }
        }

        /// <summary>
        /// 上传并加载插件
        /// </summary>
        private async Task HandleUploadAndLoadPluginAsync(WebSocket webSocket, dynamic data, string replyTo = null)
        {
            string fileName = data?.fileName?.ToString();
            string fileContent = data?.fileContent?.ToString();

            if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(fileContent))
            {
                await SendErrorAsync(webSocket, "缺少 fileName 或 fileContent 参数", replyTo);
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

                // Load plugin - discover first, then initialize with virtualization
                var pluginInfo = _pluginManager.DiscoverPlugin(targetPath);
                bool success = false;
                
                if (pluginInfo != null)
                {
                    success = _pluginManager.InitializeDiscoveredPlugin(pluginInfo);
                }

                await SendMessageAsync(webSocket, "plugin_loaded_from_file", new
                {
                    FilePath = fileName,
                    TargetPath = targetPath,
                    Success = success,
                    Message = success ? $"Plugin {fileName} uploaded and loaded successfully" : "Plugin load failed"
                }, replyTo);
            }
            catch (Exception ex)
            {
                Logger.LogError("PLUGIN_WS", $"Failed to upload and load plugin: {ex.Message}", ex);
                await SendErrorAsync(webSocket, $"Failed to upload and load plugin: {ex.Message}", replyTo);
            }
        }

        /// <summary>
        /// 获取插件自述文档
        /// </summary>
        private async Task HandleGetPluginReadmeAsync(WebSocket webSocket, dynamic data, string replyTo = null)
        {
            string pluginId = data?.pluginId?.ToString();
            if (string.IsNullOrEmpty(pluginId))
            {
                await SendErrorAsync(webSocket, "缺少 pluginId 参数", replyTo);
                return;
            }

            var plugin = _pluginManager.GetPlugin(pluginId);
            if (plugin == null)
            {
                await SendErrorAsync(webSocket, $"插件 {pluginId} 不存在", replyTo);
                return;
            }

            try
            {
                string readme = plugin.GetReadme();
                await SendMessageAsync(webSocket, "plugin_readme", new
                {
                    PluginId = pluginId,
                    Readme = readme
                }, replyTo);
            }
            catch (Exception ex)
            {
                await SendErrorAsync(webSocket, $"获取自述文档失败: {ex.Message}", replyTo);
            }
        }

        /// <summary>
        /// 获取插件权限列表
        /// </summary>
        private async Task HandleGetPluginPermissionsAsync(WebSocket webSocket, dynamic data, string replyTo = null)
        {
            string pluginId = data?.pluginId?.ToString();
            if (string.IsNullOrEmpty(pluginId))
            {
                await SendErrorAsync(webSocket, "缺少 pluginId 参数", replyTo);
                return;
            }

            var plugin = _pluginManager.GetPlugin(pluginId);
            if (plugin == null)
            {
                await SendErrorAsync(webSocket, $"插件 {pluginId} 不存在", replyTo);
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
                }, replyTo);
            }
            catch (Exception ex)
            {
                await SendErrorAsync(webSocket, $"获取权限列表失败: {ex.Message}", replyTo);
            }
        }

        /// <summary>
        /// 发送消息
        /// </summary>
        private async Task SendMessageAsync(WebSocket webSocket, string type, object data, string replyTo = null)
        {
            var message = new WebSocketMessage { Type = type, Data = data, ReplyTo = replyTo };
            var json = JsonConvert.SerializeObject(message);
            var bytes = Encoding.UTF8.GetBytes(json);
            await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        /// <summary>
        /// 发送错误消息
        /// </summary>
        private async Task SendErrorAsync(WebSocket webSocket, string errorMessage, string replyTo = null)
        {
            await SendMessageAsync(webSocket, "plugin_error", new { Message = errorMessage }, replyTo);
        }
    }
}
