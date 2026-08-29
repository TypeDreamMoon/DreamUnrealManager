using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

namespace DreamUnrealManager.Services
{
    public sealed class ConfigBackupResult
    {
        public bool Success { get; init; }
        public string? Error { get; init; }
        public List<string> Files { get; } = new();
    }

    /// <summary>
    /// 配置的导出与导入。
    ///
    /// 补的缺口：配置散落在 %LocalAppData%/DreamUnrealManager 下的多个文件里
    /// （projects.json / engines.json / settings.json / LocalSettings.json…），
    /// 换机器就得把项目和引擎全部重新配一遍，也没有任何备份手段。
    /// 打成一个 zip，导入前自动备份现有配置，避免一次误操作把两边都弄丢。
    /// </summary>
    public static class ConfigBackupService
    {
        /// <summary>纳入备份的配置文件名。日志、缓存这些不进去。</summary>
        private static readonly string[] BackupFileNames =
        {
            "projects.json",
            "engines.json",
            "settings.json",
            "LocalSettings.json",
            "default.txt"
        };

        public static string ConfigDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DreamUnrealManager");

        public static string SuggestFileName() =>
            $"DreamUnrealManager-config-{DateTime.Now:yyyyMMdd-HHmmss}.zip";

        public static async Task<ConfigBackupResult> ExportAsync(string targetZipPath)
        {
            var result = new ConfigBackupResult { Success = true };

            try
            {
                var dir = ConfigDirectory;
                if (!Directory.Exists(dir))
                {
                    return new ConfigBackupResult { Success = false, Error = "配置目录不存在，没有可导出的内容。" };
                }

                var present = BackupFileNames
                    .Select(name => Path.Combine(dir, name))
                    .Where(File.Exists)
                    .ToList();

                if (present.Count == 0)
                {
                    return new ConfigBackupResult { Success = false, Error = "没有找到任何配置文件。" };
                }

                await Task.Run(() =>
                {
                    // 覆盖前先删：ZipFile 以 Create 模式打开已存在的文件会抛。
                    if (File.Exists(targetZipPath))
                    {
                        File.Delete(targetZipPath);
                    }

                    using var archive = ZipFile.Open(targetZipPath, ZipArchiveMode.Create);
                    foreach (var file in present)
                    {
                        archive.CreateEntryFromFile(file, Path.GetFileName(file), CompressionLevel.Optimal);
                    }
                }).ConfigureAwait(false);

                result.Files.AddRange(present.Select(Path.GetFileName)!);
                AppLogService.Info($"配置已导出到 {targetZipPath}（{result.Files.Count} 个文件）", "ConfigBackup");
                return result;
            }
            catch (Exception ex)
            {
                AppLogService.Error("导出配置失败", ex, "ConfigBackup");
                return new ConfigBackupResult { Success = false, Error = ex.Message };
            }
        }

        public static async Task<ConfigBackupResult> ImportAsync(string sourceZipPath)
        {
            var result = new ConfigBackupResult { Success = true };

            try
            {
                if (!File.Exists(sourceZipPath))
                {
                    return new ConfigBackupResult { Success = false, Error = "备份文件不存在。" };
                }

                var dir = ConfigDirectory;
                Directory.CreateDirectory(dir);

                await Task.Run(() =>
                {
                    // 导入前先把现有配置备份一份：导入是覆盖操作，
                    // 万一 zip 里是别的机器的旧配置，用户还能回退。
                    var rollbackDir = Path.Combine(dir, "Backup",
                        $"before-import-{DateTime.Now:yyyyMMdd-HHmmss}");
                    Directory.CreateDirectory(rollbackDir);

                    foreach (var name in BackupFileNames)
                    {
                        var existing = Path.Combine(dir, name);
                        if (File.Exists(existing))
                        {
                            File.Copy(existing, Path.Combine(rollbackDir, name), overwrite: true);
                        }
                    }

                    using var archive = ZipFile.OpenRead(sourceZipPath);
                    foreach (var entry in archive.Entries)
                    {
                        // 只认白名单里的文件名，且丢弃任何路径部分——
                        // 防止 zip 里带 ../ 路径把文件写到配置目录之外（zip slip）。
                        var name = Path.GetFileName(entry.FullName);
                        if (string.IsNullOrEmpty(name) ||
                            !BackupFileNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        entry.ExtractToFile(Path.Combine(dir, name), overwrite: true);
                        result.Files.Add(name);
                    }

                    AppLogService.Info($"导入前配置已备份到 {rollbackDir}", "ConfigBackup");
                }).ConfigureAwait(false);

                if (result.Files.Count == 0)
                {
                    return new ConfigBackupResult
                    {
                        Success = false,
                        Error = "备份文件中没有可识别的配置项。"
                    };
                }

                AppLogService.Info($"已从 {sourceZipPath} 导入 {result.Files.Count} 个配置文件", "ConfigBackup");
                return result;
            }
            catch (Exception ex)
            {
                AppLogService.Error("导入配置失败", ex, "ConfigBackup");
                return new ConfigBackupResult { Success = false, Error = ex.Message };
            }
        }
    }
}
