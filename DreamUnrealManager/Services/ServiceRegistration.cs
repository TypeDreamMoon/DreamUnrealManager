using DreamUnrealManager.Activation;
using DreamUnrealManager.Contracts.Services;
using DreamUnrealManager.Core.Contracts.Services;
using DreamUnrealManager.Core.Services;
using DreamUnrealManager.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace DreamUnrealManager.Services
{
    /// <summary>
    /// 应用服务的注册清单。
    ///
    /// 从 App.xaml.cs 里抽出来，是为了能在测试里直接构造容器验证注册完整性——
    /// 此前有六个接口定义了却从没注册，各页面只好自己 new，问题一直到运行时才暴露。
    /// 视图/ViewModel 的注册仍留在 App 里，它们需要 XAML 运行时，测不了也不该测。
    /// </summary>
    public static class ServiceRegistration
    {
        public static IServiceCollection AddAppServices(this IServiceCollection services)
        {
            // 激活处理器：IActivationService 直接依赖它们，必须和服务注册在一起，
            // 否则容器校验会在这里断掉（这条正是被 ServiceRegistrationTests 抓出来的）。
            services.AddTransient<ActivationHandler<LaunchActivatedEventArgs>, DefaultActivationHandler>();
            services.AddTransient<IActivationHandler, AppNotificationActivationHandler>();

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

            services.AddSingleton<IIdeLauncherService, IdeLauncherService>();
            services.AddSingleton<IProjectFilterService, ProjectFilterService>();
            services.AddSingleton<IEngineSwitchService, EngineSwitchService>();
            services.AddSingleton<IEditorLaunchService, EditorLaunchService>();
            services.AddSingleton<IProjectFactoryService, ProjectFactoryService>();
            services.AddSingleton<IProjectSearchService, ProjectSearchService>();

            services.AddTransient<IProjectDataService, ProjectDataService>();

            // Core Services
            services.AddSingleton<IFileService, FileService>();

            return services;
        }

        /// <summary>
        /// AddAppServices 注册的全部服务契约。测试遍历这份清单逐个解析，
        /// 保证「接口有了但忘了注册」「构造函数依赖没注册」这类问题能在 CI 上直接失败。
        /// </summary>
        public static IReadOnlyList<Type> RegisteredContracts { get; } = new[]
        {
            typeof(IAppNotificationService),
            typeof(IThemeSelectorService),
            typeof(INavigationViewService),
            typeof(IActivationService),
            typeof(IPageService),
            typeof(INavigationService),
            typeof(IDialogService),
            typeof(IEngineManagerService),
            typeof(IEngineResolverService),
            typeof(IBuildService),
            typeof(IProjectRepositoryService),
            typeof(IUnrealProjectService),
            typeof(IVersionCheckService),
            typeof(IIdeLauncherService),
            typeof(IProjectFilterService),
            typeof(IEngineSwitchService),
            typeof(IEditorLaunchService),
            typeof(IProjectFactoryService),
            typeof(IProjectSearchService),
            typeof(IProjectDataService),
            typeof(IFileService)
        };
    }
}
