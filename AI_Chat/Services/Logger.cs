using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AI_Chat.Models;
using AI_Chat.Constants;
using Newtonsoft.Json;

namespace AI_Chat.Services
{
    public static class Logger
    {
        private static readonly object _logsLock = new object();
        private static List<LogEntry> _logs = new List<LogEntry>();
        private static Action<WebSocketMessage> _broadcastCallback;

        public static void Initialize(Action<WebSocketMessage> broadcastCallback)
        {
            _broadcastCallback = broadcastCallback;
        }

        public static void LogInfo(string source, string message)
        {
            AddLog("INFO", source, message);
            Console.WriteLine($"[{DateTime.Now:T}] [INFO] [{source}] {message}");
            LogToFile(AppConstants.GENERAL_LOG_SUBFOLDER, $"[INFO] [{source}] {message}");
        }

        public static void LogWarning(string source, string message)
        {
            AddLog("WARNING", source, message);
            Console.WriteLine($"[{DateTime.Now:T}] [WARN] [{source}] {message}");
            LogToFile(AppConstants.GENERAL_LOG_SUBFOLDER, $"[WARN] [{source}] {message}");
        }

        public static void LogError(string source, string message, Exception ex = null)
        {
            string fullMessage = ex != null ? $"{message}: {ex.Message}" : message;
            AddLog("ERROR", source, fullMessage);
            Console.WriteLine($"[{DateTime.Now:T}] [ERROR] [{source}] {fullMessage}");
            LogToFile(AppConstants.GENERAL_LOG_SUBFOLDER, $"[ERROR] [{source}] {fullMessage}");
        }

        private static void AddLog(string level, string source, string message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                message = Regex.Replace(message,
                    @"(?<=(?:key=|Access Key: ))([a-zA-Z0-9]{4})[a-zA-Z0-9]+([a-zA-Z0-9]{4})",
                    "$1****$2",
                    RegexOptions.IgnoreCase);
            }
            string safeMessage = WebUtility.HtmlEncode(message);
            string timestamp = DateTime.Now.ToString("HH:mm:ss");

            lock (_logsLock)
            {
                _logs.Add(new LogEntry
                {
                    Timestamp = timestamp,
                    Level = level,
                    Source = source,
                    Message = safeMessage
                });

                if (_logs.Count > AppConstants.MAX_LOGS) _logs.RemoveAt(0);
            }

            _broadcastCallback?.Invoke(new WebSocketMessage
            {
                Type = "log",
                Data = new
                {
                    timestamp = timestamp,
                    level = level,
                    source = source,
                    message = safeMessage
                }
            });
        }

        private static void LogToFile(string subfolder, string message)
        {
            try
            {
                message = Regex.Replace(message,
                    @"(?<=(?:key=|Access Key: ))([a-zA-Z0-9]{4})[a-zA-Z0-9]+([a-zA-Z0-9]{4})",
                    "$1****$2",
                    RegexOptions.IgnoreCase);

                string dir = Path.Combine(Environment.CurrentDirectory, AppConstants.LOG_ROOT_FOLDER, subfolder);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                File.AppendAllText(
                    Path.Combine(dir, $"{DateTime.Now:yyyy-MM-dd}.log"),
                    $"[{DateTime.Now:T}] {message}\n"
                );
            }
            catch { }
        }

        public static void LogAIContext(string hid, List<Message> context)
        {
            try
            {
                string dir = Path.Combine(Environment.CurrentDirectory, AppConstants.LOG_ROOT_FOLDER, AppConstants.CONTEXT_LOG_SUBFOLDER);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, $"{DateTime.Now:yyyy-MM-dd}_AI_Context.log"), $"\n{new string('-', 30)}\nHID: {hid}\n{JsonConvert.SerializeObject(context, Formatting.Indented)}\n");
            }
            catch { }
        }

        public static List<LogEntry> GetLogs()
        {
            lock (_logsLock) return _logs.ToList();
        }

        public static void ClearLogs()
        {
            lock (_logsLock) _logs.Clear();
        }
    }
}
