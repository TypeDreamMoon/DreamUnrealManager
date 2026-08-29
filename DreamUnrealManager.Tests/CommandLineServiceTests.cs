using DreamUnrealManager.Services;

namespace DreamUnrealManager.Tests;

public class CommandLineServiceTests
{
    // 存在性判断注入掉，测试不依赖真实文件系统
    private static readonly Func<string, bool> AlwaysExists = _ => true;
    private static readonly Func<string, bool> NeverExists = _ => false;

    [Fact]
    public void 无参数时不产生任何工作()
    {
        var request = CommandLineService.Parse(Array.Empty<string>(), AlwaysExists);

        Assert.False(request.HasWork);
        Assert.Null(request.ProjectPath);
    }

    [Fact]
    public void null_参数不抛异常()
    {
        var request = CommandLineService.Parse(null, AlwaysExists);
        Assert.False(request.HasWork);
    }

    [Fact]
    public void 识别_uproject_路径()
    {
        var request = CommandLineService.Parse(new[] { @"C:\Games\MyGame\MyGame.uproject" }, AlwaysExists);

        Assert.True(request.HasWork);
        Assert.Equal(@"C:\Games\MyGame\MyGame.uproject", request.ProjectPath);
        Assert.False(request.LaunchImmediately);
    }

    [Fact]
    public void 剥掉外部传入的引号()
    {
        // explorer 与批处理常把路径整段带引号传进来
        var request = CommandLineService.Parse(new[] { "\"C:\\Games\\My Game\\My Game.uproject\"" }, AlwaysExists);

        Assert.Equal(@"C:\Games\My Game\My Game.uproject", request.ProjectPath);
    }

    [Theory]
    [InlineData("--launch")]
    [InlineData("--LAUNCH")]
    [InlineData("-l")]
    public void launch_开关触发直接启动(string flag)
    {
        var request = CommandLineService.Parse(new[] { flag, @"C:\G\A.uproject" }, AlwaysExists);

        Assert.True(request.LaunchImmediately);
        Assert.Equal(@"C:\G\A.uproject", request.ProjectPath);
    }

    [Fact]
    public void 开关放在路径之后同样生效()
    {
        var request = CommandLineService.Parse(new[] { @"C:\G\A.uproject", "--launch" }, AlwaysExists);

        Assert.True(request.LaunchImmediately);
        Assert.Equal(@"C:\G\A.uproject", request.ProjectPath);
    }

    [Fact]
    public void 忽略非_uproject_扩展名()
    {
        var request = CommandLineService.Parse(new[] { @"C:\G\A.uasset", @"C:\G\B.txt" }, AlwaysExists);

        Assert.False(request.HasWork);
    }

    [Fact]
    public void 文件不存在时不采纳()
    {
        var request = CommandLineService.Parse(new[] { @"C:\G\Missing.uproject" }, NeverExists);

        Assert.False(request.HasWork);
    }

    [Fact]
    public void 未知开关被忽略而不是导致失败()
    {
        // 命令行来源不可控，多一个参数不应该让程序起不来
        var request = CommandLineService.Parse(
            new[] { "--unknown", "-x", @"C:\G\A.uproject" }, AlwaysExists);

        Assert.True(request.HasWork);
        Assert.Equal(@"C:\G\A.uproject", request.ProjectPath);
    }

    [Fact]
    public void 只取第一个有效项目路径()
    {
        var request = CommandLineService.Parse(
            new[] { @"C:\G\A.uproject", @"C:\G\B.uproject" }, AlwaysExists);

        Assert.Equal(@"C:\G\A.uproject", request.ProjectPath);
    }

    [Fact]
    public void 空白参数被跳过()
    {
        var request = CommandLineService.Parse(
            new[] { "", "   ", @"C:\G\A.uproject" }, AlwaysExists);

        Assert.Equal(@"C:\G\A.uproject", request.ProjectPath);
    }

    // ---------- SplitArguments ----------

    [Fact]
    public void 切分带引号的命令行()
    {
        var parts = CommandLineService.SplitArguments("\"C:\\My Games\\A.uproject\" --launch");

        Assert.Equal(2, parts.Count);
        Assert.Equal(@"C:\My Games\A.uproject", parts[0]);
        Assert.Equal("--launch", parts[1]);
    }

    [Fact]
    public void 切分普通空格分隔的命令行()
    {
        var parts = CommandLineService.SplitArguments("--launch C:\\A.uproject");

        Assert.Equal(new[] { "--launch", @"C:\A.uproject" }, parts);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 切分空命令行返回空集合(string? input)
    {
        Assert.Empty(CommandLineService.SplitArguments(input));
    }

    [Fact]
    public void 切分后可直接交给_Parse()
    {
        var parts = CommandLineService.SplitArguments("\"C:\\My Games\\A.uproject\"");
        var request = CommandLineService.Parse(parts, AlwaysExists);

        Assert.Equal(@"C:\My Games\A.uproject", request.ProjectPath);
    }
}
