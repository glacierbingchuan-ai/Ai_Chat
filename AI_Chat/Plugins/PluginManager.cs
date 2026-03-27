using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AI_Chat.Services;
using AI_Chat.Models;
using Newtonsoft.Json;

namespace AI_Chat.Plugins
{
    public class PluginManager : IPluginManager, IDisposable
    {
        private readonly Dictionary<string, PluginInstance> _plugins;
        private readonly PluginLoader _loader;
        private readonly PluginServiceProvider _serviceProvider;
        private readonly PluginLogger _logger;
        private readonly string _pluginDirectory;
        private readonly string _dataDirectory;
        private readonly string _configDirectory;
        private readonly string _pluginStateFile;
        private readonly ConfigManager _configManager;
        private IPluginApi _pluginApi;
        private readonly object _lock = new object();
        private Dictionary<string, PluginStateConfig> _pluginStates;

        public string PluginDirectory => _pluginDirectory;
        public string DataDirectory => _dataDirectory;
        public string ConfigDirectory => _configDirectory;
        public IServiceProvider ServiceProvider => _serviceProvider;
        public IPluginApi PluginApi => _pluginApi;

        public PluginManager(ConfigManager configManager, IPluginApi pluginApi = null, string baseDirectory = null)
        {
            _configManager = configManager;
            _pluginApi = pluginApi;
            _plugins = new Dictionary<string, PluginInstance>(StringComparer.OrdinalIgnoreCase);
            _loader = new PluginLoader();
            _serviceProvider = new PluginServiceProvider();
            _logger = new PluginLogger();
            _pluginStates = new Dictionary<string, PluginStateConfig>(StringComparer.OrdinalIgnoreCase);

            baseDirectory = baseDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
            _pluginDirectory = Path.Combine(baseDirectory, "Plugins");
            _dataDirectory = Path.Combine(baseDirectory, "PluginData");
            _configDirectory = Path.Combine(baseDirectory, "PluginConfigs");
            _pluginStateFile = Path.Combine(baseDirectory, "PluginStates.json");

            EnsureDirectories();
            LoadPluginStates();
        }

        public void SetPluginApi(IPluginApi pluginApi)
        {
            _pluginApi = pluginApi;
            if (_pluginApi != null)
            {
                _serviceProvider.RegisterService<IPluginApi>(_pluginApi);
                _logger.Info("PluginManager", "IPluginApi registered to service container");
            }
        }

        public void Initialize()
        {
            _logger.Info("PluginManager", "Plugin manager initializing...");

            _serviceProvider.RegisterService<IPluginManager>(this);
            _serviceProvider.RegisterService<ConfigManager>(_configManager);

            if (_pluginApi != null)
            {
                _serviceProvider.RegisterService<IPluginApi>(_pluginApi);
                _logger.Info("PluginManager", "IPluginApi registered to service container");
            }

            _logger.Info("PluginManager", "Plugin manager initialization completed");
        }

        private void LoadPluginStates()
        {
            try
            {
                if (File.Exists(_pluginStateFile))
                {
                    var json = File.ReadAllText(_pluginStateFile);
                    _pluginStates = JsonConvert.DeserializeObject<Dictionary<string, PluginStateConfig>>(json)
                        ?? new Dictionary<string, PluginStateConfig>(StringComparer.OrdinalIgnoreCase);
                    _logger.Info("PluginManager", $"Loaded {_pluginStates.Count} plugin state configurations");
                }
            }
            catch (Exception ex)
            {
                _logger.Error("PluginManager", $"Failed to load plugin state configuration: {ex.Message}");
                _pluginStates = new Dictionary<string, PluginStateConfig>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private void SavePluginStates()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_pluginStates, Formatting.Indented);
                File.WriteAllText(_pluginStateFile, json);
            }
            catch (Exception ex)
            {
                _logger.Error("PluginManager", $"Failed to save plugin state configuration: {ex.Message}");
            }
        }

        private string GetPluginIdHash(string pluginId)
        {
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(pluginId));
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        private PluginStateConfig GetPluginStateConfig(string pluginId)
        {
            var hashKey = GetPluginIdHash(pluginId);
            if (!_pluginStates.TryGetValue(hashKey, out var config))
            {
                config = new PluginStateConfig { IsEnabled = true, PluginId = pluginId };
                _pluginStates[hashKey] = config;
            }
            return config;
        }

        public bool EnablePlugin(string pluginId)
        {
            lock (_lock)
            {
                var hashKey = GetPluginIdHash(pluginId);
                var stateConfig = GetPluginStateConfig(pluginId);
                stateConfig.IsEnabled = true;
                _pluginStates[hashKey] = stateConfig;
                SavePluginStates();

                if (_plugins.TryGetValue(pluginId, out var instance))
                {
                    instance.Info.IsEnabled = true;
                    
                    if (instance.Plugin != null && instance.Info.State != PluginState.Running)
                    {
                        return DoStartPlugin(instance);
                    }
                    
                    if (instance.Plugin == null)
                    {
                        return LoadAndStartPluginFromFile(pluginId);
                    }
                    
                    return true;
                }
                
                return LoadAndStartPluginFromFile(pluginId);
            }
        }

        private bool LoadAndStartPluginFromFile(string pluginId)
        {
            _logger.Info("PluginManager", $"Loading plugin {pluginId} from directory: {_pluginDirectory}");
            
            if (!Directory.Exists(_pluginDirectory))
            {
                _logger.Warning("PluginManager", "Plugin directory does not exist");
                return false;
            }

            var pluginFiles = Directory.GetFiles(_pluginDirectory, "*.dll");
            foreach (var file in pluginFiles)
            {
                try
                {
                    var result = _loader.LoadPlugin(file);
                    if (result.Success && result.PluginInfo.Id.Equals(pluginId, StringComparison.OrdinalIgnoreCase))
                    {
                        result.PluginInfo.IsEnabled = true;
                        
                        _plugins.Remove(pluginId);
                        
                        LoadAndInitializePlugin(result.PluginInfo);
                        
                        if (_plugins.TryGetValue(pluginId, out var newInstance) && 
                            newInstance.Info.State != PluginState.Running)
                        {
                            return DoStartPlugin(newInstance);
                        }
                        
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error("PluginManager", $"Failed to load plugin file: {file}, {ex.Message}");
                }
            }

            _logger.Warning("PluginManager", $"Plugin {pluginId} not found in directory");
            return false;
        }

        private bool DoStartPlugin(PluginInstance instance)
        {
            if (instance.Plugin == null)
            {
                _logger.Warning("PluginManager", $"Plugin {instance.Info.Id} instance is null");
                return false;
            }

            if (instance.Info.State == PluginState.Running)
            {
                return true;
            }

            try
            {
                instance.Plugin.Start();
                instance.Info.State = PluginState.Running;

                return true;
            }
            catch (Exception ex)
            {
                instance.Info.State = PluginState.Error;
                instance.Info.ErrorMessage = ex.Message;
                _logger.Error("PluginManager", $"Failed to start plugin {instance.Info.Id}", ex);
                return false;
            }
        }

        public bool DisablePlugin(string pluginId)
        {
            lock (_lock)
            {
                var hashKey = GetPluginIdHash(pluginId);
                var stateConfig = GetPluginStateConfig(pluginId);
                stateConfig.IsEnabled = false;
                _pluginStates[hashKey] = stateConfig;
                SavePluginStates();

                if (_plugins.TryGetValue(pluginId, out var instance))
                {
                    instance.Info.IsEnabled = false;
                    
                    StopAndDisposePlugin(instance);
                    
                    UnregisterPluginHandlers(pluginId, instance.Info.Name);
                    
                    instance.Plugin = null;
                    instance.Context = null;
                    instance.Info.State = PluginState.Stopped;
                }

                _logger.Info("PluginManager", $"Plugin {pluginId} disabled");
                return true;
            }
        }

        private List<PluginInfo> _discoveredPlugins = new List<PluginInfo>();

        public void LoadAllPlugins()
        {
            _logger.Info("PluginManager", $"Loading plugins from directory: {_pluginDirectory}");

            if (!Directory.Exists(_pluginDirectory))
            {
                _logger.Warning("PluginManager", "Plugin directory does not exist");
                return;
            }

            var results = _loader.LoadPluginsFromDirectory(_pluginDirectory);
            _discoveredPlugins.Clear();

            foreach (var result in results)
            {
                if (result.Success)
                {
                    var stateConfig = GetPluginStateConfig(result.PluginInfo.Id);
                    result.PluginInfo.IsEnabled = stateConfig.IsEnabled;
                    
                    if (!stateConfig.IsEnabled)
                    {
                        AddDisabledPluginToList(result.PluginInfo);
                        _logger.Info("PluginManager", $"Plugin {result.PluginInfo.Name} is disabled, added to list only");
                    }
                    else
                    {
                        _discoveredPlugins.Add(result.PluginInfo);
                        _logger.Info("PluginManager", $"Discovered plugin: {result.PluginInfo.Name} v{result.PluginInfo.Version}");
                    }
                }
                else
                {
                    _logger.Warning("PluginManager", $"Failed to load plugin: {result.ErrorMessage}");
                }
            }

            _logger.Info("PluginManager", $"Discovered {_discoveredPlugins.Count} enabled plugins");
        }

        public void InitializeAllPlugins()
        {
            var sortedPlugins = _discoveredPlugins.OrderBy(p => p.Priority).ToList();

            foreach (var pluginInfo in sortedPlugins)
            {
                try
                {
                    LoadAndInitializePlugin(pluginInfo);
                }
                catch (Exception ex)
                {
                    _logger.Error("PluginManager", $"Failed to initialize plugin {pluginInfo.Name}", ex);
                }
            }

            _logger.Info("PluginManager", $"Total {_plugins.Count} plugins loaded (including disabled)");
        }

        private void AddDisabledPluginToList(PluginInfo pluginInfo)
        {
            lock (_lock)
            {
                if (_plugins.ContainsKey(pluginInfo.Id))
                {
                    return;
                }

                var instance = new PluginInstance
                {
                    Info = pluginInfo,
                    Plugin = null,
                    Context = null
                };

                _plugins[pluginInfo.Id] = instance;
                pluginInfo.State = PluginState.Stopped;
            }
        }

        private void LoadAndInitializePlugin(PluginInfo pluginInfo)
        {
            lock (_lock)
            {
                if (_plugins.ContainsKey(pluginInfo.Id))
                {
                    _logger.Warning("PluginManager", $"Plugin {pluginInfo.Id} already exists");
                    return;
                }

                var plugin = _loader.CreatePluginInstance(pluginInfo);

                var context = new PluginContext
                {
                    ApplicationName = "AI_Chat",
                    ApplicationVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version,
                    PluginDirectory = _pluginDirectory,
                    DataDirectory = Path.Combine(_dataDirectory, pluginInfo.Id),
                    ConfigDirectory = _configDirectory,
                    Logger = _logger,
                    ServiceProvider = _serviceProvider,
                    ConfigManager = _configManager,
                    PluginManager = this,
                    GlobalSettings = new Dictionary<string, object>()
                };

                plugin.Initialize(context);

                var instance = new PluginInstance
                {
                    Info = pluginInfo,
                    Plugin = plugin,
                    Context = context
                };

                _plugins[pluginInfo.Id] = instance;

                if (pluginInfo.AutoStart)
                {
                    StartPlugin(pluginInfo.Id);
                }
                else
                {
                    pluginInfo.State = PluginState.Stopped;
                }
            }
        }

        public PluginInfo DiscoverPlugin(string assemblyPath)
        {
            var result = _loader.LoadPlugin(assemblyPath);

            if (!result.Success)
            {
                _logger.Error("PluginManager", $"Failed to load plugin: {result.ErrorMessage}");
                return null;
            }

            return result.PluginInfo;
        }

        public bool InitializeDiscoveredPlugin(PluginInfo pluginInfo)
        {
            var stateConfig = GetPluginStateConfig(pluginInfo.Id);
            if (!stateConfig.IsEnabled)
            {
                _logger.Info("PluginManager", $"Plugin {pluginInfo.Name} is disabled, skipping load");
                return false;
            }

            try
            {
                LoadAndInitializePlugin(pluginInfo);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error("PluginManager", $"Failed to initialize plugin", ex);
                return false;
            }
        }

        public bool LoadPlugin(string assemblyPath)
        {
            var result = _loader.LoadPlugin(assemblyPath);

            if (!result.Success)
            {
                _logger.Error("PluginManager", $"Failed to load plugin: {result.ErrorMessage}");
                return false;
            }

            var stateConfig = GetPluginStateConfig(result.PluginInfo.Id);
            if (!stateConfig.IsEnabled)
            {
                _logger.Info("PluginManager", $"Plugin {result.PluginInfo.Name} is disabled, skipping load");
                return false;
            }

            try
            {
                LoadAndInitializePlugin(result.PluginInfo);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error("PluginManager", $"Failed to initialize plugin", ex);
                return false;
            }
        }

        public bool UnloadPlugin(string pluginId)
        {
            lock (_lock)
            {
                if (!_plugins.TryGetValue(pluginId, out var instance))
                {
                    _logger.Warning("PluginManager", $"Plugin {pluginId} not found");
                    return false;
                }

                try
                {
                    StopAndDisposePlugin(instance);
                    UnregisterPluginHandlers(pluginId, instance.Info.Name);
                    
                    _plugins.Remove(pluginId);

                    var hashKey = GetPluginIdHash(pluginId);
                    if (_pluginStates.Remove(hashKey))
                    {
                        SavePluginStates();
                    }

                    var assemblyPath = instance.Info.AssemblyPath;
                    _logger.Info("PluginManager", $"Preparing to unload plugin assembly, path: {assemblyPath}");
                    if (!string.IsNullOrEmpty(assemblyPath))
                    {
                        _loader.UnloadAssembly(assemblyPath);
                        _logger.Info("PluginManager", $"Plugin assembly unloaded: {assemblyPath}");
                    }

                    if (string.IsNullOrEmpty(assemblyPath))
                    {
                        _logger.Warning("PluginManager", "Plugin file path is empty, cannot delete");
                    }
                    else if (!File.Exists(assemblyPath))
                    {
                        _logger.Warning("PluginManager", $"Plugin file does not exist: {assemblyPath}");
                    }
                    else
                    {
                        try
                        {
                            File.Delete(assemblyPath);
                            _logger.Info("PluginManager", $"Deleted plugin file: {assemblyPath}");
                        }
                        catch (Exception ex)
                        {
                            _logger.Warning("PluginManager", $"Failed to delete plugin file: {ex.Message}");
                        }
                    }

                    var pluginDataDir = Path.Combine(_dataDirectory, pluginId);
                    if (Directory.Exists(pluginDataDir))
                    {
                        try
                        {
                            Directory.Delete(pluginDataDir, true);
                            _logger.Info("PluginManager", $"Deleted plugin data directory: {pluginDataDir}");
                        }
                        catch (Exception ex)
                        {
                            _logger.Warning("PluginManager", $"Failed to delete plugin data directory: {ex.Message}");
                        }
                    }

                    _logger.Info("PluginManager", $"Plugin {instance.Info.Name} unloaded");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.Error("PluginManager", $"Failed to unload plugin {pluginId}", ex);
                    return false;
                }
            }
        }

        public bool ReloadPlugin(string pluginId)
        {
            lock (_lock)
            {
                if (!_plugins.TryGetValue(pluginId, out var instance))
                {
                    _logger.Warning("PluginManager", $"Plugin {pluginId} not found");
                    return false;
                }

                var assemblyPath = instance.Info.AssemblyPath;
                var wasEnabled = instance.Info.IsEnabled;

                try
                {
                    if (instance.Info.State == PluginState.Running && instance.Plugin != null)
                    {
                        instance.Plugin.Stop();
                    }

                    if (instance.Plugin != null)
                    {
                        instance.Plugin.Dispose();
                    }

                    if (_pluginApi is PluginApi pluginApi)
                    {
                        pluginApi.UnregisterPreMergeMessageHandlers(pluginId);
                        pluginApi.UnregisterPostMergeMessageHandlers(pluginId);
                        pluginApi.UnregisterMessageAppendedHandlers(pluginId);
                        pluginApi.UnregisterLLMResponseHandlers(pluginId);
                        pluginApi.UnregisterPreLLMRequestHandlers(pluginId);
                        pluginApi.UnregisterPluginPermissions(pluginId);
                        _logger.Info("PluginManager", $"Handlers and permissions for plugin {instance.Info.Name} unregistered");
                    }

                    if (!string.IsNullOrEmpty(assemblyPath))
                    {
                        _loader.UnloadAssembly(assemblyPath);
                        _logger.Info("PluginManager", $"Plugin assembly unloaded for reload: {assemblyPath}");
                    }

                    _plugins.Remove(pluginId);

                    _logger.Info("PluginManager", $"Plugin {pluginId} unloaded, preparing to reload");

                    if (!string.IsNullOrEmpty(assemblyPath) && File.Exists(assemblyPath))
                    {
                        return LoadPlugin(assemblyPath);
                    }
                    else
                    {
                        _logger.Error("PluginManager", $"Plugin file does not exist: {assemblyPath}");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error("PluginManager", $"Failed to reload plugin {pluginId}", ex);
                    return false;
                }
            }
        }

        public bool StartPlugin(string pluginId)
        {
            lock (_lock)
            {
                if (!_plugins.TryGetValue(pluginId, out var instance))
                {
                    _logger.Warning("PluginManager", $"Plugin {pluginId} not found");
                    return false;
                }

                if (instance.Plugin == null)
                {
                    return EnablePlugin(pluginId);
                }

                return DoStartPlugin(instance);
            }
        }

        public bool StopPlugin(string pluginId)
        {
            lock (_lock)
            {
                if (!_plugins.TryGetValue(pluginId, out var instance))
                {
                    _logger.Warning("PluginManager", $"Plugin {pluginId} not found");
                    return false;
                }

                try
                {
                    if (instance.Plugin != null)
                    {
                        instance.Plugin.Stop();
                        instance.Plugin.Dispose();
                    }

                    if (_pluginApi is PluginApi pluginApi)
                    {
                        pluginApi.UnregisterPreMergeMessageHandlers(pluginId);
                        pluginApi.UnregisterPostMergeMessageHandlers(pluginId);
                        pluginApi.UnregisterMessageAppendedHandlers(pluginId);
                        pluginApi.UnregisterLLMResponseHandlers(pluginId);
                        pluginApi.UnregisterPreLLMRequestHandlers(pluginId);
                        pluginApi.UnregisterPluginPermissions(pluginId);
                        _logger.Info("PluginManager", $"Handlers and permissions for plugin {instance.Info.Name} unregistered");
                    }

                    instance.Info.State = PluginState.Stopped;

                    var hashKey = GetPluginIdHash(pluginId);
                    var stateConfig = GetPluginStateConfig(pluginId);
                    stateConfig.IsEnabled = false;
                    _pluginStates[hashKey] = stateConfig;
                    SavePluginStates();
                    instance.Info.IsEnabled = false;

                    instance.Plugin = null;
                    instance.Context = null;

                    _logger.Info("PluginManager", $"Plugin {instance.Info.Name} stopped and disabled");
                    return true;
                }
                catch (Exception ex)
                {
                    instance.Info.State = PluginState.Error;
                    instance.Info.ErrorMessage = ex.Message;
                    _logger.Error("PluginManager", $"Failed to stop plugin {pluginId}", ex);
                    return false;
                }
            }
        }

        public void StartAllPlugins()
        {
            var sortedPlugins = _plugins.Values
                .Where(p => p.Info.IsEnabled)
                .OrderBy(p => p.Info.Priority)
                .ToList();

            foreach (var plugin in sortedPlugins)
            {
                if (plugin.Info.State == PluginState.Initialized || plugin.Info.State == PluginState.Stopped)
                {
                    StartPlugin(plugin.Info.Id);
                }
            }
        }

        public void StopAllPlugins()
        {
            foreach (var plugin in _plugins.Values.ToList())
            {
                if (plugin.Info.State == PluginState.Running)
                {
                    StopPlugin(plugin.Info.Id);
                }
            }
        }

        public IEnumerable<IPlugin> GetAllPlugins()
        {
            lock (_lock)
            {
                return _plugins.Values.Where(p => p.Plugin != null).Select(p => p.Plugin).ToList();
            }
        }

        public IEnumerable<PluginInfo> GetAllPluginInfos()
        {
            lock (_lock)
            {
                return _plugins.Values.Select(p => p.Info).ToList();
            }
        }

        public IPlugin GetPlugin(string pluginId)
        {
            lock (_lock)
            {
                return _plugins.TryGetValue(pluginId, out var instance) ? instance.Plugin : null;
            }
        }

        public PluginInfo GetPluginInfo(string pluginId)
        {
            lock (_lock)
            {
                return _plugins.TryGetValue(pluginId, out var instance) ? instance.Info : null;
            }
        }

        public PluginBase GetPluginInstance(string pluginId)
        {
            lock (_lock)
            {
                return _plugins.TryGetValue(pluginId, out var instance) ? instance.Plugin as PluginBase : null;
            }
        }

        public bool IsPluginLoaded(string pluginId)
        {
            lock (_lock)
            {
                return _plugins.ContainsKey(pluginId);
            }
        }

        public bool IsPluginRunning(string pluginId)
        {
            lock (_lock)
            {
                return _plugins.TryGetValue(pluginId, out var instance) && instance.Info.State == PluginState.Running;
            }
        }

        public object ExecuteCommand(string pluginId, string command, Dictionary<string, object> parameters)
        {
            var plugin = GetPlugin(pluginId);
            if (plugin == null)
            {
                throw new InvalidOperationException($"插件 {pluginId} 未找到");
            }

            return plugin.ExecuteCommand(command, parameters);
        }

        /// <summary>
        /// 获取所有可用插件的能力列表
        /// </summary>
        public Dictionary<string, List<PluginCapability>> GetAllPluginCapabilities()
        {
            lock (_lock)
            {
                var result = new Dictionary<string, List<PluginCapability>>();

                foreach (var instance in _plugins.Values)
                {
                    if (instance.Plugin != null && instance.Info.IsEnabled && instance.Info.State == PluginState.Running)
                    {
                        try
                        {
                            var capabilities = instance.Plugin.GetCapabilities();
                            if (capabilities != null && capabilities.Count > 0)
                            {
                                result[instance.Plugin.Id] = capabilities;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Error("PluginManager", $"获取插件 {instance.Plugin.Id} 能力列表失败", ex);
                        }
                    }
                }

                return result;
            }
        }

        /// <summary>
        /// 调用指定插件的能力
        /// </summary>
        public PluginCapabilityResult InvokePluginCapability(string pluginId, string capabilityName, Dictionary<string, object> parameters)
        {
            lock (_lock)
            {
                if (!_plugins.TryGetValue(pluginId, out var instance))
                {
                    return PluginCapabilityResult.ErrorResult($"插件 {pluginId} 未找到");
                }

                if (instance.Plugin == null)
                {
                    return PluginCapabilityResult.ErrorResult($"插件 {pluginId} 未加载");
                }

                if (!instance.Info.IsEnabled || instance.Info.State != PluginState.Running)
                {
                    return PluginCapabilityResult.ErrorResult($"插件 {pluginId} 未启用或未运行");
                }

                try
                {
                    return instance.Plugin.InvokeCapability(capabilityName, parameters);
                }
                catch (Exception ex)
                {
                    _logger.Error("PluginManager", $"调用插件 {pluginId} 能力 {capabilityName} 失败", ex);
                    return PluginCapabilityResult.ErrorResult(ex.Message);
                }
            }
        }

        /// <summary>
        /// 生成可用插件能力提示词（用于大模型）
        /// </summary>
        public string GeneratePluginCapabilitiesPrompt()
        {
            var capabilities = GetAllPluginCapabilities();

            var sb = new StringBuilder();
            sb.AppendLine("\n\n【回复格式要求】");
            sb.AppendLine("你的回复必须是JSON格式，包含以下字段：");
            sb.AppendLine("- reply: true/false（是否回复本条消息）");
            sb.AppendLine("- messages: 消息数组，每个对象只能包含content（文字内容）或meme（表情包文件名），以及可选的delay_ms（延迟毫秒数）");
            sb.AppendLine("- events: 约定数组，每个对象包含name（约定内容）和time（yyyy-MM-dd HH:mm:ss格式）");
            sb.AppendLine("- plugin_invoke: 插件调用对象（可选，与messages同级），包含plugin_id、capability、parameters");
            sb.AppendLine();
            sb.AppendLine("完整示例：");
            sb.AppendLine("{");
            sb.AppendLine("  \"reply\": true,");
            sb.AppendLine("  \"messages\": [");
            sb.AppendLine("    {\"content\": \"纯文字消息内容，可融入河南方言\", \"delay_ms\": 0},");
            sb.AppendLine("    {\"meme\": \"表情包文件名.jpg\", \"delay_ms\": 500}");
            sb.AppendLine("  ],");
            sb.AppendLine("  \"events\": [");
            sb.AppendLine("    {\"name\": \"约定具体内容\", \"time\": \"2026-02-01 07:00:00\"}");
            sb.AppendLine("  ],");
            sb.AppendLine("  \"plugin_invoke\": {");
            sb.AppendLine("    \"plugin_id\": \"插件ID\",");
            sb.AppendLine("    \"capability\": \"能力名称\",");
            sb.AppendLine("    \"parameters\": {\"参数名\": \"参数值\"}");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("重要提示：");
            sb.AppendLine("1. plugin_invoke 必须放在 messages 和 events 外面，与它们是同级关系");
            sb.AppendLine("2. messages 数组里的对象只能有 content/meme 和 delay_ms，不能放 plugin_invoke");
            sb.AppendLine("3. content仅限发文字消息时用，meme仅限发表情消息时用");
            sb.AppendLine("4. 如果同时有消息和插件调用，会先发送消息给用户，然后执行插件调用");
            sb.AppendLine("5. 插件执行结果会反馈给你，你可以继续回复");
            sb.AppendLine("6. 用不上的字段可以不添加（如不需要约定可省略 events，不需要插件调用可省略 plugin_invoke）");

            if (capabilities.Count > 0)
            {
                sb.AppendLine("\n【当前可用插件】");
                sb.AppendLine("你可以通过调用以下插件来扩展功能：");
                sb.AppendLine();

                foreach (var kvp in capabilities)
                {
                    var pluginId = kvp.Key;
                    var pluginCaps = kvp.Value;
                    var plugin = GetPlugin(pluginId);

                    sb.AppendLine($"插件ID: {pluginId}");
                    sb.AppendLine($"插件名称: {plugin?.Name ?? pluginId}");
                    sb.AppendLine("可用能力:");

                    foreach (var cap in pluginCaps)
                    {
                        sb.AppendLine($"  - {cap.Name}: {cap.Description}");
                        if (cap.Parameters != null && cap.Parameters.Count > 0)
                        {
                            sb.AppendLine($"    参数:");
                            foreach (var param in cap.Parameters)
                            {
                                var required = param.Required ? "(必需)" : "(可选)";
                                sb.AppendLine($"      - {param.Name} ({param.Type}){required}: {param.Description}");
                            }
                        }
                    }
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        public void Dispose()
        {
            _logger.Info("PluginManager", "Shutting down plugin manager...");

            StopAllPlugins();

            foreach (var instance in _plugins.Values.ToList())
            {
                try
                {
                    instance.Plugin.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Error("PluginManager", $"Error disposing plugin {instance.Info.Id}", ex);
                }
            }

            _plugins.Clear();
            _serviceProvider.Clear();

            _logger.Info("PluginManager", "Plugin manager shut down");
        }

        private void EnsureDirectories()
        {
            if (!Directory.Exists(_pluginDirectory))
                Directory.CreateDirectory(_pluginDirectory);

            if (!Directory.Exists(_dataDirectory))
                Directory.CreateDirectory(_dataDirectory);

            if (!Directory.Exists(_configDirectory))
                Directory.CreateDirectory(_configDirectory);
        }

        private void UnregisterPluginHandlers(string pluginId, string pluginName)
        {
            if (_pluginApi is PluginApi pluginApi)
            {
                pluginApi.UnregisterPreMergeMessageHandlers(pluginId);
                pluginApi.UnregisterPostMergeMessageHandlers(pluginId);
                pluginApi.UnregisterMessageAppendedHandlers(pluginId);
                pluginApi.UnregisterLLMResponseHandlers(pluginId);
                pluginApi.UnregisterPreLLMRequestHandlers(pluginId);
                pluginApi.UnregisterPluginPermissions(pluginId);
                _logger.Info("PluginManager", $"Handlers and permissions for plugin {pluginName} unregistered");
            }
        }

        private void StopAndDisposePlugin(PluginInstance instance)
        {
            if (instance.Plugin == null) return;

            try
            {
                if (instance.Info.State == PluginState.Running)
                {
                    instance.Plugin.Stop();
                }
            }
            catch (Exception ex)
            {
                _logger.Error("PluginManager", $"Error stopping plugin {instance.Info.Name}", ex);
            }

            try
            {
                instance.Plugin.Dispose();
            }
            catch (Exception ex)
            {
                _logger.Error("PluginManager", $"Error disposing plugin {instance.Info.Name}", ex);
            }
        }

        private class PluginInstance
        {
            public PluginInfo Info { get; set; }
            public IPlugin Plugin { get; set; }
            public PluginContext Context { get; set; }
        }
    }

    public class PluginStateConfig
    {
        public string PluginId { get; set; }
        public bool IsEnabled { get; set; } = true;
    }
}
