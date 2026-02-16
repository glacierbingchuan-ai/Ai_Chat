using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace AI_Chat.Plugins
{
    /// <summary>
    /// 插件加载器 - 负责从程序集加载插件
    /// </summary>
    public class PluginLoader
    {
        private readonly Dictionary<string, Assembly> _loadedAssemblies;
        private readonly object _lock = new object();

        public PluginLoader()
        {
            _loadedAssemblies = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 从程序集文件加载插件
        /// </summary>
        public PluginLoadResult LoadPlugin(string assemblyPath)
        {
            if (!File.Exists(assemblyPath))
            {
                return PluginLoadResult.FailureResult($"程序集文件不存在: {assemblyPath}");
            }

            try
            {
                Assembly assembly;
                lock (_lock)
                {
                    if (_loadedAssemblies.TryGetValue(assemblyPath, out assembly))
                    {
                        // 程序集已加载，直接返回插件信息（不重复加载）
                        var existingPluginTypes = FindPluginTypes(assembly);
                        if (existingPluginTypes.Count == 0)
                        {
                            return PluginLoadResult.FailureResult("程序集中未找到插件类型");
                        }

                        var existingPluginType = existingPluginTypes[0];
                        var existingAttribute = existingPluginType.GetCustomAttribute<PluginAttribute>();

                        var existingPluginInfo = new PluginInfo
                        {
                            Id = existingAttribute?.Id ?? existingPluginType.FullName,
                            Name = existingAttribute?.Name ?? existingPluginType.Name,
                            Version = existingAttribute?.Version ?? "1.0.0",
                            Author = existingAttribute?.Author ?? "Unknown",
                            Description = existingAttribute?.Description ?? string.Empty,
                            Dependencies = existingAttribute?.Dependencies?.ToList() ?? new List<string>(),
                            Priority = existingAttribute?.Priority ?? 100,
                            AutoStart = existingAttribute?.AutoStart ?? false,
                            AssemblyPath = assemblyPath,
                            TypeName = existingPluginType.FullName,
                            State = PluginState.Loaded
                        };

                        return PluginLoadResult.SuccessResult(existingPluginInfo);
                    }

                    // 读取DLL到内存，避免锁定文件
                    byte[] assemblyBytes = File.ReadAllBytes(assemblyPath);
                    assembly = Assembly.Load(assemblyBytes);
                    _loadedAssemblies[assemblyPath] = assembly;
                }

                var pluginTypes = FindPluginTypes(assembly);

                if (pluginTypes.Count == 0)
                {
                    return PluginLoadResult.FailureResult("程序集中未找到插件类型");
                }

                var pluginType = pluginTypes[0];
                var attribute = pluginType.GetCustomAttribute<PluginAttribute>();

                var pluginInfo = new PluginInfo
                {
                    Id = attribute?.Id ?? pluginType.FullName,
                    Name = attribute?.Name ?? pluginType.Name,
                    Version = attribute?.Version ?? "1.0.0",
                    Author = attribute?.Author ?? "Unknown",
                    Description = attribute?.Description ?? string.Empty,
                    Dependencies = attribute?.Dependencies?.ToList() ?? new List<string>(),
                    Priority = attribute?.Priority ?? 100,
                    AutoStart = attribute?.AutoStart ?? false,
                    AssemblyPath = assemblyPath,
                    TypeName = pluginType.FullName,
                    State = PluginState.Loaded
                };

                return PluginLoadResult.SuccessResult(pluginInfo);
            }
            catch (Exception ex)
            {
                return PluginLoadResult.FailureResult($"加载程序集失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 从程序集文件加载多个插件
        /// </summary>
        public List<PluginLoadResult> LoadPluginsFromDirectory(string directoryPath, string searchPattern = "*.dll")
        {
            var results = new List<PluginLoadResult>();

            if (!Directory.Exists(directoryPath))
            {
                return results;
            }

            var assemblyFiles = Directory.GetFiles(directoryPath, searchPattern);

            foreach (var file in assemblyFiles)
            {
                var result = LoadPlugin(file);
                results.Add(result);
            }

            return results;
        }

        /// <summary>
        /// 创建插件实例
        /// </summary>
        public IPlugin CreatePluginInstance(PluginInfo pluginInfo)
        {
            if (pluginInfo == null)
                throw new ArgumentNullException(nameof(pluginInfo));

            try
            {
                Assembly assembly;
                lock (_lock)
                {
                    if (!_loadedAssemblies.TryGetValue(pluginInfo.AssemblyPath, out assembly))
                    {
                        // 读取DLL到内存，避免锁定文件
                        byte[] assemblyBytes = File.ReadAllBytes(pluginInfo.AssemblyPath);
                        assembly = Assembly.Load(assemblyBytes);
                        _loadedAssemblies[pluginInfo.AssemblyPath] = assembly;
                    }
                }

                var pluginType = assembly.GetType(pluginInfo.TypeName);
                if (pluginType == null)
                {
                    throw new TypeLoadException($"无法找到类型: {pluginInfo.TypeName}");
                }

                if (!typeof(IPlugin).IsAssignableFrom(pluginType))
                {
                    throw new InvalidCastException($"类型 {pluginInfo.TypeName} 未实现 IPlugin 接口");
                }

                var instance = (IPlugin)Activator.CreateInstance(pluginType);
                return instance;
            }
            catch (Exception ex)
            {
                throw new Exception($"创建插件实例失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 查找程序集中的所有插件类型
        /// </summary>
        private List<Type> FindPluginTypes(Assembly assembly)
        {
            var pluginTypes = new List<Type>();

            try
            {
                var types = assembly.GetTypes();

                foreach (var type in types)
                {
                    if (type.IsAbstract || type.IsInterface)
                        continue;

                    if (typeof(IPlugin).IsAssignableFrom(type))
                    {
                        pluginTypes.Add(type);
                    }
                }
            }
            catch (ReflectionTypeLoadException ex)
            {
                var loadedTypes = ex.Types.Where(t => t != null);
                foreach (var type in loadedTypes)
                {
                    if (type.IsAbstract || type.IsInterface)
                        continue;

                    if (typeof(IPlugin).IsAssignableFrom(type))
                    {
                        pluginTypes.Add(type);
                    }
                }
            }

            return pluginTypes;
        }

        /// <summary>
        /// 卸载程序集（注：.NET Framework 中无法真正卸载程序集，除非卸载整个 AppDomain）
        /// </summary>
        public bool UnloadAssembly(string assemblyPath)
        {
            lock (_lock)
            {
                return _loadedAssemblies.Remove(assemblyPath);
            }
        }

        /// <summary>
        /// 获取已加载的程序集列表
        /// </summary>
        public IEnumerable<string> GetLoadedAssemblyPaths()
        {
            lock (_lock)
            {
                return new List<string>(_loadedAssemblies.Keys);
            }
        }

        /// <summary>
        /// 检查程序集是否已加载
        /// </summary>
        public bool IsAssemblyLoaded(string assemblyPath)
        {
            lock (_lock)
            {
                return _loadedAssemblies.ContainsKey(assemblyPath);
            }
        }

        /// <summary>
        /// 获取程序集信息
        /// </summary>
        public AssemblyInfo GetAssemblyInfo(string assemblyPath)
        {
            if (!File.Exists(assemblyPath))
                return null;

            try
            {
                var assemblyName = AssemblyName.GetAssemblyName(assemblyPath);
                var fileInfo = new FileInfo(assemblyPath);

                return new AssemblyInfo
                {
                    Name = assemblyName.Name,
                    Version = assemblyName.Version.ToString(),
                    FullName = assemblyName.FullName,
                    Path = assemblyPath,
                    Size = fileInfo.Length,
                    LastModified = fileInfo.LastWriteTime
                };
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// 程序集信息
    /// </summary>
    public class AssemblyInfo
    {
        public string Name { get; set; }
        public string Version { get; set; }
        public string FullName { get; set; }
        public string Path { get; set; }
        public long Size { get; set; }
        public DateTime LastModified { get; set; }
    }
}
