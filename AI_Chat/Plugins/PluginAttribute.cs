using System;

namespace AI_Chat.Plugins
{
    /// <summary>
    /// 插件特性 - 用于标记插件类
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public class PluginAttribute : Attribute
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
        public string[] Dependencies { get; set; }

        /// <summary>
        /// 插件优先级（数字越小优先级越高）
        /// </summary>
        public int Priority { get; set; }

        /// <summary>
        /// 是否自动启动
        /// </summary>
        public bool AutoStart { get; set; }

        public PluginAttribute()
        {
            Version = "1.0.0";
            Dependencies = new string[0];
            Priority = 100;
            AutoStart = false;
        }
    }

    /// <summary>
    /// 插件命令特性 - 用于标记插件命令方法
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public class PluginCommandAttribute : Attribute
    {
        /// <summary>
        /// 命令名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 命令描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 命令用法示例
        /// </summary>
        public string Usage { get; set; }

        public PluginCommandAttribute(string name)
        {
            Name = name;
        }
    }
}
