using DreamUnrealManager.Models;
using DreamUnrealManager.Services;

namespace DreamUnrealManager.Tests;

/// <summary>
/// 清理功能的安全边界测试。
///
/// 这是个递归删除功能，边界判断错了就是删用户的源码。所以这里重点不是
/// "能不能删对"，而是"该拒绝的有没有全部拒绝"。
/// </summary>
public class ProjectCleanupServiceTests : IDisposable
{
    private readonly string _root;

    public ProjectCleanupServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dum-cleanup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // 测试清理失败不影响结论。
        }
    }

    private string MakeProject(string name = "MyProject")
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string MakeDir(string parent, params string[] segments)
    {
        var path = Path.Combine(new[] { parent }.Concat(segments).ToArray());
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteFile(string dir, string name, int bytes)
    {
        File.WriteAllBytes(Path.Combine(dir, name), new byte[bytes]);
    }

    // ---------- 安全边界 ----------

    [Fact]
    public void 拒绝删除项目根目录本身()
    {
        var project = MakeProject();
        Assert.False(ProjectCleanupService.IsSafeToDelete(project, project, out var reason));
        Assert.Contains("项目根目录", reason);
    }

    [Fact]
    public void 拒绝删除项目目录之外的路径()
    {
        var project = MakeProject();
        var outside = MakeDir(_root, "Outside", "Binaries");

        Assert.False(ProjectCleanupService.IsSafeToDelete(project, outside, out var reason));
        Assert.Contains("不在项目目录内", reason);
    }

    [Fact]
    public void 拒绝前缀相同但不是子目录的路径()
    {
        // C:\MyProject 与 C:\MyProject2 —— 少写一个分隔符就会误判为子目录
        var project = MakeProject("MyProject");
        var sibling = MakeDir(_root, "MyProject2", "Binaries");

        Assert.False(ProjectCleanupService.IsSafeToDelete(project, sibling, out var reason));
        Assert.Contains("不在项目目录内", reason);
    }

    [Theory]
    [InlineData("Source")]
    [InlineData("Content")]
    [InlineData("Config")]
    [InlineData("Plugins")]
    [InlineData("MyStuff")]
    public void 拒绝删除白名单以外的目录名(string dirName)
    {
        var project = MakeProject();
        var target = MakeDir(project, dirName);

        Assert.False(ProjectCleanupService.IsSafeToDelete(project, target, out var reason));
        Assert.Contains("白名单", reason);
    }

    [Fact]
    public void 拒绝删除不存在的目录()
    {
        var project = MakeProject();
        var missing = Path.Combine(project, "Binaries");

        Assert.False(ProjectCleanupService.IsSafeToDelete(project, missing, out var reason));
        Assert.Contains("不存在", reason);
    }

    [Fact]
    public void 拒绝删除磁盘根目录()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        Assert.False(ProjectCleanupService.IsSafeToDelete(root, root, out _));
    }

    [Theory]
    [InlineData("Binaries")]
    [InlineData("Intermediate")]
    [InlineData("DerivedDataCache")]
    [InlineData("Saved")]
    [InlineData(".vs")]
    public void 允许删除白名单内的生成目录(string dirName)
    {
        var project = MakeProject();
        var target = MakeDir(project, dirName);

        Assert.True(ProjectCleanupService.IsSafeToDelete(project, target, out var reason), reason);
    }

    [Fact]
    public void 允许删除插件下的生成目录()
    {
        var project = MakeProject();
        var target = MakeDir(project, "Plugins", "MyPlugin", "Binaries");

        Assert.True(ProjectCleanupService.IsSafeToDelete(project, target, out var reason), reason);
    }

    // ---------- 扫描 ----------

    [Fact]
    public async Task 扫描统计各类别体积()
    {
        var project = MakeProject();
        WriteFile(MakeDir(project, "Binaries"), "a.dll", 1000);
        WriteFile(MakeDir(project, "Intermediate"), "b.obj", 2000);
        WriteFile(MakeDir(project, "Source"), "c.cpp", 500);   // 不该被统计

        var scan = await new ProjectCleanupService().ScanAsync(project);

        Assert.Equal(3000, scan.TotalBytes);
        Assert.Equal(2, scan.Categories.Count);
        Assert.DoesNotContain(scan.Categories, c => c.DisplayName.Contains("Source"));
    }

    [Fact]
    public async Task 扫描覆盖嵌套布局的插件目录()
    {
        var project = MakeProject();
        var pluginDir = MakeDir(project, "Plugins", "Group", "MyPlugin");
        File.WriteAllText(Path.Combine(pluginDir, "MyPlugin.uplugin"), "{}");
        WriteFile(MakeDir(pluginDir, "Binaries"), "p.dll", 4096);

        var scan = await new ProjectCleanupService().ScanAsync(project);

        var binaries = Assert.Single(scan.Categories, c => c.Category == CleanupCategory.Binaries);
        Assert.Equal(4096, binaries.TotalBytes);
        Assert.Contains(binaries.Entries, e => e.RelativePath.Contains("MyPlugin"));
    }

    [Fact]
    public async Task 扫描盘根目录直接拒绝()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;

        var scan = await new ProjectCleanupService().ScanAsync(root);

        Assert.Empty(scan.Categories);
        Assert.Contains(scan.Warnings, w => w.Contains("根目录"));
    }

    // ---------- 清理 ----------

    [Fact]
    public async Task 清理只删除被选中的类别()
    {
        var project = MakeProject();
        var binaries = MakeDir(project, "Binaries");
        var intermediate = MakeDir(project, "Intermediate");
        var source = MakeDir(project, "Source");
        WriteFile(binaries, "a.dll", 1000);
        WriteFile(intermediate, "b.obj", 2000);
        WriteFile(source, "c.cpp", 500);

        var service = new ProjectCleanupService();
        var scan = await service.ScanAsync(project);
        var result = await service.CleanAsync(scan, new[] { CleanupCategory.Binaries });

        Assert.True(result.Success, string.Join("; ", result.Failures));
        Assert.False(Directory.Exists(binaries));
        Assert.True(Directory.Exists(intermediate));   // 未选中，保留
        Assert.True(Directory.Exists(source));         // 永远不动
        Assert.Equal(1000, result.FreedBytes);
    }

    [Fact]
    public async Task 清理_Saved_时保留_Config()
    {
        var project = MakeProject();
        var saved = MakeDir(project, "Saved");
        var logs = MakeDir(saved, "Logs");
        var config = MakeDir(saved, "Config");
        WriteFile(logs, "run.log", 100);
        WriteFile(config, "EditorPerProjectUserSettings.ini", 50);

        var service = new ProjectCleanupService();
        var scan = await service.ScanAsync(project);
        var result = await service.CleanAsync(scan, new[] { CleanupCategory.Saved });

        Assert.True(result.Success, string.Join("; ", result.Failures));
        Assert.False(Directory.Exists(logs));
        Assert.True(Directory.Exists(config));
        Assert.True(File.Exists(Path.Combine(config, "EditorPerProjectUserSettings.ini")));
    }

    [Fact]
    public async Task 未选中任何类别时不做任何删除()
    {
        var project = MakeProject();
        var binaries = MakeDir(project, "Binaries");
        WriteFile(binaries, "a.dll", 1000);

        var service = new ProjectCleanupService();
        var scan = await service.ScanAsync(project);
        var result = await service.CleanAsync(scan, Array.Empty<CleanupCategory>());

        Assert.Equal(0, result.DeletedDirectories);
        Assert.True(Directory.Exists(binaries));
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1 MB")]
    [InlineData(1073741824, "1 GB")]
    public void 体积格式化(long bytes, string expected)
    {
        Assert.Equal(expected, ByteSizeFormatter.Format(bytes));
    }
}
