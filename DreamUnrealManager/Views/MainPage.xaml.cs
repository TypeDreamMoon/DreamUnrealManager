using System.Collections.ObjectModel;
using System.Diagnostics;
using Windows.ApplicationModel;
using Windows.ApplicationModel.DataTransfer;
using CommunityToolkit.WinUI.Controls;
using DreamUnrealManager.Contracts.Services;
using DreamUnrealManager.Helpers;
using DreamUnrealManager.Models;
using DreamUnrealManager.Services;
using DreamUnrealManager.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DreamUnrealManager.Views;

public sealed partial class MainPage : Page
{
    private readonly IProjectRepositoryService _repo;
    private readonly IProjectFilterService _projectFilterService;
    private readonly IBuildService _build;
    private readonly IIdeLauncherService _ide;
    private readonly IDialogService _dialogs;
    private readonly IUnrealProjectService _uproj;
    private readonly IVersionCheckService _versionCheck;

    public ObservableCollection<ProjectInfo> FavoritesProjects
    {
        get;
        set;
    } = new ObservableCollection<ProjectInfo>();


    public MainViewModel ViewModel
    {
        get;
    }


    public MainPage()
    {
        ViewModel = App.GetService<MainViewModel>();

        _repo = App.GetService<IProjectRepositoryService>();
        _projectFilterService = App.GetService<IProjectFilterService>();
        _build = App.GetService<IBuildService>();
        _ide = App.GetService<IIdeLauncherService>();
        _dialogs = App.GetService<IDialogService>();
        _uproj = App.GetService<IUnrealProjectService>();
        _versionCheck = App.GetService<IVersionCheckService>();


        InitializeComponent();

        // 加载收藏项目
        LoadFavoriteProjects();

        Loaded += MainPage_Loaded;
    }

    private async void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        // async void 事件处理器：异常一旦逃逸就是进程级崩溃，必须自己兜住。
        try
        {
            await CheckVersionAsync();
        }
        catch (Exception ex)
        {
            AppLogService.Error("首页版本检查失败", ex, "MainPage");
        }
    }

    private async Task CheckVersionAsync()
    {
        CurrentVersionTextBlock.Text = $"当前版本: v{App.AppVersion}";
        GithubVersionTextBlock.Text = "正在检查...";
        NewVersionHyperLinkButton.Visibility = Visibility.Collapsed;

        var result = await _versionCheck.CheckAsync();

        switch (result.Status)
        {
            case UpdateCheckStatus.UpdateAvailable:
                GithubVersionTextBlock.Text = $"最新版本: v{result.LatestVersion}";
                NewVersionHyperLinkButton.Visibility = Visibility.Visible;
                if (!string.IsNullOrWhiteSpace(result.ReleaseUrl))
                {
                    NewVersionHyperLinkButton.NavigateUri = new Uri(result.ReleaseUrl);
                }

                break;

            case UpdateCheckStatus.UpToDate:
                GithubVersionTextBlock.Text = "已是最新版本";
                break;

            case UpdateCheckStatus.Ahead:
                // 本地开发构建比已发布版本新，属正常情况，不该提示“有更新”。
                GithubVersionTextBlock.Text = $"本机版本领先于已发布版本 (v{result.LatestVersion})";
                break;

            default:
                // 关键：检查失败绝不能走到“发现新版本”分支。
                GithubVersionTextBlock.Text = $"检查更新失败: {result.Error}";
                break;
        }
    }

    private void QuickBuildingButton_OnClick(object sender, RoutedEventArgs e)
    {
        App.GetService<INavigationService>().NavigateTo(typeof(PluginsBuildViewModel).FullName!);
    }

    private void QuickLaunchButton_OnClick(object sender, RoutedEventArgs e)
    {
        App.GetService<INavigationService>().NavigateTo(typeof(LauncherViewModel).FullName!);
    }

    private async void LoadFavoriteProjects()
    {
        // 从仓库加载项目列表
        var allProjects = await _repo.LoadAsync();

        // 使用过滤器筛选收藏项目
        var filteredProjects = _projectFilterService.FilterAndSort(allProjects, new ProjectFilterOptions { OnlyFavorites = true });

        // 清空并加载筛选后的收藏项目
        FavoritesProjects.Clear();
        foreach (var project in filteredProjects)
        {
            FavoritesProjects.Add(project);
        }
    }

    private void QuickLaunchFavoriteProject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is ProjectInfo selectedProject)
        {
            // 启动项目（根据需要调用启动方法）
            _uproj.LaunchProject(selectedProject);
        }
    }

    private void ProjectImage_ImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        try
        {
            if (sender is Image img)
            {
                // 必须用 BaseDirectory 而不是 GetCurrentDirectory()：工作目录不等于安装目录，
                // 文件选择器等操作都会改动它，占位图会在用户第一次选过文件之后失效。
                var path = Path.Combine(AppContext.BaseDirectory, "Assets", "UnrealIcon.png");
                img.Source = new BitmapImage(new Uri(path));
            }
        }
        catch (Exception ex)
        {
            AppLogService.Warn($"加载项目占位图失败: {ex.Message}", "Image");
        }
    }

    private async void OpenWithIDE_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button item && item.Tag is ProjectInfo p) await _ide.LaunchAsync(p);
    }

}
