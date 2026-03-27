using System;
using System.Text.RegularExpressions;

namespace AI_Chat.Utils
{
    /// <summary>
    /// 日期时间工具类 - 提供统一的时间解析和处理方法
    /// </summary>
    public static class DateTimeUtils
    {
        /// <summary>
        /// 健壮的时间解析方法 - 支持标准格式和 HH:mm:ss 格式
        /// </summary>
        /// <param name="timeStr">时间字符串</param>
        /// <param name="result">解析结果</param>
        /// <returns>是否解析成功</returns>
        public static bool TryParseRobustDateTime(string timeStr, out DateTime result)
        {
            if (DateTime.TryParse(timeStr, out result))
            {
                if (result.Year == 1) result = DateTime.Today.Add(result.TimeOfDay);
                return true;
            }

            var match = Regex.Match(timeStr, @"(\d{1,2})[:：](\d{1,2})[:：](\d{1,2})");
            if (match.Success)
            {
                result = DateTime.Today.Add(new TimeSpan(
                    int.Parse(match.Groups[1].Value),
                    int.Parse(match.Groups[2].Value),
                    int.Parse(match.Groups[3].Value)));
                return true;
            }

            return false;
        }

        /// <summary>
        /// 格式化时间为标准字符串
        /// </summary>
        public static string FormatToStandardString(DateTime dateTime, string format = "yyyy-MM-dd HH:mm:ss")
        {
            return dateTime.ToString(format);
        }

        /// <summary>
        /// 获取时间键（用于事件去重）
        /// </summary>
        public static string GetTimeKey(DateTime dateTime)
        {
            return dateTime.ToString("yyyy-MM-dd HH:mm");
        }
    }
}
