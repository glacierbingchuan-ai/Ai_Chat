using System;
using AI_Chat.Services;

namespace AI_Chat.Plugins
{
    /// <summary>
    /// 插件日志实现 - 包装系统日志服务
    /// </summary>
    public class PluginLogger : IPluginLogger
    {
        public void Debug(string pluginId, string message)
        {
            // 系统Logger没有Debug方法，使用Info代替
            Logger.LogInfo(pluginId, $"[DEBUG] {message}");
        }

        public void Info(string pluginId, string message)
        {
            Logger.LogInfo(pluginId, message);
        }

        public void Warning(string pluginId, string message)
        {
            Logger.LogWarning(pluginId, message);
        }

        public void Error(string pluginId, string message)
        {
            Logger.LogError(pluginId, message);
        }

        public void Error(string pluginId, string message, Exception exception)
        {
            Logger.LogError(pluginId, message, exception);
        }

        public void Fatal(string pluginId, string message)
        {
            Logger.LogError(pluginId, $"[FATAL] {message}");
        }

        public void Fatal(string pluginId, string message, Exception exception)
        {
            Logger.LogError(pluginId, $"[FATAL] {message}", exception);
        }
    }
}
