using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DreamUnrealManager.Contracts.Services;
using DreamUnrealManager.Models;

namespace DreamUnrealManager.Services
{
    /// <summary>
    /// 清理 UE 项目的生成目录（Binaries / Intermediate / DerivedDataCache / Saved / IDE 缓存）。
    ///
    /// 这是个删除功能，所有安全边界都写死在 IsSafeToDelete 里，不接受调用方传任意路径：
    /// 目标必须是项目目录下的真实子目录、必须是白名单里的目录名、不能是重解析点。
    /// </summary>
    public sealed class ProjectCleanupService : IProjectCleanupService
    {
        /// <summary>白名单：只有这些目录名会被删除，其余一律不碰。</summary>
        private static readonly Dictionary<CleanupCategory, string[]> CategoryDirectoryNames = new()
        {
            [CleanupCategory.Binaries] = new[] { "Binaries" },
            [CleanupCategory.Intermediate] = new[] { "Intermediate" },
            [CleanupCategory.DerivedDataCache] = new[] { "DerivedDataCache" },
            [CleanupCategory.Saved] = new[] { "Saved" },
            [CleanupCategory.IdeCache] = new[] { ".vs", ".idea" }
        };

        private static readonly Dictionary<CleanupCategory, (string Name, string Description)> CategoryInfo = new()
        {
            [CleanupCategory.Binaries] =
                ("Binaries", "编译产物（含各插件）。删除后需要重新编译 C++ 才能打开项目。"),
            [CleanupCategory.Intermediate] =
                ("Intermediate", "中间产物（含各插件）。可安全删除，下次编译会重新生成。"),
            [CleanupCategory.DerivedDataCache] =
                ("DerivedDataCache", "本地派生数据缓存。可安全删除，代价是下次打开项目要重新烘焙着色器。"),
            [CleanupCategory.Saved] =
                ("Saved", "日志、崩溃转储、自动保存、烘焙输出等。注意：会一并清掉自动保存的关卡备份。"),
            [CleanupCategory.IdeCache] =
                (".vs / .idea", "IDE 索引缓存。可安全删除，代价是 IDE 下次打开要重建索引。")
        };

        /// <summary>Saved 下这些子目录不删：Config 里是用户的编辑器偏好，删了要重新配。</summary>
        private static readonly string[] SavedKeepSubDirectories = { "Config" };

        public async Task<CleanupScanResult> ScanAsync(
            string projectDirectory,
            CancellationToken cancellationToken = default)
        {
            var result = new CleanupScanResult { ProjectDirectory = projectDirectory };

            if (string.IsNullOrWhiteSpace(projectDirectory) || !Directory.Exists(projectDirectory))
            {
                result.Warnings.Add($"项目目录不存在: {projectDirectory}");
                return result;
            }

            var root = Path.GetFullPath(projectDirectory);
            if (IsDriveRoot(root))
            {
                // 防呆：项目目录若被误设成盘根，扫描会遍历整个磁盘，清理更是灾难性的。
                result.Warnings.Add("项目目录指向磁盘根目录，已拒绝扫描。");
                AppLogService.Warn($"拒绝扫描盘根目录: {root}", "Cleanup");
                return result;
            }

            await Task.Run(() =>
            {
                foreach (var (category, (name, description)) in CategoryInfo)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var summary = new CleanupCategorySummary
                    {
                        Category = category,
                        DisplayName = name,
                        Description = description
                    };

                    foreach (var path in EnumerateCandidates(root, category, result.Warnings))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var (size, files) = MeasureDirectory(path, result.Warnings, cancellationToken);
                        if (files == 0 && size == 0)
                        {
                            continue;
                        }

                        summary.Entries.Add(new CleanupEntry
                        {
                            Category = category,
                            FullPath = path,
                            RelativePath = Path.GetRelativePath(root, path),
                            SizeBytes = size,
                            FileCount = files
                        });
                    }

                    if (summary.HasContent)
                    {
                        result.Categories.Add(summary);
                    }
                }
            }, cancellationToken).ConfigureAwait(false);

            AppLogService.Info(
                $"清理扫描完成: {root}，可释放 {ByteSizeFormatter.Format(result.TotalBytes)}",
                "Cleanup");

            return result;
        }

        /// <summary>
        /// 收集候选目录：项目根下一层，外加 Plugins/*/ 下一层。
        /// 插件的 Binaries/Intermediate 往往比项目本身还大，只扫根目录会漏掉大头。
        /// </summary>
        private static IEnumerable<string> EnumerateCandidates(
            string root,
            CleanupCategory category,
            List<string> warnings)
        {
            var names = CategoryDirectoryNames[category];

            foreach (var name in names)
            {
                var path = Path.Combine(root, name);
                if (Directory.Exists(path))
                {
                    yield return path;
                }
            }

            // 插件目录只对编译类产物有意义，DDC/Saved/IDE 缓存不会出现在插件下。
            if (category != CleanupCategory.Binaries && category != CleanupCategory.Intermediate)
            {
                yield break;
            }

            var pluginsRoot = Path.Combine(root, "Plugins");
            if (!Directory.Exists(pluginsRoot))
            {
                yield break;
            }

            List<string> pluginDirs;
            try
            {
                // 插件可能有嵌套布局（Plugins/GroupName/PluginName），所以按 .uplugin 定位而不是只看一层。
                pluginDirs = Directory
                    .EnumerateFiles(pluginsRoot, "*.uplugin", SearchOption.AllDirectories)
                    .Select(Path.GetDirectoryName)
                    .Where(d => !string.IsNullOrEmpty(d))
                    .Select(d => d!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                warnings.Add($"枚举插件目录失败: {ex.Message}");
                yield break;
            }

            foreach (var pluginDir in pluginDirs)
            {
                foreach (var name in names)
                {
                    var path = Path.Combine(pluginDir, name);
                    if (Directory.Exists(path))
                    {
                        yield return path;
                    }
                }
            }
        }

        private static (long Size, int Files) MeasureDirectory(
            string directory,
            List<string> warnings,
            CancellationToken cancellationToken)
        {
            long size = 0;
            var files = 0;

            var stack = new Stack<string>();
            stack.Push(directory);

            while (stack.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var current = stack.Pop();

                try
                {
                    var info = new DirectoryInfo(current);

                    // 不跟随符号链接/联接点：否则可能统计到（进而删除到）项目之外的内容。
                    if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        continue;
                    }

                    foreach (var file in info.EnumerateFiles())
                    {
                        size += file.Length;
                        files++;
                    }

                    foreach (var sub in info.EnumerateDirectories())
                    {
                        stack.Push(sub.FullName);
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    warnings.Add($"无权限读取: {current}");
                }
                catch (DirectoryNotFoundException)
                {
                    // 扫描期间目录被别的进程删掉，忽略即可。
                }
                catch (Exception ex)
                {
                    warnings.Add($"读取失败 {current}: {ex.Message}");
                }
            }

            return (size, files);
        }

        public async Task<CleanupResult> CleanAsync(
            CleanupScanResult scan,
            IReadOnlyCollection<CleanupCategory> categories,
            IProgress<string>? log = null,
            CancellationToken cancellationToken = default)
        {
            var result = new CleanupResult();

            if (scan == null || categories == null || categories.Count == 0)
            {
                return result;
            }

            var root = Path.GetFullPath(scan.ProjectDirectory);

            var targets = scan.Categories
                .Where(c => categories.Contains(c.Category))
                .SelectMany(c => c.Entries)
                .ToList();

            await Task.Run(() =>
            {
                foreach (var entry in targets)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!IsSafeToDelete(root, entry.FullPath, out var reason))
                    {
                        var message = $"已跳过 {entry.RelativePath}：{reason}";
                        log?.Report(message);
                        result.Failures.Add(message);
                        AppLogService.Warn($"拒绝删除 {entry.FullPath}: {reason}", "Cleanup");
                        continue;
                    }

                    try
                    {
                        if (entry.Category == CleanupCategory.Saved)
                        {
                            DeleteSavedPreservingConfig(entry.FullPath, log);
                        }
                        else
                        {
                            Directory.Delete(entry.FullPath, recursive: true);
                            log?.Report($"已删除 {entry.RelativePath} ({ByteSizeFormatter.Format(entry.SizeBytes)})");
                        }

                        result.FreedBytes += entry.SizeBytes;
                        result.DeletedDirectories++;
                    }
                    catch (Exception ex)
                    {
                        // 最常见的是引擎/IDE 正开着，dll 被占用。
                        var message = $"删除 {entry.RelativePath} 失败: {ex.Message}";
                        log?.Report(message);
                        result.Failures.Add(message);
                        AppLogService.Error($"删除失败 {entry.FullPath}", ex, "Cleanup");
                    }
                }
            }, cancellationToken).ConfigureAwait(false);

            AppLogService.Info(
                $"清理完成: 释放 {ByteSizeFormatter.Format(result.FreedBytes)}，" +
                $"删除 {result.DeletedDirectories} 个目录，失败 {result.Failures.Count} 项",
                "Cleanup");

            return result;
        }

        /// <summary>Saved 整体删除，但保留 Saved/Config（里面是用户的编辑器偏好设置）。</summary>
        private static void DeleteSavedPreservingConfig(string savedPath, IProgress<string>? log)
        {
            foreach (var sub in Directory.EnumerateDirectories(savedPath))
            {
                var name = Path.GetFileName(sub);
                if (SavedKeepSubDirectories.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    log?.Report($"保留 Saved/{name}（用户配置）");
                    continue;
                }

                Directory.Delete(sub, recursive: true);
            }

            foreach (var file in Directory.EnumerateFiles(savedPath))
            {
                File.Delete(file);
            }

            log?.Report("已清理 Saved（保留 Config）");
        }

        /// <summary>
        /// 删除前的最后一道闸。任何一条不满足就拒绝：
        /// 必须存在、不能是盘根、必须真的在项目目录之内、目录名必须在白名单里、不能是重解析点。
        /// </summary>
        internal static bool IsSafeToDelete(string projectRoot, string target, out string reason)
        {
            reason = string.Empty;

            if (string.IsNullOrWhiteSpace(target) || !Directory.Exists(target))
            {
                reason = "目录不存在";
                return false;
            }

            string fullTarget, fullRoot;
            try
            {
                fullTarget = Path.GetFullPath(target);
                fullRoot = Path.GetFullPath(projectRoot);
            }
            catch (Exception ex)
            {
                reason = $"路径无法解析: {ex.Message}";
                return false;
            }

            if (IsDriveRoot(fullTarget))
            {
                reason = "目标是磁盘根目录";
                return false;
            }

            if (string.Equals(fullTarget, fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                reason = "目标就是项目根目录本身";
                return false;
            }

            // 必须严格位于项目目录内。补上分隔符再比较，避免 C:\Proj 匹配到 C:\Proj2。
            var rootWithSeparator = fullRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullTarget.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                reason = "目标不在项目目录内";
                return false;
            }

            var name = Path.GetFileName(fullTarget.TrimEnd(Path.DirectorySeparatorChar));
            var allowed = CategoryDirectoryNames.Values.SelectMany(v => v);
            if (!allowed.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                reason = $"目录名 {name} 不在可清理白名单内";
                return false;
            }

            try
            {
                if (new DirectoryInfo(fullTarget).Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    // 符号链接/联接点递归删除会删到链接指向的真实位置，可能在项目之外。
                    reason = "目标是符号链接或联接点";
                    return false;
                }
            }
            catch (Exception ex)
            {
                reason = $"无法读取目录属性: {ex.Message}";
                return false;
            }

            return true;
        }

        private static bool IsDriveRoot(string path)
        {
            try
            {
                var full = Path.GetFullPath(path);
                var root = Path.GetPathRoot(full);
                return string.Equals(
                    full.TrimEnd(Path.DirectorySeparatorChar),
                    root?.TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                // 解析不出来时按“是根目录”处理，宁可拒绝也不误删。
                return true;
            }
        }
    }
}
