using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AI_Chat.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace AI_Chat.Plugins.Virtualization
{
    public class VirtualizationWebSocketHandler
    {
        private readonly PluginVirtualizationManager _virtualizationManager;
        private readonly PluginManager _pluginManager;
        private readonly JsonSerializerSettings _jsonSettings;

        public VirtualizationWebSocketHandler(PluginVirtualizationManager virtualizationManager, PluginManager pluginManager)
        {
            _virtualizationManager = virtualizationManager;
            _pluginManager = pluginManager;
            _jsonSettings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                NullValueHandling = NullValueHandling.Ignore
            };
        }

        public async Task HandleMessageAsync(WebSocket webSocket, string messageType, dynamic data)
        {
            try
            {
                switch (messageType)
                {
                    case "get_virtualization_data":
                        await SendVirtualizationDataAsync(webSocket);
                        break;

                    case "get_plugin_virtualization_data":
                        string pluginId = data?.pluginId?.ToString();
                        await SendPluginVirtualizationDataAsync(webSocket, pluginId);
                        break;

                    case "get_virtual_registry":
                        await SendVirtualRegistryAsync(webSocket, data?.pluginId?.ToString());
                        break;

                    case "get_virtual_files":
                        await SendVirtualFilesAsync(webSocket, data?.pluginId?.ToString());
                        break;

                    case "get_virtualization_stats":
                        await SendVirtualizationStatsAsync(webSocket, data?.pluginId?.ToString());
                        break;

                    case "clear_plugin_virtualization":
                        await ClearPluginVirtualizationAsync(webSocket, data?.pluginId?.ToString());
                        break;

                    case "toggle_virtualization":
                        bool enabled = false;
                        if (data?.enabled != null)
                        {
                            bool.TryParse(data.enabled.ToString(), out enabled);
                        }
                        await ToggleVirtualizationAsync(webSocket, data?.pluginId?.ToString(), enabled);
                        break;

                    case "delete_virtual_registry_key":
                        await DeleteVirtualRegistryKeyAsync(webSocket, data?.pluginId?.ToString(), data?.keyPath?.ToString());
                        break;

                    case "delete_virtual_file":
                        await DeleteVirtualFileAsync(webSocket, data?.pluginId?.ToString(), data?.virtualPath?.ToString());
                        break;

                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                await SendErrorAsync(webSocket, $"Error handling virtualization message: {ex.Message}");
            }
        }

        private async Task SendVirtualizationDataAsync(WebSocket webSocket)
        {
            var allData = _virtualizationManager.GetAllPluginVirtualizationData();
            var message = new WebSocketMessage
            {
                Type = "virtualization_data",
                Data = new
                {
                    plugins = allData,
                    config = new
                    {
                        enableRegistryVirtualization = _virtualizationManager.Config.EnableRegistryVirtualization,
                        enableFileVirtualization = _virtualizationManager.Config.EnableFileVirtualization,
                        enableProcessInterception = _virtualizationManager.Config.EnableProcessInterception,
                        blockExeWrites = _virtualizationManager.Config.BlockExeWrites
                    }
                }
            };
            await SendAsync(webSocket, message);
        }

        private async Task SendPluginVirtualizationDataAsync(WebSocket webSocket, string pluginId)
        {
            if (string.IsNullOrEmpty(pluginId))
            {
                await SendErrorAsync(webSocket, "Plugin ID is required");
                return;
            }

            var data = _virtualizationManager.GetPluginVirtualizationData(pluginId);
            var message = new WebSocketMessage
            {
                Type = "plugin_virtualization_data",
                Data = data
            };
            await SendAsync(webSocket, message);
        }

        private async Task SendVirtualRegistryAsync(WebSocket webSocket, string pluginId)
        {
            List<VirtualRegistryEntry> entries;
            if (string.IsNullOrEmpty(pluginId))
            {
                var allEntries = _virtualizationManager.GetAllVirtualRegistryEntries();
                entries = new List<VirtualRegistryEntry>();
                foreach (var kvp in allEntries)
                {
                    entries.AddRange(kvp.Value);
                }
            }
            else
            {
                entries = _virtualizationManager.GetVirtualRegistryEntries(pluginId);
            }

            var message = new WebSocketMessage
            {
                Type = "virtual_registry",
                Data = new
                {
                    pluginId = pluginId,
                    entries = entries
                }
            };
            await SendAsync(webSocket, message);
        }

        private async Task SendVirtualFilesAsync(WebSocket webSocket, string pluginId)
        {
            List<VirtualFileEntry> entries;
            if (string.IsNullOrEmpty(pluginId))
            {
                var allEntries = _virtualizationManager.GetAllVirtualFileEntries();
                entries = new List<VirtualFileEntry>();
                foreach (var kvp in allEntries)
                {
                    entries.AddRange(kvp.Value);
                }
            }
            else
            {
                entries = _virtualizationManager.GetVirtualFileEntries(pluginId);
            }

            var message = new WebSocketMessage
            {
                Type = "virtual_files",
                Data = new
                {
                    pluginId = pluginId,
                    entries = entries
                }
            };
            await SendAsync(webSocket, message);
        }

        private async Task SendVirtualizationStatsAsync(WebSocket webSocket, string pluginId)
        {
            Dictionary<string, VirtualizationStatistics> stats;
            if (string.IsNullOrEmpty(pluginId))
            {
                stats = _virtualizationManager.GetAllStatistics();
            }
            else
            {
                stats = new Dictionary<string, VirtualizationStatistics>
                {
                    { pluginId, _virtualizationManager.GetStatistics(pluginId) }
                };
            }

            var message = new WebSocketMessage
            {
                Type = "virtualization_stats",
                Data = stats
            };
            await SendAsync(webSocket, message);
        }

        private async Task ClearPluginVirtualizationAsync(WebSocket webSocket, string pluginId)
        {
            if (string.IsNullOrEmpty(pluginId))
            {
                await SendErrorAsync(webSocket, "Plugin ID is required");
                return;
            }

            _virtualizationManager.ClearPluginData(pluginId);

            var message = new WebSocketMessage
            {
                Type = "virtualization_cleared",
                Data = new { pluginId = pluginId, success = true }
            };
            await SendAsync(webSocket, message);
        }

        private async Task ToggleVirtualizationAsync(WebSocket webSocket, string pluginId, bool enabled)
        {
            if (string.IsNullOrEmpty(pluginId))
            {
                await SendErrorAsync(webSocket, "Plugin ID is required");
                return;
            }

            // 检查插件是否支持沙箱
            if (enabled)
            {
                var pluginInfo = _pluginManager.GetPluginInfo(pluginId);
                if (pluginInfo != null && !pluginInfo.SupportSandbox)
                {
                    await SendErrorAsync(webSocket, "This plugin does not support sandbox execution");
                    return;
                }
                
                _virtualizationManager.EnableVirtualization(pluginId);
            }
            else
            {
                _virtualizationManager.DisableVirtualization(pluginId);
            }

            var message = new WebSocketMessage
            {
                Type = "virtualization_toggled",
                Data = new { pluginId = pluginId, enabled = enabled }
            };
            await SendAsync(webSocket, message);
        }

        private async Task DeleteVirtualRegistryKeyAsync(WebSocket webSocket, string pluginId, string keyPath)
        {
            if (string.IsNullOrEmpty(pluginId) || string.IsNullOrEmpty(keyPath))
            {
                await SendErrorAsync(webSocket, "Plugin ID and key path are required");
                return;
            }

            var result = _virtualizationManager.DeleteRegistryKey(pluginId, keyPath);

            var message = new WebSocketMessage
            {
                Type = "virtual_registry_deleted",
                Data = new { pluginId = pluginId, keyPath = keyPath, success = result.Allowed }
            };
            await SendAsync(webSocket, message);
        }

        private async Task DeleteVirtualFileAsync(WebSocket webSocket, string pluginId, string virtualPath)
        {
            if (string.IsNullOrEmpty(pluginId) || string.IsNullOrEmpty(virtualPath))
            {
                await SendErrorAsync(webSocket, "Plugin ID and virtual path are required");
                return;
            }

            var result = _virtualizationManager.CheckFileDelete(pluginId, virtualPath);

            var message = new WebSocketMessage
            {
                Type = "virtual_file_deleted",
                Data = new { pluginId = pluginId, virtualPath = virtualPath, success = result.Allowed }
            };
            await SendAsync(webSocket, message);
        }

        private async Task SendAsync(WebSocket webSocket, WebSocketMessage message)
        {
            if (webSocket.State == WebSocketState.Open)
            {
                var json = JsonConvert.SerializeObject(message, _jsonSettings);
                var buffer = Encoding.UTF8.GetBytes(json);
                await webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }

        private async Task SendErrorAsync(WebSocket webSocket, string errorMessage)
        {
            var message = new WebSocketMessage
            {
                Type = "virtualization_error",
                Data = new { error = errorMessage }
            };
            await SendAsync(webSocket, message);
        }
    }
}
