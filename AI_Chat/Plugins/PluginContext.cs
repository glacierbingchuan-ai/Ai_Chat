using System;
using System.Collections.Generic;
using AI_Chat.Services;

namespace AI_Chat.Plugins
{
    /// <summary>
    /// 插件上下文 - 提供给插件的宿主环境信息
    /// </summary>
    public class PluginContext
    {
        /// <summary>
        /// 应用程序名称
        /// </summary>
        public string ApplicationName { get; set; }

        /// <summary>
        /// 应用程序版本
        /// </summary>
        public Version ApplicationVersion { get; set; }

        /// <summary>
        /// 插件目录路径
        /// </summary>
        public string PluginDirectory { get; set; }

        /// <summary>
        /// 插件数据目录路径
        /// </summary>
        public string DataDirectory { get; set; }

        /// <summary>
        /// 插件配置目录路径
        /// </summary>
        public string ConfigDirectory { get; set; }

        /// <summary>
        /// 日志服务
        /// </summary>
        public IPluginLogger Logger { get; set; }

        /// <summary>
        /// 服务提供者
        /// </summary>
        public IServiceProvider ServiceProvider { get; set; }

        /// <summary>
        /// 配置管理器
        /// </summary>
        public ConfigManager ConfigManager { get; set; }

        /// <summary>
        /// 插件管理器
        /// </summary>
        public IPluginManager PluginManager { get; set; }

        /// <summary>
        /// 全局配置字典
        /// </summary>
        public Dictionary<string, object> GlobalSettings { get; set; }

        public PluginContext()
        {
            GlobalSettings = new Dictionary<string, object>();
        }
    }

    /// <summary>
    /// 插件日志接口
    /// </summary>
    public interface IPluginLogger
    {
        void Debug(string pluginId, string message);
        void Info(string pluginId, string message);
        void Warning(string pluginId, string message);
        void Error(string pluginId, string message);
        void Error(string pluginId, string message, Exception exception);
        void Fatal(string pluginId, string message);
        void Fatal(string pluginId, string message, Exception exception);
    }

    /// <summary>
    /// 服务提供者接口
    /// </summary>
    public interface IServiceProvider
    {
        /// <summary>
        /// 注册服务
        /// </summary>
        void RegisterService<T>(T service) where T : class;

        /// <summary>
        /// 注册服务（带名称）
        /// </summary>
        void RegisterService<T>(string name, T service) where T : class;

        /// <summary>
        /// 获取服务
        /// </summary>
        T GetService<T>() where T : class;

        /// <summary>
        /// 获取服务（带名称）
        /// </summary>
        T GetService<T>(string name) where T : class;

        /// <summary>
        /// 检查服务是否存在
        /// </summary>
        bool HasService<T>() where T : class;

        /// <summary>
        /// 检查服务是否存在（带名称）
        /// </summary>
        bool HasService<T>(string name) where T : class;
    }

    /// <summary>
    /// 插件管理器接口
    /// </summary>
    public interface IPluginManager
    {
        /// <summary>
        /// 获取所有已加载的插件
        /// </summary>
        IEnumerable<IPlugin> GetAllPlugins();

        /// <summary>
        /// 获取指定插件
        /// </summary>
        IPlugin GetPlugin(string pluginId);

        /// <summary>
        /// 获取插件信息
        /// </summary>
        PluginInfo GetPluginInfo(string pluginId);

        /// <summary>
        /// 检查插件是否已加载
        /// </summary>
        bool IsPluginLoaded(string pluginId);

        /// <summary>
        /// 加载插件
        /// </summary>
        bool LoadPlugin(string assemblyPath);

        /// <summary>
        /// 卸载插件
        /// </summary>
        bool UnloadPlugin(string pluginId);

        /// <summary>
        /// 重新加载插件
        /// </summary>
        bool ReloadPlugin(string pluginId);

        /// <summary>
        /// 启动插件
        /// </summary>
        bool StartPlugin(string pluginId);

        /// <summary>
        /// 停止插件
        /// </summary>
        bool StopPlugin(string pluginId);

        /// <summary>
        /// 执行插件命令
        /// </summary>
        object ExecuteCommand(string pluginId, string command, Dictionary<string, object> parameters);
    }
}
