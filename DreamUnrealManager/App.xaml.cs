using System.Reflection;
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

    public static ProjectRepositoryService RepositoryService
    {
        get;
        set;
    } = new();

    public App()
    {
        InitializeComponent();

        Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder().UseContentRoot(AppContext.BaseDirectory).ConfigureServices((context, services) =>
        {
            // Default Activation Handler
            services.AddTransient<ActivationHandler<LaunchActivatedEventArgs>, DefaultActivationHandler>();

            // Other Activation Handlers
            services.AddTransient<IActivationHandler, AppNotificationActivationHandler>();

            // Services
            services.AddSingleton<IAppNotificationService, AppNotificationService>();
            services.AddSingleton<IThemeSelectorService, ThemeSelectorService>();
            services.AddTransient<INavigationViewService, NavigationViewService>();

            services.AddSingleton<IActivationService, ActivationService>();
            services.AddSingleton<IPageService, PageService>();
            services.AddSingleton<INavigationService, NavigationService>();

            services.AddSingleton<IDialogService, DialogService>();

            services.AddSingleton<IEngineManagerService>(_ => EngineManagerService.Instance);
            services.AddSingleton<IEngineResolverService, EngineResolverService>();
            services.AddSingleton<IBuildService, BuildService>();
            services.AddSingleton<IProjectRepositoryService, ProjectRepositoryService>();
            services.AddSingleton<IUnrealProjectService, UnrealProjectService>();
            services.AddSingleton<IVersionCheckService, VersionCheckService>();

            // 以下服务此前只定义了接口却从未注册，各页面只能自己 new，
            // 既绕开了单例语义，也让依赖关系无法在一处看清。
            services.AddSingleton<IIdeLauncherService, IdeLauncherService>();
            services.AddSingleton<IProjectFilterService, ProjectFilterService>();
            services.AddSingleton<IEngineSwitchService, EngineSwitchService>();
            services.AddSingleton<IEditorLaunchService, EditorLaunchService>();
            services.AddSingleton<IProjectFactoryService, ProjectFactoryService>();
            services.AddSingleton<IProjectSearchService, ProjectSearchService>();

            services.AddTransient<IProjectDataService, ProjectDataService>();

            // Core Services
            services.AddSingleton<IFileService, FileService>();

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

        RepositoryService = (ProjectRepositoryService)App.GetService<IProjectRepositoryService>();
        await RepositoryService.LoadAsync();

        UiDispatcherService.Initialize(MainWindow);
    }
}
