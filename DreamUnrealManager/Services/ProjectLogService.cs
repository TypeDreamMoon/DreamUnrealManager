using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DreamUnrealManager.Services
{
    public enum ProjectLogKind
    {
        Log,
        Crash
    }

    public sealed class ProjectLogEntry
    {
        public required ProjectLogKind Kind { get; init; }
        public required string DisplayName { get; init; }
        public required string FullPath { get; init; }
        public required DateTime LastWriteTime { get; init; }
        public required long SizeBytes { get; init; }

        /// <summary>崩溃条目指向目录而不是单个文件。</summary>
        public bool IsDirectory { get; init; }
    }

    /// <summary>
    /// 读取项目的 Saved/Logs 与 Saved/Crashes。
    ///
    /// 补的是一个明显缺口：程序能读项目的一切元数据，却打不开它的日志和崩溃转储，
    /// 而这恰恰是 UE 开发者一天要看几十次的东西。
    /// 服务只读，不做任何删除——清理交给 ProjectCleanupService 的 Saved 类别。
    /// </summary>
    public static class ProjectLogService
    {
        /// <summary>UE 日志可能有几百 MB，全读进内存会直接卡死 UI，只取尾部。</summary>
        public const int DefaultTailLines = 2000;

        public static IReadOnlyList<ProjectLogEntry> Enumerate(string projectDirectory)
        {
            var entries = new List<ProjectLogEntry>();

            if (string.IsNullOrWhiteSpace(projectDirectory) || !Directory.Exists(projectDirectory))
            {
                return entries;
            }

            CollectLogs(Path.Combine(projectDirectory, "Saved", "Logs"), entries);
            CollectCrashes(Path.Combine(projectDirectory, "Saved", "Crashes"), entries);

            // 最新的排最前——找问题永远是从最近一次开始看。
            return entries.OrderByDescending(e => e.LastWriteTime).ToList();
        }

        private static void CollectLogs(string logsDirectory, List<ProjectLogEntry> entries)
        {
            if (!Directory.Exists(logsDirectory))
            {
                return;
            }

            try
            {
                foreach (var file in Directory.EnumerateFiles(logsDirectory, "*.log"))
                {
                    var info = new FileInfo(file);
                    entries.Add(new ProjectLogEntry
                    {
                        Kind = ProjectLogKind.Log,
                        DisplayName = info.Name,
                        FullPath = info.FullName,
                        LastWriteTime = info.LastWriteTime,
                        SizeBytes = info.Length
                    });
                }
            }
            catch (Exception ex)
            {
                AppLogService.Warn($"枚举日志目录失败 ({logsDirectory}): {ex.Message}", "ProjectLog");
            }
        }

        private static void CollectCrashes(string crashesDirectory, List<ProjectLogEntry> entries)
        {
            if (!Directory.Exists(crashesDirectory))
            {
                return;
            }

            try
            {
                // 每次崩溃是一个目录（含 .log / .dmp / CrashContext.runtime-xml）。
                foreach (var dir in Directory.EnumerateDirectories(crashesDirectory))
                {
                    var info = new DirectoryInfo(dir);
                    long size = 0;
                    try
                    {
                        size = info.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
                    }
                    catch
                    {
                        // 体积算不出来不影响列出这条崩溃记录。
                    }

                    entries.Add(new ProjectLogEntry
                    {
                        Kind = ProjectLogKind.Crash,
                        DisplayName = info.Name,
                        FullPath = info.FullName,
                        LastWriteTime = info.LastWriteTime,
                        SizeBytes = size,
                        IsDirectory = true
                    });
                }
            }
            catch (Exception ex)
            {
                AppLogService.Warn($"枚举崩溃目录失败 ({crashesDirectory}): {ex.Message}", "ProjectLog");
            }
        }

        /// <summary>
        /// 读取日志尾部若干行。崩溃条目会自动挑目录里最大的那个 .log。
        /// </summary>
        public static async Task<string> ReadTailAsync(
            ProjectLogEntry entry,
            int maxLines = DefaultTailLines,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var path = entry.IsDirectory ? FindPrimaryLogInCrash(entry.FullPath) : entry.FullPath;

                if (path == null || !File.Exists(path))
                {
                    return "(该记录中没有可读的 .log 文件，可用「打开所在目录」查看 .dmp 等文件)";
                }

                return await Task.Run(() => ReadTail(path, maxLines, cancellationToken), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLogService.Error($"读取日志失败 ({entry.FullPath})", ex, "ProjectLog");
                return $"(读取失败: {ex.Message})";
            }
        }

        private static string? FindPrimaryLogInCrash(string crashDirectory)
        {
            try
            {
                return Directory
                    .EnumerateFiles(crashDirectory, "*.log", SearchOption.AllDirectories)
                    .OrderByDescending(f => new FileInfo(f).Length)
                    .FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private static string ReadTail(string path, int maxLines, CancellationToken cancellationToken)
        {
            // 必须允许共享读写：编辑器往往正开着并持续往这个文件里写。
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            // UE 日志是 UTF-8；用宽松解码避免个别坏字节中断整份读取。
            using var reader = new StreamReader(stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false),
                detectEncodingFromByteOrderMarks: true);

            var buffer = new Queue<string>(maxLines);
            var total = 0;

            while (reader.ReadLine() is { } line)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (buffer.Count == maxLines)
                {
                    buffer.Dequeue();
                }

                buffer.Enqueue(line);
                total++;
            }

            var builder = new StringBuilder();
            if (total > maxLines)
            {
                builder.AppendLine($"(共 {total} 行，以下为最后 {maxLines} 行)");
                builder.AppendLine();
            }

            foreach (var line in buffer)
            {
                builder.AppendLine(line);
            }

            return builder.ToString();
        }
    }
}
