using System;
using System.Collections.Generic;

namespace AI_Chat.Plugins
{
    /// <summary>
    /// 服务提供者实现 - 管理插件间共享服务
    /// </summary>
    public class PluginServiceProvider : IServiceProvider
    {
        private readonly Dictionary<Type, object> _services;
        private readonly Dictionary<string, object> _namedServices;
        private readonly object _lock = new object();

        public PluginServiceProvider()
        {
            _services = new Dictionary<Type, object>();
            _namedServices = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 注册服务
        /// </summary>
        public void RegisterService<T>(T service) where T : class
        {
            if (service == null)
                throw new ArgumentNullException(nameof(service));

            lock (_lock)
            {
                _services[typeof(T)] = service;
            }
        }

        /// <summary>
        /// 注册服务（带名称）
        /// </summary>
        public void RegisterService<T>(string name, T service) where T : class
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("服务名称不能为空", nameof(name));

            if (service == null)
                throw new ArgumentNullException(nameof(service));

            lock (_lock)
            {
                var key = $"{typeof(T).FullName}:{name}";
                _namedServices[key] = service;
            }
        }

        /// <summary>
        /// 获取服务
        /// </summary>
        public T GetService<T>() where T : class
        {
            lock (_lock)
            {
                if (_services.TryGetValue(typeof(T), out var service))
                {
                    return service as T;
                }
                return null;
            }
        }

        /// <summary>
        /// 获取服务（带名称）
        /// </summary>
        public T GetService<T>(string name) where T : class
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            lock (_lock)
            {
                var key = $"{typeof(T).FullName}:{name}";
                if (_namedServices.TryGetValue(key, out var service))
                {
                    return service as T;
                }
                return null;
            }
        }

        /// <summary>
        /// 检查服务是否存在
        /// </summary>
        public bool HasService<T>() where T : class
        {
            lock (_lock)
            {
                return _services.ContainsKey(typeof(T));
            }
        }

        /// <summary>
        /// 检查服务是否存在（带名称）
        /// </summary>
        public bool HasService<T>(string name) where T : class
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            lock (_lock)
            {
                var key = $"{typeof(T).FullName}:{name}";
                return _namedServices.ContainsKey(key);
            }
        }

        /// <summary>
        /// 注销服务
        /// </summary>
        public void UnregisterService<T>() where T : class
        {
            lock (_lock)
            {
                _services.Remove(typeof(T));
            }
        }

        /// <summary>
        /// 注销服务（带名称）
        /// </summary>
        public void UnregisterService<T>(string name) where T : class
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            lock (_lock)
            {
                var key = $"{typeof(T).FullName}:{name}";
                _namedServices.Remove(key);
            }
        }

        /// <summary>
        /// 获取所有已注册的服务类型
        /// </summary>
        public IEnumerable<Type> GetRegisteredServiceTypes()
        {
            lock (_lock)
            {
                return new List<Type>(_services.Keys);
            }
        }

        /// <summary>
        /// 清除所有服务
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _services.Clear();
                _namedServices.Clear();
            }
        }
    }
}
