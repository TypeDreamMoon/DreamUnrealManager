using DreamUnrealManager.Helpers;

namespace DreamUnrealManager.Tests;

/// <summary>
/// 版本比较的回归测试。这块逻辑之前是一句字符串相等判断，
/// 导致离线和预发布 tag 两种情况下都会误报"有新版本"。
/// </summary>
public class SemanticVersionTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3, "")]
    [InlineData("v1.2.3", 1, 2, 3, "")]
    [InlineData("V1.2.3", 1, 2, 3, "")]
    [InlineData("0.4.0", 0, 4, 0, "")]
    [InlineData("1.2", 1, 2, 0, "")]
    [InlineData("1", 1, 0, 0, "")]
    [InlineData("v1.2.3.4", 1, 2, 3, "")]          // 第四段（Revision）忽略
    [InlineData("v1.2.3-beta.1", 1, 2, 3, "beta.1")]
    [InlineData("1.2.3-rc1", 1, 2, 3, "rc1")]
    [InlineData("1.2.3+build.7", 1, 2, 3, "")]      // 构建元数据丢弃
    [InlineData("  v1.2.3  ", 1, 2, 3, "")]
    public void TryParse_接受常见的_tag_写法(string input, int major, int minor, int patch, string pre)
    {
        Assert.True(SemanticVersion.TryParse(input, out var version));
        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(patch, version.Patch);
        Assert.Equal(pre, version.PreRelease);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("v")]
    [InlineData("1.x.3")]
    // 这条是关键回归：旧实现把这个错误字符串当版本号，导致离线时永远提示有更新
    [InlineData("获取GitHub发布版本失败: 无法连接到远程服务器")]
    public void TryParse_拒绝无法解析的输入(string? input)
    {
        Assert.False(SemanticVersion.TryParse(input, out _));
    }

    [Theory]
    [InlineData("1.0.0", "1.0.1")]
    [InlineData("1.0.0", "1.1.0")]
    [InlineData("1.0.0", "2.0.0")]
    [InlineData("1.9.0", "1.10.0")]   // 按数值比而不是字典序
    [InlineData("1.0.0-beta", "1.0.0")]      // 预发布版小于同号正式版
    [InlineData("1.0.0-alpha", "1.0.0-beta")]
    [InlineData("1.0.0-beta", "1.0.0-beta.1")]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.10")] // 数字段按数值比
    public void CompareTo_左边应当小于右边(string left, string right)
    {
        Assert.True(SemanticVersion.TryParse(left, out var a));
        Assert.True(SemanticVersion.TryParse(right, out var b));
        Assert.True(a < b, $"{a} 应当 < {b}");
        Assert.True(b > a);
    }

    [Theory]
    [InlineData("1.2.3", "v1.2.3")]
    [InlineData("1.2.3", "1.2.3+meta")]
    [InlineData("1.2", "1.2.0")]
    [InlineData("0.4.0", "v0.4.0.0")]
    public void CompareTo_视为相等(string left, string right)
    {
        Assert.True(SemanticVersion.TryParse(left, out var a));
        Assert.True(SemanticVersion.TryParse(right, out var b));
        Assert.Equal(0, a.CompareTo(b));
        Assert.True(a == b);
    }

    [Fact]
    public void 同一个正式版不应被判为有更新()
    {
        // 复现原始 bug 的场景：本机 0.4.0，GitHub tag v0.4.0
        Assert.True(SemanticVersion.TryParse("0.4.0", out var current));
        Assert.True(SemanticVersion.TryParse("v0.4.0", out var latest));
        Assert.False(latest > current);
    }

    [Fact]
    public void 预发布_tag_不应被判为比正式版新()
    {
        // 原始 bug：字符串比较下 "v0.4.0-beta.1" != "v0.4.0" → 误报有更新
        Assert.True(SemanticVersion.TryParse("0.4.0", out var current));
        Assert.True(SemanticVersion.TryParse("v0.4.0-beta.1", out var latest));
        Assert.False(latest > current);
    }

    [Fact]
    public void ToString_可回环解析()
    {
        Assert.True(SemanticVersion.TryParse("v2.10.3-rc.2", out var version));
        Assert.Equal("2.10.3-rc.2", version.ToString());

        Assert.True(SemanticVersion.TryParse(version.ToString(), out var roundTrip));
        Assert.Equal(version, roundTrip);
    }
}
