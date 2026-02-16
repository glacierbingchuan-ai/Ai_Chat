using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;

namespace AI_Chat.Plugins
{
    /// <summary>
    /// 插件基类 - 所有插件应继承此类
    /// </summary>
    public abstract class PluginBase : IPlugin
    {
        private PluginContext _context;
        private Dictionary<string, object> _configuration;
        private readonly Dictionary<string, MethodInfo> _commandHandlers;

        public abstract string Id { get; }
        public abstract string Name { get; }
        public abstract Version Version { get; }
        public abstract string Author { get; }
        public abstract string Description { get; }

        public PluginState State { get; protected set; }

        protected PluginContext Context => _context;
        protected IPluginLogger Logger => _context?.Logger;
        protected string DataDirectory => _context?.DataDirectory;
        protected string ConfigDirectory => _context?.ConfigDirectory;
        protected IServiceProvider Services => _context?.ServiceProvider;
        protected IPluginManager PluginManager => _context?.PluginManager;
        protected IPluginApi Api => _context?.ServiceProvider?.GetService<IPluginApi>();

        protected PluginBase()
        {
            State = PluginState.Unloaded;
            _configuration = new Dictionary<string, object>();
            _commandHandlers = new Dictionary<string, MethodInfo>(StringComparer.OrdinalIgnoreCase);

            RegisterCommands();
        }

        /// <summary>
        /// 初始化插件
        /// </summary>
        public virtual void Initialize(PluginContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            State = PluginState.Initializing;
            _context = context;

            try
            {
                EnsureDirectories();
                LoadConfiguration();
                OnInitialize();

                State = PluginState.Initialized;
                Logger?.Info(Id, $"插件 '{Name}' 初始化成功");
            }
            catch (Exception ex)
            {
                State = PluginState.Error;
                Logger?.Error(Id, $"插件 '{Name}' 初始化失败", ex);
                throw;
            }
        }

        /// <summary>
        /// 启动插件
        /// </summary>
        public virtual void Start()
        {
            if (State != PluginState.Initialized && State != PluginState.Stopped)
            {
                throw new InvalidOperationException($"插件状态不正确，当前状态: {State}");
            }

            try
            {
                OnStart();
                State = PluginState.Running;
                Logger?.Info(Id, $"插件 '{Name}' 已启动");
            }
            catch (Exception ex)
            {
                State = PluginState.Error;
                Logger?.Error(Id, $"插件 '{Name}' 启动失败", ex);
                throw;
            }
        }

        /// <summary>
        /// 停止插件
        /// </summary>
        public virtual void Stop()
        {
            if (State != PluginState.Running)
            {
                Logger?.Warning(Id, $"插件 '{Name}' 未在运行状态，当前状态: {State}");
                return;
            }

            try
            {
                OnStop();
                SaveConfiguration();
                State = PluginState.Stopped;
                Logger?.Info(Id, $"插件 '{Name}' 已停止");
            }
            catch (Exception ex)
            {
                State = PluginState.Error;
                Logger?.Error(Id, $"插件 '{Name}' 停止失败", ex);
                throw;
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public virtual void Dispose()
        {
            if (State == PluginState.Running)
            {
                Stop();
            }

            OnDispose();

            State = PluginState.Uninstalled;
            Logger?.Info(Id, $"插件 '{Name}' 已释放");
        }

        /// <summary>
        /// 获取插件配置
        /// </summary>
        public virtual Dictionary<string, object> GetConfiguration()
        {
            return new Dictionary<string, object>(_configuration);
        }

        /// <summary>
        /// 设置插件配置
        /// </summary>
        public virtual void SetConfiguration(Dictionary<string, object> configuration)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            _configuration = new Dictionary<string, object>(configuration);
            OnConfigurationChanged();
            SaveConfiguration();
        }

        /// <summary>
        /// 执行插件命令
        /// </summary>
        public virtual object ExecuteCommand(string command, Dictionary<string, object> parameters)
        {
            if (string.IsNullOrWhiteSpace(command))
                throw new ArgumentException("命令名称不能为空", nameof(command));

            if (_commandHandlers.TryGetValue(command, out var method))
            {
                try
                {
                    var result = method.Invoke(this, new object[] { parameters });
                    Logger?.Debug(Id, $"执行命令 '{command}' 成功");
                    return result;
                }
                catch (TargetInvocationException ex)
                {
                    Logger?.Error(Id, $"执行命令 '{command}' 失败", ex.InnerException);
                    throw ex.InnerException;
                }
            }

            throw new NotSupportedException($"命令 '{command}' 不被支持");
        }

        /// <summary>
        /// 获取配置值
        /// </summary>
        protected T GetConfig<T>(string key, T defaultValue = default)
        {
            if (_configuration.TryGetValue(key, out var value))
            {
                if (value is T typedValue)
                {
                    return typedValue;
                }
                
                // 尝试类型转换（处理从前端传来的 JSON 值）
                try
                {
                    if (typeof(T) == typeof(string))
                    {
                        return (T)(object)value?.ToString();
                    }
                    if (typeof(T) == typeof(bool) && value is bool)
                    {
                        return (T)(object)value;
                    }
                    if (typeof(T) == typeof(int) && value is long longValue)
                    {
                        return (T)(object)(int)longValue;
                    }
                    if (typeof(T) == typeof(int) && value is int)
                    {
                        return (T)(object)value;
                    }
                    if (typeof(T) == typeof(double) && value is double)
                    {
                        return (T)(object)value;
                    }
                    if (typeof(T) == typeof(double) && value is long)
                    {
                        return (T)(object)Convert.ToDouble(value);
                    }
                    // 使用 Convert 进行通用转换
                    return (T)Convert.ChangeType(value, typeof(T));
                }
                catch
                {
                    // 转换失败，返回默认值
                }
            }
            return defaultValue;
        }

        /// <summary>
        /// 设置配置值
        /// </summary>
        protected void SetConfig<T>(string key, T value)
        {
            _configuration[key] = value;
        }

        /// <summary>
        /// 保存数据到文件
        /// </summary>
        protected void SaveData<T>(string fileName, T data)
        {
            var filePath = Path.Combine(DataDirectory, fileName);
            var json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// 从文件加载数据
        /// </summary>
        protected T LoadData<T>(string fileName, T defaultValue = default)
        {
            var filePath = Path.Combine(DataDirectory, fileName);
            if (!File.Exists(filePath))
                return defaultValue;

            var json = File.ReadAllText(filePath);
            return JsonConvert.DeserializeObject<T>(json);
        }

        /// <summary>
        /// 注册命令（通过特性自动扫描）
        /// </summary>
        private void RegisterCommands()
        {
            var methods = GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance);

            foreach (var method in methods)
            {
                var attr = method.GetCustomAttribute<PluginCommandAttribute>();
                if (attr != null)
                {
                    var parameters = method.GetParameters();
                    if (parameters.Length == 1 &&
                        parameters[0].ParameterType == typeof(Dictionary<string, object>))
                    {
                        _commandHandlers[attr.Name] = method;
                    }
                }
            }
        }

        /// <summary>
        /// 确保目录存在
        /// </summary>
        private void EnsureDirectories()
        {
            if (!string.IsNullOrEmpty(DataDirectory) && !Directory.Exists(DataDirectory))
                Directory.CreateDirectory(DataDirectory);

            if (!string.IsNullOrEmpty(ConfigDirectory) && !Directory.Exists(ConfigDirectory))
                Directory.CreateDirectory(ConfigDirectory);
        }

        /// <summary>
        /// 加载配置
        /// </summary>
        private void LoadConfiguration()
        {
            var configPath = Path.Combine(ConfigDirectory, $"{Id}.json");
            if (File.Exists(configPath))
            {
                try
                {
                    var json = File.ReadAllText(configPath);
                    _configuration = JsonConvert.DeserializeObject<Dictionary<string, object>>(json)
                        ?? new Dictionary<string, object>();
                }
                catch (Exception ex)
                {
                    Logger?.Warning(Id, $"加载配置文件失败: {ex.Message}");
                    _configuration = new Dictionary<string, object>();
                }
            }
        }

        /// <summary>
        /// 保存配置
        /// </summary>
        private void SaveConfiguration()
        {
            try
            {
                var configPath = Path.Combine(ConfigDirectory, $"{Id}.json");
                var json = JsonConvert.SerializeObject(_configuration, Formatting.Indented);
                File.WriteAllText(configPath, json);
            }
            catch (Exception ex)
            {
                Logger?.Error(Id, $"保存配置文件失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 子类可重写的初始化方法
        /// </summary>
        protected virtual void OnInitialize() { }

        /// <summary>
        /// 子类可重写的启动方法
        /// </summary>
        protected virtual void OnStart() { }

        /// <summary>
        /// 子类可重写的停止方法
        /// </summary>
        protected virtual void OnStop() { }

        /// <summary>
        /// 子类可重写的释放方法
        /// </summary>
        protected virtual void OnDispose() { }

        /// <summary>
        /// 子类可重写的配置变更处理方法
        /// </summary>
        protected virtual void OnConfigurationChanged() { }

        /// <summary>
        /// 获取插件自述文档（HTML格式）
        /// </summary>
        public virtual string GetReadme()
        {
            // 默认返回空，子类可重写
            return $"<h2>{Name}</h2><p>{Description}</p><p>版本: {Version}</p><p>作者: {Author}</p>";
        }

        /// <summary>
        /// 获取插件权限列表
        /// </summary>
        public virtual List<string> GetPermissions()
        {
            // 从 Api 获取实际注册的权限（使用当前插件的ID）
            var permissions = new List<string>();

            if (Api != null)
            {
                var registeredPerms = Api.GetPluginPermissions(Id);
                if (registeredPerms != null && registeredPerms.Count > 0)
                {
                    permissions.AddRange(registeredPerms);
                }
            }

            // 如果没有注册任何权限，显示基础权限
            if (permissions.Count == 0)
            {
                permissions.Add("基础插件功能（无特殊权限）");
            }

            return permissions;
        }

        /// <summary>
        /// 获取插件权限信息（系统识别 + 插件自述）
        /// </summary>
        public virtual PluginPermissionsInfo GetPermissionsInfo()
        {
            var info = new PluginPermissionsInfo();

            // 从 Api 获取系统自动识别的权限（使用当前插件的ID）
            if (Api != null)
            {
                var registeredPerms = Api.GetPluginPermissions(Id);
                if (registeredPerms != null && registeredPerms.Count > 0)
                {
                    info.SystemPermissions.AddRange(registeredPerms);
                }
            }

            // 如果没有系统权限，添加基础说明
            if (info.SystemPermissions.Count == 0)
            {
                info.SystemPermissions.Add("基础插件功能（无特殊权限）");
            }

            return info;
        }
    }
}
