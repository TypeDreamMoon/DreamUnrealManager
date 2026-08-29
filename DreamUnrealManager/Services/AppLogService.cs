using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace DreamUnrealManager.Services
{
    public enum LogLevel
    {
        Debug,
        Info,
        Warn,
        Error
    }

    /// <summary>
    /// 应用统一日志：按天滚动写入 %LocalAppData%/DreamUnrealManager/Logs/app-yyyyMMdd.log。
    /// 之前全项目只有零散的 Debug.WriteLine，Release 下等于没有输出，用户报障时拿不到任何现场信息。
    /// 约定：日志自身永远不抛异常，写失败就放弃这一条，绝不能因为记日志把主流程带崩。
    /// </summary>
    public static class AppLogService
    {
        private const int RetainDays = 7;

        private static readonly string LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DreamUnrealManager",
            "Logs");

        private static readonly object Gate = new();
        private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

        private static DateTime _currentDay = DateTime.MinValue;
        private static string? _currentFile;

        public static string Directory => LogDirectory;

        /// <summary>当天日志文件的完整路径；目录尚未创建时返回预期路径，不做副作用。</summary>
        public static string CurrentLogFile => Path.Combine(
            LogDirectory,
            $"app-{DateTime.Now:yyyyMMdd}.log");

        public static void Debug(string message, string? category = null)
            => Write(LogLevel.Debug, category, message, null);

        public static void Info(string message, string? category = null)
            => Write(LogLevel.Info, category, message, null);

        public static void Warn(string message, string? category = null)
            => Write(LogLevel.Warn, category, message, null);

        public static void Error(string message, Exception? exception = null, string? category = null)
            => Write(LogLevel.Error, category, message, exception);

        private static void Write(LogLevel level, string? category, string message, Exception? exception)
        {
            try
            {
                var now = DateTime.Now;
                var builder = new StringBuilder();
                builder.Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
                builder.Append(" [").Append(level.ToString().ToUpperInvariant()).Append(']');
                if (!string.IsNullOrWhiteSpace(category))
                {
                    builder.Append(" [").Append(category).Append(']');
                }

                builder.Append(' ').Append(message);

                if (exception != null)
                {
                    builder.AppendLine();
                    builder.Append(exception);
                }

                builder.AppendLine();

                lock (Gate)
                {
                    var file = EnsureFileForDay(now);
                    File.AppendAllText(file, builder.ToString(), Utf8NoBom);
                }
            }
            catch
            {
                // 记日志失败不允许影响调用方。
            }
        }

        /// <summary>切到当天文件；跨天时顺手清理过期日志，避免无限增长。</summary>
        private static string EnsureFileForDay(DateTime now)
        {
            var day = now.Date;
            if (_currentFile != null && _currentDay == day)
            {
                return _currentFile;
            }

            System.IO.Directory.CreateDirectory(LogDirectory);
            _currentDay = day;
            _currentFile = Path.Combine(LogDirectory, $"app-{day:yyyyMMdd}.log");
            PruneOldLogs(day);
            return _currentFile;
        }

        private static void PruneOldLogs(DateTime today)
        {
            try
            {
                var cutoff = today.AddDays(-RetainDays);
                foreach (var path in System.IO.Directory.EnumerateFiles(LogDirectory, "app-*.log"))
                {
                    var stamp = Path.GetFileNameWithoutExtension(path);
                    // 文件名固定为 app-yyyyMMdd，解析失败的一律不动，避免误删用户放进来的东西。
                    if (stamp.Length != 12 ||
                        !DateTime.TryParseExact(stamp[4..], "yyyyMMdd", CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out var fileDay))
                    {
                        continue;
                    }

                    if (fileDay < cutoff)
                    {
                        File.Delete(path);
                    }
                }
            }
            catch
            {
                // 清理失败不影响写日志。
            }
        }

        /// <summary>读取最近若干行，供“关于/诊断”界面直接展示。</summary>
        public static IReadOnlyList<string> ReadRecentLines(int maxLines = 500)
        {
            try
            {
                var file = CurrentLogFile;
                if (!File.Exists(file))
                {
                    return Array.Empty<string>();
                }

                // 日志可能正被本进程持有，必须以共享读写方式打开。
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Utf8NoBom);
                var lines = new Queue<string>(maxLines);
                while (reader.ReadLine() is { } line)
                {
                    if (lines.Count == maxLines)
                    {
                        lines.Dequeue();
                    }

                    lines.Enqueue(line);
                }

                return lines.ToList();
            }
            catch (Exception ex)
            {
                return new[] { $"(读取日志失败: {ex.Message})" };
            }
        }
    }
}
