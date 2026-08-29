using DreamUnrealManager.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DreamUnrealManager.Tests;

/// <summary>
/// DI 注册完整性。
///
/// 这组测试针对的是一类真实存在过的问题：IIdeLauncherService / IProjectFilterService 等
/// 六个接口定义了却从未注册，各页面只好自己 new，问题一直到运行时打开对应页面才暴露。
/// 现在只要有人加了接口忘了注册、或者给服务加了个没注册的构造参数，CI 就会直接红。
/// </summary>
public class ServiceRegistrationTests
{
    private static ServiceProvider BuildProvider() =>
        new ServiceCollection()
            .AddAppServices()
            // ValidateOnBuild 会在构造容器时就检查所有可解析性，而不是等到第一次 GetService
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });

    [Fact]
    public void 容器能够构建且通过内建校验()
    {
        using var provider = BuildProvider();
        Assert.NotNull(provider);
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public void 每个已声明的服务契约都能解析(Type contractType)
    {
        using var provider = BuildProvider();

        var instance = provider.GetService(contractType);

        Assert.True(instance != null, $"{contractType.Name} 无法从容器解析——是不是忘了在 AddAppServices 里注册？");
        Assert.IsAssignableFrom(contractType, instance);
    }

    [Fact]
    public void 单例服务重复解析返回同一实例()
    {
        using var provider = BuildProvider();

        // 项目仓库必须是全局唯一的：多份实例各持一份缓存，保存时会互相覆盖。
        var first = provider.GetService<DreamUnrealManager.Contracts.Services.IProjectRepositoryService>();
        var second = provider.GetService<DreamUnrealManager.Contracts.Services.IProjectRepositoryService>();

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    public static TheoryData<Type> Contracts
    {
        get
        {
            var data = new TheoryData<Type>();
            foreach (var contract in ServiceRegistration.RegisteredContracts)
            {
                data.Add(contract);
            }

            return data;
        }
    }
}
