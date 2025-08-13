using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
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
using Newtonsoft.Json.Linq;

namespace DreamUnrealManager.Views;

public sealed partial class MainPage : Page
{
    private readonly IProjectRepositoryService _repo;
    private readonly IProjectFilterService _projectFilterService;
    private readonly IBuildService _build;
    private readonly IIdeLauncherService _ide;
    private readonly IDialogService _dialogs;
    private readonly IUnrealProjectService _uproj;

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

        _repo = new ProjectRepositoryService();
        _projectFilterService = new ProjectFilterService();
        _build = new BuildService();
        _ide = new IdeLauncherService();
        _dialogs = new DialogService();
        _uproj = new UnrealProjectService();


        InitializeComponent();

        // 加载收藏项目
        LoadFavoriteProjects();

        Loaded += MainPage_Loaded;
    }

    private async void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        await CheckVersion();
    }

    private async Task CheckVersion()
    {
        try
        {
            var appVersion = GetAppVersion();
            CurrentVersionTextBlock.Text = $"当前版本: {appVersion}";
            var githubRelease = await GetLatestGithubRelease("TypeDreamMoon", "DreamUnrealManager");
            GithubVersionTextBlock.Text = $"最新版本: {githubRelease}";
            NewVersionHyperLinkButton.Visibility = githubRelease != appVersion ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            throw;
        }
    }

    private void QuickBuildingButton_OnClick(object sender, RoutedEventArgs e)
    {
        App.GetService<INavigationService>().Frame.Navigate(typeof(PluginsBuildPage));
    }

    private void QuickLaunchButton_OnClick(object sender, RoutedEventArgs e)
    {
        App.GetService<INavigationService>().Frame.Navigate(typeof(LauncherPage));
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
                string path = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "UnrealIcon.png");
                img.Source = new BitmapImage(new Uri(path));
            }
        }
        catch
        {
        }
    }

    private async void OpenWithIDE_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button item && item.Tag is ProjectInfo p) await _ide.LaunchAsync(p);
    }

    public string GetAppVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return $"v{version.Major}.{version.Minor}.{version.Build}";
    }

    public async Task<string> GetLatestGithubRelease(string owner, string repo)
    {
        try
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "request"); // GitHub API requires a User-Agent header

            // GitHub API 地址
            var url = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";

            // 获取响应
            var response = await client.GetStringAsync(url);

            // 解析 JSON 响应
            var releaseData = JObject.Parse(response);
            var latestVersion = releaseData["tag_name"]?.ToString(); // 获取最新的版本号

            return latestVersion ?? "无法获取最新版本";
        }
        catch (Exception ex)
        {
            return $"获取GitHub发布版本失败: {ex.Message}";
        }
    }
}