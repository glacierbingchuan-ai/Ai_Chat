using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AI_Chat.Services;
using AI_Chat.Plugins.Virtualization;
using AI_Chat.Models;
using Newtonsoft.Json;

namespace AI_Chat.Plugins
{
    /// <summary>
    /// 插件管理器 - 管理插件的生命周期
    /// </summary>
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
        private PluginVirtualizationManager _virtualizationManager;
        private ControlPanelServer _controlPanelServer;
        private readonly object _lock = new object();
        private Dictionary<string, PluginStateConfig> _pluginStates;

        public string PluginDirectory => _pluginDirectory;
        public string DataDirectory => _dataDirectory;
        public string ConfigDirectory => _configDirectory;
        public IServiceProvider ServiceProvider => _serviceProvider;
        public IPluginApi PluginApi => _pluginApi;
        public PluginVirtualizationManager VirtualizationManager => _virtualizationManager;

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

        /// <summary>
        /// 设置 PluginApi（用于延迟注入）
        /// </summary>
        public void SetPluginApi(IPluginApi pluginApi)
        {
            _pluginApi = pluginApi;
            if (_pluginApi != null)
            {
                _serviceProvider.RegisterService<IPluginApi>(_pluginApi);
                _logger.Info("PluginManager", "IPluginApi registered to service container");
            }
        }

        /// <summary>
        /// 设置虚拟化管理器
        /// </summary>
        public void SetVirtualizationManager(PluginVirtualizationManager virtualizationManager)
        {
            _virtualizationManager = virtualizationManager;
        }

        /// <summary>
        /// 设置控制面板服务器
        /// </summary>
        public void SetControlPanelServer(ControlPanelServer controlPanelServer)
        {
            _controlPanelServer = controlPanelServer;
        }

        /// <summary>
        /// 广播沙箱警告给所有客户端
        /// </summary>
        private void BroadcastSandboxWarning(PluginInfo pluginInfo)
        {
            if (_controlPanelServer == null) return;

            var message = new WebSocketMessage
            {
                Type = "plugin_sandbox_warning",
                Data = new
                {
                    pluginId = pluginInfo.Id,
                    pluginName = pluginInfo.Name
                }
            };
            _controlPanelServer.BroadcastMessageToClients(message);
        }

        /// <summary>
        /// 初始化插件管理器
        /// </summary>
        public void Initialize()
        {
            _logger.Info("PluginManager", "Plugin manager initializing...");

            _serviceProvider.RegisterService<IPluginManager>(this);
            _serviceProvider.RegisterService<ConfigManager>(_configManager);

            // Register IPluginApi to service container
            if (_pluginApi != null)
            {
                _serviceProvider.RegisterService<IPluginApi>(_pluginApi);
                _logger.Info("PluginManager", "IPluginApi registered to service container");
            }

            _logger.Info("PluginManager", "Plugin manager initialization completed");
        }

        /// <summary>
        /// 加载插件状态配置
        /// </summary>
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

        /// <summary>
        /// 保存插件状态配置
        /// </summary>
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

        /// <summary>
        /// 获取插件ID的MD5哈希值
        /// </summary>
        private string GetPluginIdHash(string pluginId)
        {
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(pluginId));
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        /// <summary>
        /// 获取插件状态配置
        /// </summary>
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

        /// <summary>
        /// 启用插件 - 统一入口，处理从文件加载到启动的完整流程
        /// </summary>
        public bool EnablePlugin(string pluginId)
        {
            lock (_lock)
            {
                // 更新状态配置
                var hashKey = GetPluginIdHash(pluginId);
                var stateConfig = GetPluginStateConfig(pluginId);
                stateConfig.IsEnabled = true;
                _pluginStates[hashKey] = stateConfig;
                SavePluginStates();

                // 检查插件是否已加载
                if (_plugins.TryGetValue(pluginId, out var instance))
                {
                    instance.Info.IsEnabled = true;
                    
                    // 如果插件已初始化但未运行，直接启动
                    if (instance.Plugin != null && instance.Info.State != PluginState.Running)
                    {
                        return DoStartPlugin(instance);
                    }
                    
                    // 如果插件未初始化（Plugin == null），需要从文件重新加载
                    if (instance.Plugin == null)
                    {
                        return LoadAndStartPluginFromFile(pluginId);
                    }
                    
                    // 插件已经在运行
                    return true;
                }
                
                // 插件未加载，从文件加载
                return LoadAndStartPluginFromFile(pluginId);
            }
        }

        /// <summary>
        /// 从文件加载并启动插件
        /// </summary>
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
                        
                        // 如果存在旧的空实例，先移除
                        _plugins.Remove(pluginId);
                        
                        // 加载并初始化
                        LoadAndInitializePlugin(result.PluginInfo);
                        
                        // 如果插件没有自动启动，手动启动
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

        /// <summary>
        /// 核心启动方法 - 统一处理插件启动和虚拟化启用
        /// </summary>
        private bool DoStartPlugin(PluginInstance instance)
        {
            if (instance.Plugin == null)
            {
                _logger.Warning("PluginManager", $"Plugin {instance.Info.Id} instance is null");
                return false;
            }

            if (instance.Info.State == PluginState.Running)
            {
                return true; // 已经在运行
            }

            try
            {
                // 根据 SupportSandbox 决定是否启用虚拟化
                if (instance.Info.SupportSandbox)
                {
                    _virtualizationManager?.EnableVirtualization(instance.Info.Id);
                    _logger.Info("PluginManager", $"Plugin {instance.Info.Name} started with virtualization enabled");
                }
                else
                {
                    _logger.Info("PluginManager", $"Plugin {instance.Info.Name} started without virtualization (plugin does not support sandbox)");
                }
                
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

        /// <summary>
        /// 禁用插件
        /// </summary>
        public bool DisablePlugin(string pluginId)
        {
            lock (_lock)
            {
                var hashKey = GetPluginIdHash(pluginId);
                var stateConfig = GetPluginStateConfig(pluginId);
                stateConfig.IsEnabled = false;
                _pluginStates[hashKey] = stateConfig;
                SavePluginStates();

                // 如果插件已加载，停止并释放它
                if (_plugins.TryGetValue(pluginId, out var instance))
                {
                    instance.Info.IsEnabled = false;
                    
                    // 停止并释放插件
                    StopAndDisposePlugin(instance);
                    
                    // 注销所有处理器和权限
                    UnregisterPluginHandlers(pluginId, instance.Info.Name);
                    
                    // 清空实例引用但保留在列表中
                    instance.Plugin = null;
                    instance.Context = null;
                    instance.Info.State = PluginState.Stopped;
                }

                _logger.Info("PluginManager", $"Plugin {pluginId} disabled");
                return true;
            }
        }

        private List<PluginInfo> _discoveredPlugins = new List<PluginInfo>();

        /// <summary>
        /// 发现所有插件（不初始化）
        /// </summary>
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

        /// <summary>
        /// 初始化所有已发现的插件（在启用虚拟化后调用）
        /// </summary>
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

        /// <summary>
        /// 将禁用的插件添加到列表（不初始化）
        /// </summary>
        private void AddDisabledPluginToList(PluginInfo pluginInfo)
        {
            lock (_lock)
            {
                if (_plugins.ContainsKey(pluginInfo.Id))
                {
                    return;
                }

                // 创建空的插件实例（不调用 Initialize）
                var instance = new PluginInstance
                {
                    Info = pluginInfo,
                    Plugin = null,  // 禁用的插件没有实例
                    Context = null
                };

                _plugins[pluginInfo.Id] = instance;
                pluginInfo.State = PluginState.Stopped;
            }
        }

        /// <summary>
        /// 加载并初始化单个插件
        /// </summary>
        private void LoadAndInitializePlugin(PluginInfo pluginInfo)
        {
            lock (_lock)
            {
                if (_plugins.ContainsKey(pluginInfo.Id))
                {
                    _logger.Warning("PluginManager", $"Plugin {pluginInfo.Id} already exists");
                    return;
                }

                // 检查是否是第一次加载且不支持沙箱
                var stateConfig = GetPluginStateConfig(pluginInfo.Id);
                if (!pluginInfo.SupportSandbox && !stateConfig.HasShownSandboxWarning)
                {
                    // 标记已显示警告（但不保存，等用户确认后再保存）
                    stateConfig.HasShownSandboxWarning = true;
                    
                    // 发送警告给前端，等待用户确认
                    BroadcastSandboxWarning(pluginInfo);
                    
                    // 将插件添加到列表但不初始化，状态设为等待确认
                    var pendingInstance = new PluginInstance
                    {
                        Info = pluginInfo,
                        Plugin = null,
                        Context = null
                    };
                    _plugins[pluginInfo.Id] = pendingInstance;
                    pluginInfo.State = PluginState.PendingApproval;
                    
                    _logger.Warning("PluginManager", $"Plugin {pluginInfo.Name} ({pluginInfo.Id}) requires user approval to load (no sandbox support)");
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

                // 同步 SupportSandbox 属性到 PluginInfo
                // 优先用插件实例的值，因为插件可能在运行时动态修改
                pluginInfo.SupportSandbox = plugin.SupportSandbox;

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

        /// <summary>
        /// 加载单个插件（从文件）- 返回插件信息但不初始化
        /// </summary>
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

        /// <summary>
        /// 初始化已发现的插件
        /// </summary>
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

        /// <summary>
        /// 加载单个插件（从文件）
        /// </summary>
        public bool LoadPlugin(string assemblyPath)
        {
            var result = _loader.LoadPlugin(assemblyPath);

            if (!result.Success)
            {
                _logger.Error("PluginManager", $"Failed to load plugin: {result.ErrorMessage}");
                return false;
            }

            // Check if plugin is disabled
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

        /// <summary>
        /// 卸载插件（并删除文件）
        /// </summary>
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
                    // 停止、释放插件并注销处理器
                    StopAndDisposePlugin(instance);
                    UnregisterPluginHandlers(pluginId, instance.Info.Name);
                    
                    _plugins.Remove(pluginId);

                    // 从状态配置中移除（使用MD5哈希）
                    var hashKey = GetPluginIdHash(pluginId);
                    if (_pluginStates.Remove(hashKey))
                    {
                        SavePluginStates();
                    }

                    // Delete plugin file
                    var assemblyPath = instance.Info.AssemblyPath;
                    _logger.Info("PluginManager", $"Preparing to delete plugin file, path: {assemblyPath}");
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

                    // Delete plugin data and config directories
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

        /// <summary>
        /// 重新加载插件（不删除文件）
        /// </summary>
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
                    // Stop plugin (if running)
                    if (instance.Info.State == PluginState.Running && instance.Plugin != null)
                    {
                        instance.Plugin.Stop();
                    }

                    // Release plugin instance
                    if (instance.Plugin != null)
                    {
                        instance.Plugin.Dispose();
                    }

                    // 注销所有处理器和权限
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

                    // Remove from list (but don't delete file)
                    _plugins.Remove(pluginId);

                    _logger.Info("PluginManager", $"Plugin {pluginId} unloaded, preparing to reload");

                    // Reload plugin
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

        /// <summary>
        /// 启动插件 - 统一使用 DoStartPlugin
        /// </summary>
        public bool StartPlugin(string pluginId)
        {
            lock (_lock)
            {
                if (!_plugins.TryGetValue(pluginId, out var instance))
                {
                    _logger.Warning("PluginManager", $"Plugin {pluginId} not found");
                    return false;
                }

                // 如果插件未初始化，先启用（会从文件加载）
                if (instance.Plugin == null)
                {
                    return EnablePlugin(pluginId);
                }

                // 使用统一的启动方法
                return DoStartPlugin(instance);
            }
        }

        /// <summary>
        /// 停止插件（同时禁用，保留在列表中）
        /// </summary>
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
                    // Stop plugin
                    if (instance.Plugin != null)
                    {
                        instance.Plugin.Stop();
                        instance.Plugin.Dispose();
                    }

                    // 停止插件后禁用虚拟化
                    _virtualizationManager?.DisableVirtualization(pluginId);

                    // Unregister all handlers and permissions for this plugin
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

                    // Also disable plugin to prevent auto-start on restart
                    var hashKey = GetPluginIdHash(pluginId);
                    var stateConfig = GetPluginStateConfig(pluginId);
                    stateConfig.IsEnabled = false;
                    _pluginStates[hashKey] = stateConfig;
                    SavePluginStates();
                    instance.Info.IsEnabled = false;

                    // Release instance but keep in list
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

        /// <summary>
        /// 用户确认后加载不支持沙箱的插件
        /// </summary>
        public bool ApproveAndLoadPlugin(string pluginId)
        {
            lock (_lock)
            {
                if (!_plugins.TryGetValue(pluginId, out var instance))
                {
                    _logger.Warning("PluginManager", $"Plugin {pluginId} not found");
                    return false;
                }

                if (instance.Info.State != PluginState.PendingApproval)
                {
                    _logger.Warning("PluginManager", $"Plugin {pluginId} is not in pending approval state");
                    return false;
                }

                try
                {
                    // 保存状态（标记已确认）
                    SavePluginStates();
                    
                    // 从列表中移除
                    _plugins.Remove(pluginId);
                    
                    // 从已发现的插件列表中获取原始 PluginInfo（状态为 Loaded）
                    var originalPluginInfo = _discoveredPlugins.FirstOrDefault(p => p.Id.Equals(pluginId, StringComparison.OrdinalIgnoreCase));
                    if (originalPluginInfo == null)
                    {
                        _logger.Error("PluginManager", $"Original plugin info for {pluginId} not found in discovered plugins");
                        return false;
                    }
                    
                    // 重新加载并初始化插件
                    LoadAndInitializePlugin(originalPluginInfo);
                    
                    _logger.Info("PluginManager", $"Plugin {instance.Info.Name} approved and loaded successfully");
                    return true;
                }
                catch (Exception ex)
                {
                    instance.Info.State = PluginState.Error;
                    instance.Info.ErrorMessage = ex.Message;
                    _logger.Error("PluginManager", $"Failed to approve and load plugin {pluginId}", ex);
                    return false;
                }
            }
        }

        /// <summary>
        /// 启动所有启用的插件（按优先级排序，数值小的先启动）
        /// </summary>
        public void StartAllPlugins()
        {
            // 按优先级排序，数值小的先启动（优先级高的先注册处理器）
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

        /// <summary>
        /// 停止所有插件
        /// </summary>
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

        /// <summary>
        /// 获取所有已启用的插件
        /// </summary>
        public IEnumerable<IPlugin> GetAllPlugins()
        {
            lock (_lock)
            {
                return _plugins.Values.Where(p => p.Plugin != null).Select(p => p.Plugin).ToList();
            }
        }

        /// <summary>
        /// 获取所有插件信息
        /// </summary>
        public IEnumerable<PluginInfo> GetAllPluginInfos()
        {
            lock (_lock)
            {
                return _plugins.Values.Select(p => p.Info).ToList();
            }
        }

        /// <summary>
        /// 获取所有已发现的插件信息（包括未初始化的）
        /// </summary>
        public IEnumerable<PluginInfo> GetDiscoveredPluginInfos()
        {
            lock (_lock)
            {
                var allInfos = new List<PluginInfo>();
                allInfos.AddRange(_discoveredPlugins);
                allInfos.AddRange(_plugins.Values.Where(p => !_discoveredPlugins.Any(d => d.Id == p.Info.Id)).Select(p => p.Info));
                return allInfos;
            }
        }

        /// <summary>
        /// 获取指定插件
        /// </summary>
        public IPlugin GetPlugin(string pluginId)
        {
            lock (_lock)
            {
                return _plugins.TryGetValue(pluginId, out var instance) ? instance.Plugin : null;
            }
        }

        /// <summary>
        /// 获取指定插件信息
        /// </summary>
        public PluginInfo GetPluginInfo(string pluginId)
        {
            lock (_lock)
            {
                return _plugins.TryGetValue(pluginId, out var instance) ? instance.Info : null;
            }
        }

        /// <summary>
        /// 获取指定插件实例（返回 PluginBase 以访问 Data 属性）
        /// </summary>
        public PluginBase GetPluginInstance(string pluginId)
        {
            lock (_lock)
            {
                return _plugins.TryGetValue(pluginId, out var instance) ? instance.Plugin as PluginBase : null;
            }
        }

        /// <summary>
        /// 检查插件是否已加载
        /// </summary>
        public bool IsPluginLoaded(string pluginId)
        {
            lock (_lock)
            {
                return _plugins.ContainsKey(pluginId);
            }
        }

        /// <summary>
        /// 检查插件是否正在运行
        /// </summary>
        public bool IsPluginRunning(string pluginId)
        {
            lock (_lock)
            {
                return _plugins.TryGetValue(pluginId, out var instance) && instance.Info.State == PluginState.Running;
            }
        }

        /// <summary>
        /// 执行插件命令
        /// </summary>
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
        /// 释放资源
        /// </summary>
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

        /// <summary>
        /// 确保目录存在
        /// </summary>
        private void EnsureDirectories()
        {
            if (!Directory.Exists(_pluginDirectory))
                Directory.CreateDirectory(_pluginDirectory);

            if (!Directory.Exists(_dataDirectory))
                Directory.CreateDirectory(_dataDirectory);

            if (!Directory.Exists(_configDirectory))
                Directory.CreateDirectory(_configDirectory);
        }

        /// <summary>
        /// 注销插件的所有处理器和权限（提取的公共方法）
        /// </summary>
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

        /// <summary>
        /// 停止并释放插件实例（提取的公共方法）
        /// </summary>
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

        /// <summary>
        /// 禁用虚拟化（如果支持）
        /// </summary>
        private void DisableVirtualizationIfSupported(string pluginId)
        {
            _virtualizationManager?.DisableVirtualization(pluginId);
        }

        private class PluginInstance
        {
            public PluginInfo Info { get; set; }
            public IPlugin Plugin { get; set; }
            public PluginContext Context { get; set; }
        }
    }

    /// <summary>
    /// 插件状态配置
    /// </summary>
    public class PluginStateConfig
    {
        /// <summary>
        /// 插件ID（用于反向查找）
        /// </summary>
        public string PluginId { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 是否已显示沙箱警告
        /// </summary>
        public bool HasShownSandboxWarning { get; set; } = false;
    }
}
