using System;
using System.Collections.Generic;

namespace AI_Chat.Plugins
{
    /// <summary>
    /// 插件信息类
    /// </summary>
    public class PluginInfo
    {
        /// <summary>
        /// 插件ID
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// 插件名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 插件版本
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// 插件作者
        /// </summary>
        public string Author { get; set; }

        /// <summary>
        /// 插件描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 依赖的其他插件ID列表
        /// </summary>
        public List<string> Dependencies { get; set; }

        /// <summary>
        /// 插件优先级
        /// </summary>
        public int Priority { get; set; }

        /// <summary>
        /// 是否自动启动
        /// </summary>
        public bool AutoStart { get; set; }

        /// <summary>
        /// 程序集路径
        /// </summary>
        public string AssemblyPath { get; set; }

        /// <summary>
        /// 插件类型全名
        /// </summary>
        public string TypeName { get; set; }

        /// <summary>
        /// 加载时间
        /// </summary>
        public DateTime LoadTime { get; set; }

        /// <summary>
        /// 当前状态
        /// </summary>
        public PluginState State { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsEnabled { get; set; }

        /// <summary>
        /// 错误信息（如果有）
        /// </summary>
        public string ErrorMessage { get; set; }

        public PluginInfo()
        {
            Dependencies = new List<string>();
            LoadTime = DateTime.Now;
            State = PluginState.Unloaded;
            IsEnabled = true;
        }
    }

    /// <summary>
    /// 插件加载结果
    /// </summary>
    public class PluginLoadResult
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 插件信息
        /// </summary>
        public PluginInfo PluginInfo { get; set; }

        /// <summary>
        /// 错误信息
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// 异常对象
        /// </summary>
        public Exception Exception { get; set; }

        public static PluginLoadResult SuccessResult(PluginInfo info)
        {
            return new PluginLoadResult
            {
                Success = true,
                PluginInfo = info
            };
        }

        public static PluginLoadResult FailureResult(string error, Exception ex = null)
        {
            return new PluginLoadResult
            {
                Success = false,
                ErrorMessage = error,
                Exception = ex
            };
        }
    }
}
