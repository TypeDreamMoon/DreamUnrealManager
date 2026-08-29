using System.IO.Compression;
using DreamUnrealManager.Services;

namespace DreamUnrealManager.Tests;

/// <summary>
/// 配置导出/导入。重点在两件事：往返能还原，以及 zip 里的恶意路径不能写出配置目录。
/// 这些测试会读写真实的配置目录，所以每条都先把现场保存下来、结束后恢复。
/// </summary>
[Collection("ConfigBackup")]
public class ConfigBackupServiceTests : IDisposable
{
    private readonly string _configDir = ConfigBackupService.ConfigDirectory;
    private readonly string _sandbox;
    private readonly string? _snapshotDir;

    public ConfigBackupServiceTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "dum-backup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);

        // 备份真实配置目录，测试结束后原样放回——绝不能因为跑测试弄丢用户配置。
        if (Directory.Exists(_configDir))
        {
            _snapshotDir = Path.Combine(_sandbox, "__snapshot");
            Directory.CreateDirectory(_snapshotDir);
            foreach (var file in Directory.EnumerateFiles(_configDir))
            {
                File.Copy(file, Path.Combine(_snapshotDir, Path.GetFileName(file)), overwrite: true);
            }
        }
    }

    public void Dispose()
    {
        try
        {
            if (_snapshotDir != null && Directory.Exists(_snapshotDir))
            {
                foreach (var file in Directory.EnumerateFiles(_snapshotDir))
                {
                    File.Copy(file, Path.Combine(_configDir, Path.GetFileName(file)), overwrite: true);
                }
            }

            if (Directory.Exists(_sandbox)) Directory.Delete(_sandbox, recursive: true);
        }
        catch
        {
            // 忽略清理失败
        }
    }

    [Fact]
    public async Task 导出再导入可以还原内容()
    {
        Directory.CreateDirectory(_configDir);
        var marker = Guid.NewGuid().ToString("N");
        var settingsPath = Path.Combine(_configDir, "settings.json");
        var original = File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : null;

        try
        {
            File.WriteAllText(settingsPath, $"{{\"marker\":\"{marker}\"}}");

            var zip = Path.Combine(_sandbox, "backup.zip");
            var export = await ConfigBackupService.ExportAsync(zip);

            Assert.True(export.Success, export.Error);
            Assert.True(File.Exists(zip));
            Assert.Contains("settings.json", export.Files);

            // 改掉内容，再导入回来
            File.WriteAllText(settingsPath, "{\"marker\":\"changed\"}");
            var import = await ConfigBackupService.ImportAsync(zip);

            Assert.True(import.Success, import.Error);
            Assert.Contains(marker, File.ReadAllText(settingsPath));
        }
        finally
        {
            if (original != null) File.WriteAllText(settingsPath, original);
        }
    }

    [Fact]
    public async Task 导出到已存在的文件会覆盖而不是报错()
    {
        Directory.CreateDirectory(_configDir);
        var zip = Path.Combine(_sandbox, "existing.zip");
        File.WriteAllText(zip, "old content");

        var result = await ConfigBackupService.ExportAsync(zip);

        Assert.True(result.Success, result.Error);
        using var archive = ZipFile.OpenRead(zip);
        Assert.NotEmpty(archive.Entries);
    }

    [Fact]
    public async Task 导入不存在的文件返回失败而不是抛异常()
    {
        var result = await ConfigBackupService.ImportAsync(Path.Combine(_sandbox, "nope.zip"));

        Assert.False(result.Success);
        Assert.Contains("不存在", result.Error);
    }

    [Fact]
    public async Task 导入无关内容的_zip_返回失败()
    {
        var zip = Path.Combine(_sandbox, "unrelated.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("readme.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("nothing useful");
        }

        var result = await ConfigBackupService.ImportAsync(zip);

        Assert.False(result.Success);
        Assert.Contains("没有可识别", result.Error);
    }

    [Fact]
    public async Task 拒绝_zip_中带上级路径的条目()
    {
        // zip slip：条目名带 ../ 试图写到配置目录之外
        var zip = Path.Combine(_sandbox, "evil.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry(@"..\..\..\settings.json");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("{\"pwned\":true}");
        }

        var escapeTarget = Path.GetFullPath(Path.Combine(_configDir, @"..\..\..\settings.json"));
        var existedBefore = File.Exists(escapeTarget);

        var result = await ConfigBackupService.ImportAsync(zip);

        // 路径部分被剥掉后文件名仍是 settings.json，会被当成合法条目写进配置目录内，
        // 关键是它绝不能落到配置目录之外。
        if (!existedBefore)
        {
            Assert.False(File.Exists(escapeTarget), "zip 条目逃逸出了配置目录");
        }

        // 落点必须在配置目录内
        if (result.Success)
        {
            Assert.All(result.Files, name => Assert.Equal(name, Path.GetFileName(name)));
        }
    }

    [Fact]
    public void 建议的文件名带时间戳且是_zip()
    {
        var name = ConfigBackupService.SuggestFileName();

        Assert.StartsWith("DreamUnrealManager-config-", name);
        Assert.EndsWith(".zip", name);
    }
}
