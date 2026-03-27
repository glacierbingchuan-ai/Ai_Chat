using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using AI_Chat.Models;
using AI_Chat.Constants;
using AI_Chat.Utils;
using Newtonsoft.Json;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.Async;
using Message = AI_Chat.Models.Message;
using SerilogLog = Serilog.Log;

namespace AI_Chat.Services
{
    public static class Logger
    {
        private static readonly object _logsLock = new object();
        private static List<LogEntry> _logs = new List<LogEntry>();
        private static Action<WebSocketMessage> _broadcastCallback;
        private static LoggingLevelSwitch _levelSwitch;

        // 控制台 Logger（不打码）
        private static ILogger _consoleLogger;
        // 文件 Logger（打码）
        private static ILogger _fileLogger;

        public static void Initialize(Action<WebSocketMessage> broadcastCallback)
        {
            _broadcastCallback = broadcastCallback;
            InitializeSerilog();
        }

        private static void InitializeSerilog()
        {
            _levelSwitch = new LoggingLevelSwitch(LogEventLevel.Debug);

            var logDirectory = PathUtils.GetLogDirectory(AppConstants.GENERAL_LOG_SUBFOLDER);

            // 创建控制台 Logger（不打码）
            _consoleLogger = new LoggerConfiguration()
                .MinimumLevel.ControlledBy(_levelSwitch)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("SourceContext", "AI_Chat")
                .WriteTo.Async(a => a.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{Source}] {Message:lj}{NewLine}{Exception}"
                ))
                .CreateLogger();

            // 创建文件 Logger（打码）
            _fileLogger = new LoggerConfiguration()
                .MinimumLevel.ControlledBy(_levelSwitch)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("SourceContext", "AI_Chat")
                .WriteTo.Async(a => a.File(
                    path: Path.Combine(logDirectory, $"{DateTime.Now:yyyy-MM-dd}.log"),
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{Source}] {Message:lj}{NewLine}{Exception}",
                    shared: true,
                    flushToDiskInterval: TimeSpan.FromSeconds(5),
                    rollOnFileSizeLimit: true,
                    fileSizeLimitBytes: 10 * 1024 * 1024,
                    retainedFileCountLimit: 30
                ))
                .CreateLogger();

            // 设置全局 Logger 为控制台 Logger（用于兼容性）
            SerilogLog.Logger = _consoleLogger;

            LogInfo("SYSTEM", "Serilog initialized successfully");
        }

        public static void SetMinimumLevel(LogEventLevel level)
        {
            _levelSwitch.MinimumLevel = level;
        }

        public static void CloseAndFlush()
        {
            (_consoleLogger as IDisposable)?.Dispose();
            (_fileLogger as IDisposable)?.Dispose();
            SerilogLog.CloseAndFlush();
        }

        public static void LogDebug(string source, string message)
        {
            AddLog("DEBUG", source, message);
            // 控制台输出原始消息
            _consoleLogger?.ForContext("Source", source).Debug("{Message}", message);
            // 文件输出打码消息
            _fileLogger?.ForContext("Source", source).Debug("{Message}", SanitizeMessage(message));
        }

        public static void LogInfo(string source, string message)
        {
            AddLog("INFO", source, message);
            // 控制台输出原始消息
            _consoleLogger?.ForContext("Source", source).Information("{Message}", message);
            // 文件输出打码消息
            _fileLogger?.ForContext("Source", source).Information("{Message}", SanitizeMessage(message));
        }

        public static void LogWarning(string source, string message)
        {
            AddLog("WARNING", source, message);
            // 控制台输出原始消息
            _consoleLogger?.ForContext("Source", source).Warning("{Message}", message);
            // 文件输出打码消息
            _fileLogger?.ForContext("Source", source).Warning("{Message}", SanitizeMessage(message));
        }

        public static void LogError(string source, string message, Exception ex = null)
        {
            string fullMessage = ex != null ? $"{message}: {ex.Message}" : message;
            AddLog("ERROR", source, fullMessage);

            if (ex != null)
            {
                _consoleLogger?.ForContext("Source", source).Error(ex, "{Message}", message);
                _fileLogger?.ForContext("Source", source).Error(ex, "{Message}", SanitizeMessage(message));
            }
            else
            {
                _consoleLogger?.ForContext("Source", source).Error("{Message}", message);
                _fileLogger?.ForContext("Source", source).Error("{Message}", SanitizeMessage(message));
            }
        }

        public static void LogFatal(string source, string message, Exception ex = null)
        {
            string fullMessage = ex != null ? $"{message}: {ex.Message}" : message;
            AddLog("FATAL", source, fullMessage);

            if (ex != null)
            {
                _consoleLogger?.ForContext("Source", source).Fatal(ex, "{Message}", message);
                _fileLogger?.ForContext("Source", source).Fatal(ex, "{Message}", SanitizeMessage(message));
            }
            else
            {
                _consoleLogger?.ForContext("Source", source).Fatal("{Message}", message);
                _fileLogger?.ForContext("Source", source).Fatal("{Message}", SanitizeMessage(message));
            }
        }

        private static string SanitizeMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
                return message;

            return Regex.Replace(message,
                @"(?<=(?:key=|Access Key: ))([a-zA-Z0-9]{4})[a-zA-Z0-9]+([a-zA-Z0-9]{4})",
                "$1****$2",
                RegexOptions.IgnoreCase);
        }

        private static void AddLog(string level, string source, string message)
        {
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

        public static void LogAIContext(string hid, List<Message> context)
        {
            try
            {
                string dir = PathUtils.GetLogDirectory(AppConstants.CONTEXT_LOG_SUBFOLDER);
                string logPath = Path.Combine(dir, $"{DateTime.Now:yyyy-MM-dd}_AI_Context.log");
                string content = $"\n{new string('-', 30)}\nHID: {hid}\n{JsonConvert.SerializeObject(context, Formatting.Indented)}\n";

                File.AppendAllText(logPath, content);

                _consoleLogger.Debug("[AI_CONTEXT] HID: {Hid}, Context count: {Count}", hid, context?.Count ?? 0);
            }
            catch (Exception ex)
            {
                _consoleLogger.Error(ex, "[Logger] Failed to write AI context log");
            }
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
