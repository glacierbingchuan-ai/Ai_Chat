using AI_Chat.Constants;
using AI_Chat.Managers;

namespace AI_Chat.Utils
{
    /// <summary>
    /// 配置工具类 - 提供统一的配置获取方法
    /// </summary>
    public static class ConfigUtils
    {
        /// <summary>
        /// 获取基础系统提示词
        /// </summary>
        /// <param name="userConfigManager">用户配置管理器</param>
        /// <param name="userId">用户ID</param>
        /// <returns>基础系统提示词</returns>
        public static string GetBaseSystemPrompt(UserConfigManager userConfigManager, long userId)
        {
            if (userConfigManager != null && userId > 0)
            {
                var userConfig = userConfigManager.GetUserConfig(userId);
                if (userConfig != null && !string.IsNullOrEmpty(userConfig.BaseSystemPrompt))
                {
                    return userConfig.BaseSystemPrompt;
                }
            }
            return SystemPrompts.BASE_SYSTEM_PROMPT;
        }

        /// <summary>
        /// 获取用户自定义提示词（如果有）
        /// </summary>
        public static string GetUserCustomPrompt(UserConfigManager userConfigManager, long userId)
        {
            if (userConfigManager != null && userId > 0)
            {
                var userConfig = userConfigManager.GetUserConfig(userId);
                if (userConfig != null && !string.IsNullOrEmpty(userConfig.BaseSystemPrompt))
                {
                    return userConfig.BaseSystemPrompt;
                }
            }
            return null;
        }
    }
}
