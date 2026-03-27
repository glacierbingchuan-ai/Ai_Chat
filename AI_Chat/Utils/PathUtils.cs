using System;
using System.IO;

namespace AI_Chat.Utils
{
    /// <summary>
    /// 路径工具类 - 提供统一的路径获取方法
    /// </summary>
    public static class PathUtils
    {
        private static readonly string _userDataBasePath;

        static PathUtils()
        {
            _userDataBasePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UserData");
        }

        /// <summary>
        /// 获取用户数据基础路径
        /// </summary>
        public static string UserDataBasePath => _userDataBasePath;

        /// <summary>
        /// 获取指定用户的数据目录
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <returns>用户数据目录路径</returns>
        public static string GetUserDirectory(long userId)
        {
            string userDir = Path.Combine(_userDataBasePath, userId.ToString());
            if (!Directory.Exists(userDir))
            {
                Directory.CreateDirectory(userDir);
            }
            return userDir;
        }

        /// <summary>
        /// 获取用户数据库文件路径
        /// </summary>
        public static string GetUserDatabasePath(long userId)
        {
            return Path.Combine(GetUserDirectory(userId), "chat_data.db");
        }

        /// <summary>
        /// 获取全局数据库文件路径
        /// </summary>
        public static string GetGlobalDatabasePath()
        {
            if (!Directory.Exists(_userDataBasePath))
            {
                Directory.CreateDirectory(_userDataBasePath);
            }
            return Path.Combine(_userDataBasePath, "chat_data.db");
        }

        /// <summary>
        /// 获取日志目录路径
        /// </summary>
        public static string GetLogDirectory(string subfolder = null)
        {
            string dir = Path.Combine(Environment.CurrentDirectory, Constants.AppConstants.LOG_ROOT_FOLDER);
            if (!string.IsNullOrEmpty(subfolder))
            {
                dir = Path.Combine(dir, subfolder);
            }
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            return dir;
        }

        /// <summary>
        /// 获取表情包目录路径
        /// </summary>
        public static string GetMemeDirectory()
        {
            string dir = Path.Combine(Environment.CurrentDirectory, "meme");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            return dir;
        }

        /// <summary>
        /// 获取配置文件路径
        /// </summary>
        public static string GetConfigFilePath()
        {
            return Path.Combine(Environment.CurrentDirectory, Constants.AppConstants.CONFIG_FILE_PATH);
        }

        /// <summary>
        /// 获取控制面板静态文件目录
        /// </summary>
        public static string GetControlPanelPublicPath()
        {
            return Path.Combine(Environment.CurrentDirectory, "ControlPanel", "public");
        }
    }
}
