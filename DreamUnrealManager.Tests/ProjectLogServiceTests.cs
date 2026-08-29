using DreamUnrealManager.Services;

namespace DreamUnrealManager.Tests;

public class ProjectLogServiceTests : IDisposable
{
    private readonly string _root;

    public ProjectLogServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dum-log-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // 忽略清理失败
        }
    }

    private string LogsDir()
    {
        var dir = Path.Combine(_root, "Saved", "Logs");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private string CrashesDir()
    {
        var dir = Path.Combine(_root, "Saved", "Crashes");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void 项目目录不存在时返回空列表()
    {
        Assert.Empty(ProjectLogService.Enumerate(Path.Combine(_root, "NoSuchProject")));
    }

    [Fact]
    public void 没有_Saved_目录时返回空列表()
    {
        Assert.Empty(ProjectLogService.Enumerate(_root));
    }

    [Fact]
    public void 列出日志文件()
    {
        var logs = LogsDir();
        File.WriteAllText(Path.Combine(logs, "MyGame.log"), "hello");
        File.WriteAllText(Path.Combine(logs, "MyGame-backup.log"), "old");
        File.WriteAllText(Path.Combine(logs, "notalog.txt"), "ignored");

        var entries = ProjectLogService.Enumerate(_root);

        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Equal(ProjectLogKind.Log, e.Kind));
        Assert.DoesNotContain(entries, e => e.DisplayName.EndsWith(".txt"));
    }

    [Fact]
    public void 列出崩溃目录()
    {
        var crashDir = Path.Combine(CrashesDir(), "UECC-Windows-ABC123");
        Directory.CreateDirectory(crashDir);
        File.WriteAllText(Path.Combine(crashDir, "MyGame.log"), new string('x', 500));
        File.WriteAllText(Path.Combine(crashDir, "UEMinidump.dmp"), new string('y', 100));

        var entries = ProjectLogService.Enumerate(_root);

        var crash = Assert.Single(entries);
        Assert.Equal(ProjectLogKind.Crash, crash.Kind);
        Assert.True(crash.IsDirectory);
        Assert.Equal(600, crash.SizeBytes);   // 目录内所有文件之和
    }

    [Fact]
    public void 按时间倒序排列()
    {
        var logs = LogsDir();
        var older = Path.Combine(logs, "old.log");
        var newer = Path.Combine(logs, "new.log");
        File.WriteAllText(older, "a");
        File.WriteAllText(newer, "b");
        File.SetLastWriteTime(older, DateTime.Now.AddDays(-2));
        File.SetLastWriteTime(newer, DateTime.Now);

        var entries = ProjectLogService.Enumerate(_root);

        Assert.Equal("new.log", entries[0].DisplayName);
        Assert.Equal("old.log", entries[1].DisplayName);
    }

    [Fact]
    public async Task 读取短日志返回全部内容()
    {
        var logs = LogsDir();
        var path = Path.Combine(logs, "a.log");
        File.WriteAllLines(path, new[] { "line1", "line2", "line3" });

        var entry = ProjectLogService.Enumerate(_root).Single();
        var text = await ProjectLogService.ReadTailAsync(entry);

        Assert.Contains("line1", text);
        Assert.Contains("line3", text);
        Assert.DoesNotContain("以下为最后", text);   // 未截断时不加提示头
    }

    [Fact]
    public async Task 超长日志只取尾部并标注()
    {
        var logs = LogsDir();
        var path = Path.Combine(logs, "big.log");
        File.WriteAllLines(path, Enumerable.Range(1, 500).Select(i => $"line{i}"));

        var entry = ProjectLogService.Enumerate(_root).Single();
        var text = await ProjectLogService.ReadTailAsync(entry, maxLines: 10);

        Assert.Contains("共 500 行，以下为最后 10 行", text);
        Assert.Contains("line500", text);
        Assert.Contains("line491", text);
        Assert.DoesNotContain("line490", text);
    }

    [Fact]
    public async Task 文件被其他进程独占写入时仍可读取()
    {
        // 这是真实场景：查看日志时 UE 编辑器往往正开着并持续往这个文件里写。
        var logs = LogsDir();
        var path = Path.Combine(logs, "live.log");
        File.WriteAllLines(path, new[] { "existing" });

        using var writer = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);

        var entry = ProjectLogService.Enumerate(_root).Single();
        var text = await ProjectLogService.ReadTailAsync(entry);

        Assert.Contains("existing", text);
    }

    [Fact]
    public async Task 崩溃目录自动挑出最大的_log()
    {
        var crashDir = Path.Combine(CrashesDir(), "UECC-Windows-XYZ");
        Directory.CreateDirectory(crashDir);
        File.WriteAllText(Path.Combine(crashDir, "small.log"), "small");
        File.WriteAllLines(Path.Combine(crashDir, "main.log"),
            Enumerable.Range(1, 50).Select(i => $"crashline{i}"));

        var entry = ProjectLogService.Enumerate(_root).Single();
        var text = await ProjectLogService.ReadTailAsync(entry);

        Assert.Contains("crashline50", text);
    }

    [Fact]
    public async Task 崩溃目录里没有_log_时给出说明而不是报错()
    {
        var crashDir = Path.Combine(CrashesDir(), "UECC-Windows-NOLOG");
        Directory.CreateDirectory(crashDir);
        File.WriteAllText(Path.Combine(crashDir, "UEMinidump.dmp"), "binary");

        var entry = ProjectLogService.Enumerate(_root).Single();
        var text = await ProjectLogService.ReadTailAsync(entry);

        Assert.Contains("没有可读的 .log", text);
    }
}
