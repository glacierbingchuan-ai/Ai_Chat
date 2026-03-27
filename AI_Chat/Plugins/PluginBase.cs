using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace AI_Chat.Plugins
{
    /// <summary>
    /// 插件基类 - 所有插件应继承此类
    /// 插件信息从 [Plugin] 特性自动读取，无需在代码中重复定义
    /// </summary>
    public abstract class PluginBase : IPlugin
    {
        private PluginContext _context;
        private PluginDataHelper _dataHelper;
        private readonly Dictionary<string, MethodInfo> _commandHandlers;
        private readonly Dictionary<string, MethodInfo> _capabilityHandlers;
        private readonly Dictionary<string, PluginCapability> _capabilities;
        private readonly PluginAttribute _attribute;

        // 从特性自动读取插件信息
        public virtual string Id => _attribute?.Id ?? GetType().FullName;
        public virtual string Name => _attribute?.Name ?? GetType().Name;
        public virtual Version Version => ParseVersion(_attribute?.Version);
        public virtual string Author => _attribute?.Author ?? "Unknown";
        public virtual string Description => _attribute?.Description ?? string.Empty;

        public PluginState State { get; protected set; }

        protected PluginContext Context => _context;
        protected IPluginLogger Logger => _context?.Logger;
        protected IServiceProvider Services => _context?.ServiceProvider;
        protected IPluginManager PluginManager => _context?.PluginManager;
        protected IPluginApi Api => _context?.ServiceProvider?.GetService<IPluginApi>();

        /// <summary>
        /// 插件数据帮助类 - 提供配置和数据文件操作
        /// </summary>
        public PluginDataHelper Data => _dataHelper;

        protected PluginBase()
        {
            State = PluginState.Unloaded;
            _commandHandlers = new Dictionary<string, MethodInfo>(StringComparer.OrdinalIgnoreCase);
            _capabilityHandlers = new Dictionary<string, MethodInfo>(StringComparer.OrdinalIgnoreCase);
            _capabilities = new Dictionary<string, PluginCapability>(StringComparer.OrdinalIgnoreCase);
            _dataHelper = null;

            // 读取 Plugin 特性
            _attribute = GetType().GetCustomAttribute<PluginAttribute>();

            RegisterCommands();
            RegisterCapabilities();
        }

        private Version ParseVersion(string versionString)
        {
            if (string.IsNullOrEmpty(versionString))
                return new Version(1, 0, 0);
            
            if (Version.TryParse(versionString, out var version))
                return version;
            
            return new Version(1, 0, 0);
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
                _dataHelper = new PluginDataHelper(Id, _context.DataDirectory, _context.ConfigDirectory);
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
                _dataHelper?.SaveConfig();
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
        /// 获取插件自述文档（HTML格式）
        /// </summary>
        public virtual string GetReadme()
        {
            return $"<h2>{Name}</h2><p>{Description}</p><p>版本: {Version}</p><p>作者: {Author}</p>";
        }

        /// <summary>
        /// 获取插件权限列表
        /// </summary>
        public virtual List<string> GetPermissions()
        {
            var permissions = new List<string>();

            if (Api != null)
            {
                var registeredPerms = Api.GetPluginPermissions(Id);
                if (registeredPerms != null && registeredPerms.Count > 0)
                {
                    permissions.AddRange(registeredPerms);
                }
            }

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

            if (Api != null)
            {
                var registeredPerms = Api.GetPluginPermissions(Id);
                if (registeredPerms != null && registeredPerms.Count > 0)
                {
                    info.SystemPermissions.AddRange(registeredPerms);
                }
            }

            if (info.SystemPermissions.Count == 0)
            {
                info.SystemPermissions.Add("基础插件功能（无特殊权限）");
            }

            return info;
        }

        #region 插件能力声明与调用

        /// <summary>
        /// 注册能力（通过特性自动扫描）
        /// </summary>
        private void RegisterCapabilities()
        {
            var methods = GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance);

            foreach (var method in methods)
            {
                var attr = method.GetCustomAttribute<PluginCapabilityAttribute>();
                if (attr != null)
                {
                    var capability = new PluginCapability
                    {
                        Name = attr.Name,
                        Description = attr.Description,
                        MethodName = method.Name,
                        Parameters = new List<CapabilityParameter>()
                    };

                    // 解析方法参数
                    var parameters = method.GetParameters();
                    foreach (var param in parameters)
                    {
                        var paramAttr = param.GetCustomAttribute<CapabilityParamAttribute>();
                        capability.Parameters.Add(new CapabilityParameter
                        {
                            Name = param.Name,
                            Type = GetFriendlyTypeName(param.ParameterType),
                            Description = paramAttr?.Description ?? $"参数 {param.Name}",
                            Required = paramAttr?.Required ?? !param.IsOptional,
                            DefaultValue = paramAttr?.DefaultValue ?? (param.HasDefaultValue ? param.DefaultValue : null)
                        });
                    }

                    _capabilities[attr.Name] = capability;
                    _capabilityHandlers[attr.Name] = method;
                }
            }
        }

        /// <summary>
        /// 获取类型友好名称
        /// </summary>
        private string GetFriendlyTypeName(Type type)
        {
            if (type == typeof(string)) return "string";
            if (type == typeof(int) || type == typeof(int?)) return "int";
            if (type == typeof(long) || type == typeof(long?)) return "long";
            if (type == typeof(double) || type == typeof(double?)) return "double";
            if (type == typeof(float) || type == typeof(float?)) return "float";
            if (type == typeof(bool) || type == typeof(bool?)) return "bool";
            if (type == typeof(DateTime) || type == typeof(DateTime?)) return "datetime";
            return type.Name.ToLower();
        }

        /// <summary>
        /// 获取插件声明的能力列表
        /// </summary>
        public virtual List<PluginCapability> GetCapabilities()
        {
            return _capabilities.Values.ToList();
        }

        /// <summary>
        /// 调用插件能力
        /// </summary>
        public virtual PluginCapabilityResult InvokeCapability(string capabilityName, Dictionary<string, object> parameters)
        {
            if (string.IsNullOrWhiteSpace(capabilityName))
                return PluginCapabilityResult.ErrorResult("能力名称不能为空");

            if (!_capabilityHandlers.TryGetValue(capabilityName, out var method))
            {
                return PluginCapabilityResult.ErrorResult($"能力 '{capabilityName}' 不存在");
            }

            if (!_capabilities.TryGetValue(capabilityName, out var capability))
            {
                return PluginCapabilityResult.ErrorResult($"能力 '{capabilityName}' 定义不存在");
            }

            try
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                // 准备参数（按方法签名顺序）
                var methodParams = method.GetParameters();
                var invokeArgs = new object[methodParams.Length];

                for (int i = 0; i < methodParams.Length; i++)
                {
                    var param = methodParams[i];
                    var paramDef = capability.Parameters.FirstOrDefault(p =>
                        p.Name.Equals(param.Name, StringComparison.OrdinalIgnoreCase));

                    if (parameters != null && parameters.TryGetValue(param.Name, out var value))
                    {
                        // 转换参数类型
                        invokeArgs[i] = ConvertParameter(value, param.ParameterType);
                    }
                    else if (param.HasDefaultValue)
                    {
                        invokeArgs[i] = param.DefaultValue;
                    }
                    else if (paramDef?.DefaultValue != null)
                    {
                        invokeArgs[i] = ConvertParameter(paramDef.DefaultValue, param.ParameterType);
                    }
                    else
                    {
                        return PluginCapabilityResult.ErrorResult($"缺少必需参数: {param.Name}");
                    }
                }

                // 调用方法
                var result = method.Invoke(this, invokeArgs);
                stopwatch.Stop();

                // 处理异步方法
                if (result is Task task)
                {
                    task.GetAwaiter().GetResult();
                    var resultProperty = task.GetType().GetProperty("Result");
                    result = resultProperty?.GetValue(task);
                }

                return new PluginCapabilityResult
                {
                    Success = true,
                    Data = result,
                    ExecutionTimeMs = stopwatch.ElapsedMilliseconds
                };
            }
            catch (TargetInvocationException ex)
            {
                Logger?.Error(Id, $"调用能力 '{capabilityName}' 失败", ex.InnerException);
                return PluginCapabilityResult.ErrorResult(ex.InnerException?.Message ?? ex.Message);
            }
            catch (Exception ex)
            {
                Logger?.Error(Id, $"调用能力 '{capabilityName}' 失败", ex);
                return PluginCapabilityResult.ErrorResult(ex.Message);
            }
        }

        /// <summary>
        /// 转换参数类型
        /// </summary>
        private object ConvertParameter(object value, Type targetType)
        {
            if (value == null) return null;
            if (targetType.IsInstanceOfType(value)) return value;

            // 处理可空类型
            var underlyingType = Nullable.GetUnderlyingType(targetType);
            if (underlyingType != null)
            {
                if (value == null || (value is string str && string.IsNullOrEmpty(str)))
                    return null;
                targetType = underlyingType;
            }

            try
            {
                if (targetType == typeof(string))
                    return value?.ToString();
                if (targetType == typeof(int) || targetType == typeof(long))
                    return Convert.ToInt64(value);
                if (targetType == typeof(double))
                    return Convert.ToDouble(value);
                if (targetType == typeof(float))
                    return Convert.ToSingle(value);
                if (targetType == typeof(bool))
                {
                    if (value is string s)
                        return bool.Parse(s);
                    return Convert.ToBoolean(value);
                }
                if (targetType == typeof(DateTime))
                    return Convert.ToDateTime(value);

                return Convert.ChangeType(value, targetType);
            }
            catch
            {
                return value;
            }
        }

        #endregion
    }
}
