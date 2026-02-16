using System;
using System.Collections.Generic;

namespace AI_Chat.Plugins
{
    /// <summary>
    /// 插件接口 - 所有插件必须实现此接口
    /// </summary>
    public interface IPlugin : IDisposable
    {
        /// <summary>
        /// 插件唯一标识符
        /// </summary>
        string Id { get; }

        /// <summary>
        /// 插件名称
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 插件版本
        /// </summary>
        Version Version { get; }

        /// <summary>
        /// 插件作者
        /// </summary>
        string Author { get; }

        /// <summary>
        /// 插件描述
        /// </summary>
        string Description { get; }

        /// <summary>
        /// 插件当前状态
        /// </summary>
        PluginState State { get; }

        /// <summary>
        /// 插件初始化
        /// </summary>
        /// <param name="context">插件上下文</param>
        void Initialize(PluginContext context);

        /// <summary>
        /// 启动插件
        /// </summary>
        void Start();

        /// <summary>
        /// 停止插件
        /// </summary>
        void Stop();

        /// <summary>
        /// 获取插件配置
        /// </summary>
        /// <returns>配置字典</returns>
        Dictionary<string, object> GetConfiguration();

        /// <summary>
        /// 设置插件配置
        /// </summary>
        /// <param name="configuration">配置字典</param>
        void SetConfiguration(Dictionary<string, object> configuration);

        /// <summary>
        /// 执行插件命令
        /// </summary>
        /// <param name="command">命令名称</param>
        /// <param name="parameters">命令参数</param>
        /// <returns>执行结果</returns>
        object ExecuteCommand(string command, Dictionary<string, object> parameters);

        /// <summary>
        /// 获取插件自述文档（HTML格式）
        /// </summary>
        /// <returns>HTML内容</returns>
        string GetReadme();

        /// <summary>
        /// 获取插件权限列表
        /// </summary>
        /// <returns>权限列表</returns>
        List<string> GetPermissions();
    }

    /// <summary>
    /// 插件权限信息
    /// </summary>
    public class PluginPermissionsInfo
    {
        /// <summary>
        /// 系统自动识别的权限（从API注册记录）
        /// </summary>
        public List<string> SystemPermissions { get; set; }

        /// <summary>
        /// 插件自述的额外权限
        /// </summary>
        public List<string> DeclaredPermissions { get; set; }

        public PluginPermissionsInfo()
        {
            SystemPermissions = new List<string>();
            DeclaredPermissions = new List<string>();
        }
    }

    /// <summary>
    /// 插件状态枚举
    /// </summary>
    public enum PluginState
    {
        /// <summary>
        /// 未加载
        /// </summary>
        Unloaded,

        /// <summary>
        /// 已加载
        /// </summary>
        Loaded,

        /// <summary>
        /// 初始化中
        /// </summary>
        Initializing,

        /// <summary>
        /// 已初始化
        /// </summary>
        Initialized,

        /// <summary>
        /// 运行中
        /// </summary>
        Running,

        /// <summary>
        /// 已停止
        /// </summary>
        Stopped,

        /// <summary>
        /// 发生错误
        /// </summary>
        Error,

        /// <summary>
        /// 已卸载
        /// </summary>
        Uninstalled
    }
}
