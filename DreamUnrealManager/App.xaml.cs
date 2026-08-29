using System.Reflection;
using System.Linq;
using DreamUnrealManager.Activation;
using DreamUnrealManager.Contracts.Services;
using DreamUnrealManager.Core.Contracts.Services;
using DreamUnrealManager.Core.Services;
using DreamUnrealManager.Helpers;
using DreamUnrealManager.Models;
using DreamUnrealManager.Notifications;
using DreamUnrealManager.Services;
using DreamUnrealManager.ViewModels;
using DreamUnrealManager.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;

namespace DreamUnrealManager;

// To learn more about WinUI 3, see https://docs.microsoft.com/windows/apps/winui/winui3/.
public partial class App : Application
{
    // The .NET Generic Host provides dependency injection, configuration, logging, and other services.
    // https://docs.microsoft.com/dotnet/core/extensions/generic-host
    // https://docs.microsoft.com/dotnet/core/extensions/dependency-injection
    // https://docs.microsoft.com/dotnet/core/extensions/configuration
    // https://docs.microsoft.com/dotnet/core/extensions/logging
    public IHost Host
    {
        get;
    }

    public static T GetService<T>()
        where T : class
    {
        if ((App.Current as App)!.Host.Services.GetService(typeof(T)) is not T service)
        {
            throw new ArgumentException($"{typeof(T)} needs to be registered in ConfigureServices within App.xaml.cs.");
        }

        return service;
    }

    public static WindowEx MainWindow
    {
        get;
    } = new MainWindow();

    public static UIElement? AppTitlebar
    {
        get;
        set;
    }

    /// <summary>
    /// 项目仓库。原先这里是 <c>= new()</c>：类型初始化时先造一个孤儿实例，
    /// 到 OnLaunched 才换成容器里的单例，中间任何一次访问拿到的都是另一份缓存，
    /// 保存时会互相覆盖。改为始终走容器，保证全局只有一个实例。
    /// </summary>
    public static IProjectRepositoryService RepositoryService => GetService<IProjectRepositoryService>();

    public App()
    {
        InitializeComponent();

        Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder().UseContentRoot(AppContext.BaseDirectory).ConfigureServices((context, services) =>
        {
            // 服务与激活处理器的注册统一放在 ServiceRegistration，测试会遍历同一份清单验证可解析性。
            services.AddAppServices();

            // Views and ViewModels
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<SettingsPage>();
            services.AddTransient<PluginsBuildViewModel>();
            services.AddTransient<PluginsBuildPage>();
            services.AddTransient<FabComplianceViewModel>();
            services.AddTransient<FabCompliancePage>();
            services.AddTransient<LauncherViewModel>();
            services.AddTransient<LauncherPage>();
            services.AddTransient<MainViewModel>();
            services.AddTransient<MainPage>();
            services.AddTransient<ShellPage>();
            services.AddTransient<ShellViewModel>();

            services.AddTransient<FabPage>();
            services.AddTransient<FabViewModel>();

            services.AddTransient<UnrealLauncherPage>();
            services.AddTransient<UnrealLauncherViewModel>();
            services.AddTransient<UnrealHordePage>();
            services.AddTransient<UnrealHordeViewModel>();

            // Configuration
            services.Configure<LocalSettingsOptions>(context.Configuration.GetSection(nameof(LocalSettingsOptions)));
        }).Build();

        App.GetService<IAppNotificationService>().Initialize();

        UnhandledException += App_UnhandledException;

        // XAML 的 UnhandledException 只覆盖 UI 线程；后台线程与被丢弃的 Task 走另外两条通道，
        // 三条都接上才能保证崩溃现场一定落到日志里。
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        AppLogService.Info($"应用启动，版本 {AppVersion}", "App");
    }

    /// <summary>程序集版本，形如 0.4.0。供日志与版本检查共用，避免各处各写一份解析逻辑。</summary>
    public static string AppVersion
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version == null
                ? "0.0.0"
                : $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        AppLogService.Error($"UI 线程未捕获异常: {e.Message}", e.Exception, "App");
    }

    private static void OnDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        AppLogService.Error(
            $"后台线程未捕获异常 (IsTerminating={e.IsTerminating})",
            e.ExceptionObject as Exception,
            "App");
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLogService.Error("Task 未观察异常", e.Exception, "App");
        // 标记为已观察：这类异常通常来自 fire-and-forget 的 UI 操作，
        // 已经落盘就没必要再按进程级致命错误处理。
        e.SetObserved();
    }

    protected async override void OnLaunched(LaunchActivatedEventArgs args)
    {
        base.OnLaunched(args);

        // App.GetService<IAppNotificationService>().Show(string.Format("AppNotificationSamplePayload".GetLocalized(), AppContext.BaseDirectory));

        await App.GetService<IActivationService>().ActivateAsync(args);

        var opt = ThemeService.Load();
        ThemeService.ApplyToWindow(App.MainWindow, opt);

        await RepositoryService.LoadAsync();

        UiDispatcherService.Initialize(MainWindow);

        // 用户再次双击图标 / 从命令行拉起时，把已有窗口带到前台，而不是开第二个进程。
        Program.Redirected += OnInstanceRedirected;

        // 本次启动自带的命令行（例如从资源管理器「打开方式」拉起）。
        await HandleCommandLineAsync(CommandLineService.GetCurrentArguments());
    }

    private void OnInstanceRedirected(Microsoft.Windows.AppLifecycle.AppActivationArguments args)
    {
        MainWindow.DispatcherQueue.TryEnqueue(async () =>
        {
            if (MainWindow is MainWindow window)
            {
                window.RestoreFromTray();
            }

            // 后续实例可能是双击 .uproject 拉起的，命令行随激活参数一起转发了过来。
            try
            {
                var commandLine = (args.Data as Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs)?.Arguments;
                await HandleCommandLineAsync(CommandLineService.SplitArguments(commandLine));
            }
            catch (Exception ex)
            {
                AppLogService.Error("处理重定向命令行失败", ex, "App");
            }
        });
    }

    /// <summary>处理命令行请求：定位到目标项目，必要时直接启动编辑器。</summary>
    private static async Task HandleCommandLineAsync(IReadOnlyList<string> args)
    {
        try
        {
            var request = CommandLineService.Parse(args);
            if (!request.HasWork)
            {
                return;
            }

            AppLogService.Info(
                $"命令行请求: {request.ProjectPath}（直接启动={request.LaunchImmediately}）", "App");

            var repository = GetService<IProjectRepositoryService>();
            var projects = await repository.LoadAsync();

            var match = projects.FirstOrDefault(p =>
                string.Equals(p.ProjectPath, request.ProjectPath, StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                // 命令行指定的项目还不在列表里，顺手加进来——
                // 用户从资源管理器双击过来，期望的就是"能用"，而不是"先去添加一遍"。
                match = await GetService<IProjectFactoryService>().CreateAsync(request.ProjectPath!);
                if (match != null)
                {
                    projects.Add(match);
                    await repository.SaveAsync(projects);
                    AppLogService.Info($"已将命令行项目加入列表: {request.ProjectPath}", "App");
                }
            }

            if (match == null)
            {
                AppLogService.Warn($"无法解析命令行项目: {request.ProjectPath}", "App");
                return;
            }

            if (request.LaunchImmediately)
            {
                await GetService<IUnrealProjectService>().LaunchProject(match);
            }
            else
            {
                GetService<INavigationService>().NavigateTo(typeof(LauncherViewModel).FullName!);
            }
        }
        catch (Exception ex)
        {
            AppLogService.Error("处理命令行参数失败", ex, "App");
        }
    }
}
