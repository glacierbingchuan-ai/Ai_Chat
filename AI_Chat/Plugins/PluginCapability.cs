using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace AI_Chat.Plugins
{
    /// <summary>
    /// 插件能力声明 - 用于描述插件可以执行的功能
    /// </summary>
    public class PluginCapability
    {
        /// <summary>
        /// 能力名称（英文标识符）
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 能力描述（用于展示给大模型）
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 能力参数定义
        /// </summary>
        public List<CapabilityParameter> Parameters { get; set; }

        /// <summary>
        /// 方法名称（用于内部调用）
        /// </summary>
        public string MethodName { get; set; }

        public PluginCapability()
        {
            Parameters = new List<CapabilityParameter>();
        }

        public override string ToString()
        {
            var paramStr = Parameters != null && Parameters.Count > 0
                ? string.Join(", ", Parameters.Select(p => $"{p.Name}:{p.Type}"))
                : "无参数";
            return $"{Name}({paramStr}) - {Description}";
        }
    }

    /// <summary>
    /// 能力参数定义
    /// </summary>
    public class CapabilityParameter
    {
        /// <summary>
        /// 参数名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 参数类型
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// 参数描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 是否必需
        /// </summary>
        public bool Required { get; set; }

        /// <summary>
        /// 默认值（可选）
        /// </summary>
        public object DefaultValue { get; set; }
    }

    /// <summary>
    /// 插件能力调用结果
    /// </summary>
    public class PluginCapabilityResult
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 结果数据
        /// </summary>
        public object Data { get; set; }

        /// <summary>
        /// 错误信息
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// 执行耗时（毫秒）
        /// </summary>
        public long ExecutionTimeMs { get; set; }

        public static PluginCapabilityResult SuccessResult(object data = null)
        {
            return new PluginCapabilityResult
            {
                Success = true,
                Data = data
            };
        }

        public static PluginCapabilityResult ErrorResult(string error)
        {
            return new PluginCapabilityResult
            {
                Success = false,
                ErrorMessage = error
            };
        }
    }

    /// <summary>
    /// 能力调用请求（大模型返回的调用指令）
    /// </summary>
    public class CapabilityInvokeRequest
    {
        /// <summary>
        /// 插件ID
        /// </summary>
        public string PluginId { get; set; }

        /// <summary>
        /// 能力名称
        /// </summary>
        public string CapabilityName { get; set; }

        /// <summary>
        /// 调用参数
        /// </summary>
        public Dictionary<string, object> Parameters { get; set; }

        public CapabilityInvokeRequest()
        {
            Parameters = new Dictionary<string, object>();
        }
    }

    /// <summary>
    /// 插件能力特性 - 用于标记插件能力方法
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public class PluginCapabilityAttribute : Attribute
    {
        /// <summary>
        /// 能力名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 能力描述
        /// </summary>
        public string Description { get; set; }

        public PluginCapabilityAttribute(string name, string description)
        {
            Name = name;
            Description = description;
        }
    }

    /// <summary>
    /// 能力参数特性 - 用于标记能力方法的参数
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
    public class CapabilityParamAttribute : Attribute
    {
        /// <summary>
        /// 参数描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 是否必需
        /// </summary>
        public bool Required { get; set; }

        /// <summary>
        /// 默认值
        /// </summary>
        public object DefaultValue { get; set; }

        public CapabilityParamAttribute(string description, bool required = true)
        {
            Description = description;
            Required = required;
        }
    }
}
