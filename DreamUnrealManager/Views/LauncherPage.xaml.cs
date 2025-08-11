using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Diagnostics;
using Windows.Storage.Pickers;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media;
using System.Linq;
using System.Runtime.CompilerServices;
using Windows.Foundation;
using Windows.UI;
using CommunityToolkit.WinUI;
using DreamUnrealManager.Models;
using DreamUnrealManager.Services;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using WinRT;

namespace DreamUnrealManager.Views
{
    public sealed partial class LauncherPage : Page
    {
        private List<ProjectInfo> _allProjects;
        private ObservableCollection<ProjectInfo> _filteredProjects;
        private bool _isSearching = false;
        private CancellationTokenSource _searchCancellationTokenSource;

        public ObservableCollection<ProjectInfo> FilteredProjects
        {
            get => _filteredProjects;
            set
            {
                _filteredProjects = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;


        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }


        private string _currentSearchText = "";
        private string _currentEngineFilter = "ALL_ENGINES";
        private string _currentSortOrder = "LastUsed";
        private ContentDialog _progressDialog;
        private TextBox _progressOutputTextBox;
        private ContentDialog _searchProgressDialog;
        private ProgressBar _searchProgressBar;
        private ProjectCardLayoutMode _currentLayoutMode = ProjectCardLayoutMode.List;
        private bool _suppressSegmentedEvent = false;

        public LauncherPage()
        {
            try
            {
                this.InitializeComponent();

                _allProjects = new List<ProjectInfo>();
                _filteredProjects = new ObservableCollection<ProjectInfo>();

                LoadLayoutModeFromSettings();

                this.Loaded += LauncherPage_Loaded;

                // 设置默认状态
                if (StatusText != null)
                    StatusText.Text = "正在初始化...";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LauncherPage构造函数失败: {ex.Message}");
            }
        }

        private void LoadLayoutModeFromSettings()
        {
            var saved = Settings.Get("UI.LayoutMode", "Grid");
            _currentLayoutMode = saved switch
            {
                "List" => ProjectCardLayoutMode.List,
                "Compact" => ProjectCardLayoutMode.Compact,
                _ => ProjectCardLayoutMode.Grid
            };
        }

        private void SaveLayoutModeToSettings(ProjectCardLayoutMode mode)
        {
            var str = mode switch
            {
                ProjectCardLayoutMode.List => "List",
                ProjectCardLayoutMode.Compact => "Compact",
                _ => "Grid"
            };
            Settings.Set("UI.LayoutMode", str);
        }


        private async void LauncherPage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                WriteDebug("页面开始加载");

                if (StatusText != null)
                    StatusText.Text = "正在加载项目...";

                await LoadProjects();
                LoadEngineFilters();
                ApplyFilters();
                UpdateLayoutMode();

                if (StatusText != null)
                    StatusText.Text = "页面加载完成";

                WriteDebug("页面加载成功");
            }
            catch (Exception ex)
            {
                WriteDebug($"页面加载失败: {ex.Message}");
                if (StatusText != null)
                    StatusText.Text = $"加载失败: {ex.Message}";
            }
        }

        private void WriteDebug(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[LauncherPage] {message}");
        }

        private async Task LoadProjects()
        {
            try
            {
                WriteDebug("开始加载项目");

                if (StatusText != null)
                    StatusText.Text = "正在加载项目...";

                // 使用新的项目数据服务加载项目
                _allProjects = await ProjectDataService.Instance.LoadProjectsAsync();
                await RehydrateProjectsAsync(_allProjects);

                WriteDebug($"项目加载完成，共 {_allProjects.Count} 个项目");

                // 输出详细的项目信息用于调试
                foreach (var project in _allProjects)
                {
                    WriteDebug($"加载的项目: {project.DisplayName} - {project.ProjectPath}");
                }

                if (StatusText != null)
                    StatusText.Text = $"加载完成，共 {_allProjects.Count} 个项目";
            }
            catch (Exception ex)
            {
                WriteDebug($"LoadProjects失败: {ex.Message}");
                WriteDebug($"堆栈跟踪: {ex.StackTrace}");

                // 如果新服务失败，尝试从旧格式加载
                try
                {
                    WriteDebug("尝试从旧格式加载项目");
                    await LoadProjectsFromOldFormat();
                }
                catch (Exception oldEx)
                {
                    WriteDebug($"从旧格式加载也失败: {oldEx.Message}");
                    if (StatusText != null)
                        StatusText.Text = $"加载失败: {ex.Message}";
                    throw;
                }
            }
        }

        // 添加从旧格式加载的备用方法
        private async Task LoadProjectsFromOldFormat()
        {
            try
            {
                WriteDebug("开始从旧格式加载项目");

                var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                if (localSettings.Values.ContainsKey("ProjectsData"))
                {
                    var savedData = localSettings.Values["ProjectsData"] as string;
                    if (!string.IsNullOrEmpty(savedData))
                    {
                        WriteDebug($"找到旧格式项目数据: {savedData}");

                        var projectPaths = savedData.Split('|');
                        foreach (var path in projectPaths)
                        {
                            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                            {
                                try
                                {
                                    var projectInfo = await CreateProjectInfo(path);
                                    if (projectInfo != null)
                                    {
                                        _allProjects.Add(projectInfo);
                                        WriteDebug($"从旧格式成功加载项目: {projectInfo.DisplayName}");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    WriteDebug($"从旧格式加载单个项目失败 {path}: {ex.Message}");
                                }
                            }
                        }

                        // 迁移到新格式
                        WriteDebug("将项目迁移到新格式");
                        await SaveProjects();
                    }
                }

                WriteDebug($"从旧格式项目加载完成，共 {_allProjects.Count} 个项目");
            }
            catch (Exception ex)
            {
                WriteDebug($"LoadProjectsFromOldFormat失败: {ex.Message}");
                throw;
            }
        }

        private async Task<ProjectInfo> CreateProjectInfo(string projectPath)
        {
            try
            {
                WriteDebug($"创建项目信息: {projectPath}");

                var projectInfo = new ProjectInfo
                {
                    ProjectPath = projectPath,
                    DisplayName = Path.GetFileNameWithoutExtension(projectPath),
                    ProjectDirectory = Path.GetDirectoryName(projectPath),
                    LastModified = File.GetLastWriteTime(projectPath)
                };

                // 尝试解析项目文件
                try
                {
                    var content = await File.ReadAllTextAsync(projectPath);
                    using var jsonDoc = System.Text.Json.JsonDocument.Parse(content);
                    var root = jsonDoc.RootElement;

                    if (root.TryGetProperty("EngineAssociation", out var engineAssoc))
                    {
                        projectInfo.EngineAssociation = engineAssoc.GetString() ?? "";
                    }

                    if (root.TryGetProperty("Description", out var desc))
                    {
                        projectInfo.Description = desc.GetString() ?? "";
                    }

                    if (root.TryGetProperty("Category", out var cat))
                    {
                        projectInfo.Category = cat.GetString() ?? "";
                    }

                    // 解析模块和插件信息
                    if (root.TryGetProperty("Modules", out var modulesElement) && modulesElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        var modules = new List<ProjectModule>();
                        foreach (var moduleElement in modulesElement.EnumerateArray())
                        {
                            var module = new ProjectModule();
                            if (moduleElement.TryGetProperty("Name", out var nameElement))
                                module.Name = nameElement.GetString();

                            if (moduleElement.TryGetProperty("Type", out var typeElement))
                                module.Type = typeElement.GetString();

                            if (moduleElement.TryGetProperty("LoadingPhase", out var loadingPhaseElement))
                                module.LoadingPhase = loadingPhaseElement.GetString();

                            modules.Add(module);
                        }

                        projectInfo.Modules = modules;
                    }

                    if (root.TryGetProperty("Plugins", out var pluginsElement) && pluginsElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        var plugins = new List<ProjectPlugin>();
                        foreach (var pluginElement in pluginsElement.EnumerateArray())
                        {
                            var plugin = new ProjectPlugin();
                            if (pluginElement.TryGetProperty("Name", out var nameElement))
                                plugin.Name = nameElement.GetString();

                            if (pluginElement.TryGetProperty("Enabled", out var enabledElement))
                                plugin.Enabled = enabledElement.GetBoolean();

                            plugins.Add(plugin);
                        }

                        projectInfo.Plugins = plugins;
                    }

                    WriteDebug($"解析项目文件成功: {projectInfo.DisplayName}");
                }
                catch (Exception ex)
                {
                    WriteDebug($"解析项目文件失败，使用默认值: {ex.Message}");
                    projectInfo.EngineAssociation = "未知版本";
                    projectInfo.Description = "无法读取项目描述";
                }

                // 关联引擎信息
                await AssociateEngineWithProject(projectInfo);

                // 刷新缩略图和Git状态
                projectInfo.RefreshThumbnail();
                projectInfo.CheckGitStatus();

                return projectInfo;
            }
            catch (Exception ex)
            {
                WriteDebug($"CreateProjectInfo失败: {ex.Message}");
                return null;
            }
        }

        private async Task AssociateEngineWithProject(ProjectInfo project)
        {
            try
            {
                if (string.IsNullOrEmpty(project.EngineAssociation))
                    return;

                // 获取引擎管理器实例
                var engineManager = EngineManagerService.Instance;
                await engineManager.LoadEngines(); // 确保引擎已加载

                var engines = engineManager.GetValidEngines();
                if (engines == null)
                    return;

                // 查找匹配的引擎
                var matchingEngine = engines.FirstOrDefault(e =>
                    e.Version == project.EngineAssociation ||
                    e.FullVersion == project.EngineAssociation ||
                    (e.BuildVersionInfo?.BranchName?.Contains(project.EngineAssociation) ?? false));

                if (matchingEngine != null)
                {
                    project.AssociatedEngine = matchingEngine;
                    WriteDebug($"项目 {project.DisplayName} 关联到引擎 {matchingEngine.DisplayName}");
                }
                else
                {
                    WriteDebug($"未找到与项目 {project.DisplayName} 关联的引擎 (关联标识: {project.EngineAssociation})");
                }
            }
            catch (Exception ex)
            {
                WriteDebug($"AssociateEngineWithProject失败: {ex.Message}");
            }
        }


        private void LoadEngineFilters()
        {
            try
            {
                WriteDebug("开始加载引擎筛选器");

                if (EngineFilterComboBox == null)
                {
                    WriteDebug("EngineFilterComboBox为空，跳过加载");
                    return;
                }

                EngineFilterComboBox.Items.Clear();

                var allItem = new ComboBoxItem { Content = "所有引擎版本", Tag = "ALL_ENGINES" };
                EngineFilterComboBox.Items.Add(allItem);

                var engineVersions = _allProjects
                    .Select(p => p.EngineAssociation)
                    .Where(v => !string.IsNullOrEmpty(v))
                    .Distinct()
                    .OrderByDescending(v => v)
                    .ToList();

                foreach (var version in engineVersions)
                {
                    var item = new ComboBoxItem { Content = $"UE {version}", Tag = version };
                    EngineFilterComboBox.Items.Add(item);
                }

                EngineFilterComboBox.SelectedIndex = 0;
                WriteDebug($"引擎筛选器加载完成，共 {EngineFilterComboBox.Items.Count} 项");
            }
            catch (Exception ex)
            {
                WriteDebug($"LoadEngineFilters失败: {ex.Message}");
            }
        }

        private void ApplyFilters()
        {
            try
            {
                WriteDebug("开始应用筛选");

                var filtered = _allProjects.AsEnumerable();

                // 搜索筛选
                if (!string.IsNullOrWhiteSpace(_currentSearchText))
                {
                    var searchLower = _currentSearchText.ToLower();
                    filtered = filtered.Where(p =>
                        (p.DisplayName?.ToLower()?.Contains(searchLower) ?? false) ||
                        (p.Description?.ToLower()?.Contains(searchLower) ?? false) ||
                        (p.EngineAssociation?.ToLower()?.Contains(searchLower) ?? false) ||
                        (p.ProjectDirectory?.ToLower()?.Contains(searchLower) ?? false)
                    );
                }

                // 引擎版本筛选
                if (_currentEngineFilter != "ALL_ENGINES")
                {
                    filtered = filtered.Where(p => p.EngineAssociation == _currentEngineFilter);
                }

                // 排序
                filtered = _currentSortOrder switch
                {
                    "Name" => filtered.OrderBy(p => p.DisplayName),
                    "Engine" => filtered.OrderBy(p => p.EngineAssociation).ThenBy(p => p.DisplayName),
                    "Size" => filtered.OrderByDescending(p => p.ProjectSize).ThenBy(p => p.DisplayName),
                    "Modified" => filtered.OrderByDescending(p => p.LastModified).ThenBy(p => p.DisplayName),
                    _ => filtered.OrderByDescending(p => p.LastUsed ?? DateTime.MinValue).ThenBy(p => p.DisplayName)
                };

                var filteredList = filtered.ToList();

                // 正确地更新ObservableCollection
                _filteredProjects.Clear();
                foreach (var project in filteredList)
                {
                    _filteredProjects.Add(project);
                }

                WriteDebug($"筛选完成，从 {_allProjects.Count} 筛选出 {_filteredProjects.Count} 个项目");

                UpdateEmptyState();
                UpdateFilterStats();
            }
            catch (Exception ex)
            {
                WriteDebug($"ApplyFilters失败: {ex.Message}");
                if (StatusText != null)
                    StatusText.Text = $"筛选失败: {ex.Message}";
            }
        }

        // 添加以下方法到 LauncherPage 类中

        private void ProjectItem_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
        {
            // 获取触发事件的项目项
            var projectItem = sender as FrameworkElement;
            if (projectItem?.DataContext is ProjectInfo project)
            {
                // 获取上下文菜单
                var contextMenu = projectItem.Resources["ProjectContextMenu"] as MenuFlyout;
                if (contextMenu != null)
                {
                    // 为每个菜单项设置 Tag（确保数据绑定正确）
                    foreach (var item in contextMenu.Items)
                    {
                        if (item is MenuFlyoutItem menuFlyoutItem)
                        {
                            menuFlyoutItem.Tag = project;
                        }
                    }

                    // 显示上下文菜单
                    contextMenu.ShowAt(projectItem, args.TryGetPosition(projectItem, out var point) ? point : new Point(0, 0));

                    // 标记事件已处理
                    args.Handled = true;
                }
            }
        }

// 上下文菜单项点击事件处理
        private void ContextMenu_LaunchProject_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is ProjectInfo project)
            {
                LaunchProject(project);
            }
        }

        private async void ContextMenu_OpenWithIDE_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is ProjectInfo project)
            {
                await OpenWithIDE(project);
            }
        }

        private void ContextMenu_OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is ProjectInfo project)
            {
                OpenProjectFolder(project);
            }
        }

        private void ContextMenu_GenerateVSProject_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is ProjectInfo project)
            {
                GenerateVisualStudioProjectFiles(project);
            }
        }

        private void ContextMenu_RefreshProject_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is ProjectInfo project)
            {
                RefreshProject(project);
            }
        }

        private void ContextMenu_SwitchEngine_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is ProjectInfo project)
            {
                SwitchEngineVersion(project);
            }
        }

        private void ContextMenu_RemoveProject_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is ProjectInfo project)
            {
                RemoveProject(project);
            }
        }


        private void ShowProjectMenu(ProjectInfo project, Button moreButton)
        {
            try
            {
                var flyout = new MenuFlyout();

                var openFolderItem = new MenuFlyoutItem { Text = "打开项目文件夹", Tag = project };
                openFolderItem.Click += (s, e) => OpenProjectFolder(project);
                flyout.Items.Add(openFolderItem);

                var generateVSItem = new MenuFlyoutItem { Text = "生成 Visual Studio 项目文件", Tag = project };
                generateVSItem.Click += (s, e) => GenerateVisualStudioProjectFiles(project);
                flyout.Items.Add(generateVSItem);

                var refresh = new MenuFlyoutItem() { Text = "刷新项目", Tag = project };
                refresh.Click += (s, e) => RefreshProject(project);
                flyout.Items.Add(refresh);

                var switchEngineItem = new MenuFlyoutItem { Text = "切换引擎版本", Tag = project };
                switchEngineItem.Click += (s, e) => SwitchEngineVersion(project);
                flyout.Items.Add(switchEngineItem);

                var removeItem = new MenuFlyoutItem { Text = "从列表中移除", Tag = project };
                removeItem.Click += (s, e) => RemoveProject(project);
                flyout.Items.Add(removeItem);

                flyout.ShowAt(moreButton);
            }
            catch (Exception ex)
            {
                WriteDebug($"ShowProjectMenu失败: {ex.Message}");
            }
        }

        private void UpdateFilterStats()
        {
            try
            {
                if (FilterStatsText == null) return;

                var totalProjects = _allProjects.Count;
                var filteredCount = _filteredProjects.Count;

                FilterStatsText.Text = totalProjects == filteredCount
                    ? $"显示 {totalProjects} 个项目"
                    : $"显示 {filteredCount} / {totalProjects} 个项目";
            }
            catch (Exception ex)
            {
                WriteDebug($"UpdateFilterStats失败: {ex.Message}");
            }
        }

        private void UpdateEmptyState()
        {
            try
            {
                if (EmptyStatePanel == null) return;

                if (_filteredProjects.Count == 0)
                {
                    EmptyStatePanel.Visibility = Visibility.Visible;
                    ProjectsListView.Visibility = Visibility.Collapsed;

                    if (_allProjects.Count == 0)
                    {
                        if (EmptyStateTitle != null) EmptyStateTitle.Text = "暂无项目";
                        if (EmptyStateMessage != null) EmptyStateMessage.Text = "点击下方按钮来添加你的第一个 Unreal Engine 项目";
                    }
                    else
                    {
                        if (EmptyStateTitle != null) EmptyStateTitle.Text = "未找到匹配的项目";
                        if (EmptyStateMessage != null) EmptyStateMessage.Text = "尝试调整搜索条件或筛选器来查找你的项目";
                    }
                }
                else
                {
                    EmptyStatePanel.Visibility = Visibility.Collapsed;
                    ProjectsListView.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                WriteDebug($"UpdateEmptyState失败: {ex.Message}");
            }
        }

        // 事件处理方法 - 都添加空值检查和异常处理
        private void ProjectSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            try
            {
                if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
                {
                    _currentSearchText = sender.Text ?? "";
                    ApplyFilters();
                }
            }
            catch (Exception ex)
            {
                WriteDebug($"ProjectSearchBox_TextChanged失败: {ex.Message}");
            }
        }

        private void EngineFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem item)
                {
                    _currentEngineFilter = item.Tag?.ToString() ?? "ALL_ENGINES";
                    ApplyFilters();
                }
            }
            catch (Exception ex)
            {
                WriteDebug($"EngineFilterComboBox_SelectionChanged失败: {ex.Message}");
            }
        }

        private void SortOrderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem item)
                {
                    _currentSortOrder = item.Tag?.ToString() ?? "LastUsed";
                    ApplyFilters();
                }
            }
            catch (Exception ex)
            {
                WriteDebug($"SortOrderComboBox_SelectionChanged失败: {ex.Message}");
            }
        }

        private void ClearFilters_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ProjectSearchBox != null) ProjectSearchBox.Text = "";
                _currentSearchText = "";

                if (EngineFilterComboBox != null) EngineFilterComboBox.SelectedIndex = 0;
                _currentEngineFilter = "ALL_ENGINES";

                if (SortOrderComboBox != null) SortOrderComboBox.SelectedIndex = 0;
                _currentSortOrder = "LastUsed";

                ApplyFilters();
            }
            catch (Exception ex)
            {
                WriteDebug($"ClearFilters_Click失败: {ex.Message}");
            }
        }

        private async void AddProject_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                WriteDebug("开始添加项目");

                if (StatusText != null) StatusText.Text = "正在选择项目文件...";

                var picker = new FileOpenPicker();
                picker.ViewMode = PickerViewMode.List;
                picker.SuggestedStartLocation = PickerLocationId.Desktop;
                picker.FileTypeFilter.Add(".uproject");

                var window = App.MainWindow;
                if (window != null)
                {
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                    WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                }

                var file = await picker.PickSingleFileAsync();
                if (file != null)
                {
                    await AddProjectFromPath(file.Path);
                    LoadEngineFilters();
                    ApplyFilters();
                }
                else
                {
                    if (StatusText != null) StatusText.Text = "未选择文件";
                }
            }
            catch (Exception ex)
            {
                WriteDebug($"AddProject_Click失败: {ex.Message}");
                if (StatusText != null) StatusText.Text = $"添加项目失败: {ex.Message}";
                await ShowErrorDialog($"添加项目失败: {ex.Message}");
            }
        }

        private async Task AddProjectFromPath(string projectPath)
        {
            try
            {
                WriteDebug($"添加项目: {projectPath}");

                if (!File.Exists(projectPath))
                {
                    throw new FileNotFoundException("项目文件不存在");
                }

                if (_allProjects.Any(p => p.ProjectPath == projectPath))
                {
                    if (StatusText != null) StatusText.Text = "项目已存在于列表中";
                    WriteDebug($"项目已存在于列表中: {projectPath}");
                    return;
                }

                var projectInfo = await CreateProjectInfo(projectPath);
                if (projectInfo != null)
                {
                    WriteDebug($"创建项目信息成功: {projectInfo.DisplayName}");
                    _allProjects.Add(projectInfo);
                    WriteDebug($"项目添加到列表，当前列表大小: {_allProjects.Count}");

                    await SaveProjects(); // 立即保存

                    if (StatusText != null) StatusText.Text = $"成功添加项目: {projectInfo.DisplayName}";
                    WriteDebug($"项目添加成功: {projectInfo.DisplayName}");
                }
                else
                {
                    WriteDebug($"创建项目信息失败: {projectPath}");
                }
            }
            catch (Exception ex)
            {
                WriteDebug($"AddProjectFromPath失败: {ex.Message}");
                WriteDebug($"堆栈跟踪: {ex.StackTrace}");
                if (StatusText != null) StatusText.Text = $"添加项目失败: {ex.Message}";
                throw;
            }
        }

        private async void RefreshProjects_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                WriteDebug("开始刷新项目");

                if (StatusText != null) StatusText.Text = "正在刷新项目列表...";

                var validProjects = new List<ProjectInfo>();
                foreach (var project in _allProjects.ToList())
                {
                    if (File.Exists(project.ProjectPath))
                    {
                        var refreshedProject = await CreateProjectInfo(project.ProjectPath);
                        if (refreshedProject != null)
                        {
                            // 保留原有的一些重要属性
                            refreshedProject.LastUsed = project.LastUsed;

                            // 如果新加载的项目没有引擎关联，但原项目有，则保留原有关联
                            if (refreshedProject.AssociatedEngine == null && project.AssociatedEngine != null)
                            {
                                refreshedProject.AssociatedEngine = project.AssociatedEngine;
                                refreshedProject.EngineAssociation = project.EngineAssociation;
                            }

                            validProjects.Add(refreshedProject);
                        }
                    }
                }

                _allProjects = validProjects;
                await SaveProjects(); // 改为异步保存
                LoadEngineFilters();
                ApplyFilters();

                if (StatusText != null) StatusText.Text = "项目列表已刷新";
                WriteDebug("项目刷新完成");
            }
            catch (Exception ex)
            {
                WriteDebug($"RefreshProjects_Click失败: {ex.Message}");
                if (StatusText != null) StatusText.Text = $"刷新失败: {ex.Message}";
            }
        }

        private void OpenProjectFolder_Click(object sender, RoutedEventArgs e)
        {
            if (StatusText != null) StatusText.Text = "请使用项目卡片上的'更多操作'按钮打开文件夹";
        }

        private async void StartWithIdeButton_OnClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var project = button?.Tag as ProjectInfo;
            if (project != null)
            {
                await OpenWithIDE(project);
            }
        }

        private async void AutoSearchProjects_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isSearching)
                {
                    await ShowErrorDialog("搜索正在进行中，请等待当前搜索完成。");
                    return;
                }

                WriteDebug("开始自动搜索项目");

                var folderPicker = new Windows.Storage.Pickers.FolderPicker();
                folderPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
                folderPicker.FileTypeFilter.Add("*");

                var window = App.MainWindow;
                if (window != null)
                {
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                    WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);
                }

                var folder = await folderPicker.PickSingleFolderAsync();
                if (folder != null)
                {
                    // 显示进度对话框
                    await ShowAutoSearchProgressDialog(folder.Path);
                }
            }
            catch (Exception ex)
            {
                WriteDebug($"AutoSearchProjects_Click失败: {ex.Message}");
                if (StatusText != null) StatusText.Text = $"自动搜索失败: {ex.Message}";
                await ShowErrorDialog($"自动搜索失败: {ex.Message}");
            }
        }

        private async void LaunchProject(ProjectInfo project)
        {
            try
            {
                WriteDebug($"启动项目: {project.DisplayName}");

                if (StatusText != null) StatusText.Text = $"正在启动项目: {project.DisplayName}";

                var startInfo = new ProcessStartInfo
                {
                    FileName = project.ProjectPath,
                    UseShellExecute = true
                };

                Process.Start(startInfo);

                project.LastUsed = DateTime.Now;
                await SaveProjects(); // 改为异步保存，保存最近使用时间

                if (StatusText != null) StatusText.Text = $"已启动项目: {project.DisplayName}";
                WriteDebug($"项目启动成功: {project.DisplayName}");
            }
            catch (Exception ex)
            {
                WriteDebug($"LaunchProject失败: {ex.Message}");
                if (StatusText != null) StatusText.Text = $"启动项目失败: {ex.Message}";
                await ShowErrorDialog($"启动项目失败: {ex.Message}");
            }
        }

        private void OpenProjectFolder(ProjectInfo project)
        {
            try
            {
                if (Directory.Exists(project.ProjectDirectory))
                {
                    Process.Start("explorer.exe", project.ProjectDirectory);
                    if (StatusText != null) StatusText.Text = "已打开项目文件夹";
                }
                else
                {
                    if (StatusText != null) StatusText.Text = "项目文件夹不存在";
                }
            }
            catch (Exception ex)
            {
                WriteDebug($"OpenProjectFolder失败: {ex.Message}");
                if (StatusText != null) StatusText.Text = $"打开文件夹失败: {ex.Message}";
            }
        }

        private async void RefreshProject(ProjectInfo project)
        {
            try
            {
                project.RefreshThumbnail();
            }
            catch (Exception ex)
            {
                WriteDebug($"RefreshProject失败: {ex.Message}");
                if (StatusText != null) StatusText.Text = $"刷新项目失败: {ex.Message}";
                await ShowErrorDialog($"刷新项目失败: {ex.Message}");
            }
        }

        private async void RemoveProject(ProjectInfo project)
        {
            try
            {
                var dialog = new ContentDialog
                {
                    Title = "确认移除",
                    Content = $"确定要从列表中移除项目 \"{project.DisplayName}\" 吗？\n\n注意：这不会删除项目文件，只是从启动器中移除。",
                    PrimaryButtonText = "移除",
                    CloseButtonText = "取消",
                    XamlRoot = this.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    _allProjects.Remove(project);
                    await SaveProjects(); // 改为异步保存
                    LoadEngineFilters();
                    ApplyFilters();
                    if (StatusText != null) StatusText.Text = $"已移除项目: {project.DisplayName}";
                    WriteDebug($"项目移除成功: {project.DisplayName}");
                }
            }
            catch (Exception ex)
            {
                WriteDebug($"RemoveProject失败: {ex.Message}");
                if (StatusText != null) StatusText.Text = $"移除项目失败: {ex.Message}";
                await ShowErrorDialog($"移除项目失败: {ex.Message}");
            }
        }

        private async Task SaveProjects()
        {
            try
            {
                WriteDebug($"开始保存项目，共 {_allProjects.Count} 个");

                // 输出要保存的项目详细信息用于调试
                foreach (var project in _allProjects)
                {
                    WriteDebug($"要保存的项目: {project.DisplayName} - {project.ProjectPath}");
                }

                var success = await ProjectDataService.Instance.SaveProjectsAsync(_allProjects);

                if (success)
                {
                    WriteDebug("项目保存成功");

                    // 立即验证保存是否成功
                    try
                    {
                        WriteDebug("验证保存的数据...");
                        var verifyProjects = await ProjectDataService.Instance.LoadProjectsAsync();
                        WriteDebug($"验证结果: 保存了 {_allProjects.Count} 个项目，验证读取到 {verifyProjects.Count} 个项目");

                        foreach (var project in verifyProjects)
                        {
                            WriteDebug($"验证读取的项目: {project.DisplayName} - {project.ProjectPath}");
                        }
                    }
                    catch (Exception verifyEx)
                    {
                        WriteDebug($"验证保存失败: {verifyEx.Message}");
                    }
                }
                else
                {
                    WriteDebug("项目保存失败");
                    if (StatusText != null) StatusText.Text = "保存项目失败";

                    // 如果新服务保存失败，回退到旧方式
                    WriteDebug("回退到旧的保存方式");
                    await SaveProjectsOldFormat();
                }
            }
            catch (Exception ex)
            {
                WriteDebug($"SaveProjects失败: {ex.Message}");
                WriteDebug($"堆栈跟踪: {ex.StackTrace}");

                // 回退到旧的保存方式
                WriteDebug("回退到旧的保存方式");
                await SaveProjectsOldFormat();

                if (StatusText != null) StatusText.Text = $"保存项目失败: {ex.Message}";
            }
        }

        // 添加旧格式保存方法作为备用
        private async Task SaveProjectsOldFormat()
        {
            try
            {
                WriteDebug("使用旧格式保存项目");
                var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                var projectPaths = _allProjects.Select(p => p.ProjectPath).ToArray();
                var dataToSave = string.Join("|", projectPaths);
                localSettings.Values["ProjectsData"] = dataToSave;
                WriteDebug($"旧格式项目保存成功: {dataToSave}");
            }
            catch (Exception ex)
            {
                WriteDebug($"旧格式保存也失败: {ex.Message}");
                throw;
            }
        }

        private async Task ShowErrorDialog(string message)
        {
            try
            {
                var dialog = new ContentDialog
                {
                    Title = "错误",
                    Content = message,
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };

                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                WriteDebug($"ShowErrorDialog失败: {ex.Message}");
            }
        }

        private void StartProjectButton_OnClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var project = button?.Tag as ProjectInfo;
            if (project != null)
            {
                LaunchProject(project);
            }
        }

        private void MoreCommandsButton_OnClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var project = button?.Tag as ProjectInfo;
            if (project != null)
            {
                ShowProjectMenu(project, button);
            }
        }

        private async void GenerateVisualStudioProjectFiles(ProjectInfo project)
        {
            try
            {
                // 创建进度对话框
                CreateProgressDialog();

                // 显示进度对话框
                var dialogTask = _progressDialog.ShowAsync();

                // 添加初始消息
                AppendProgressOutput($"正在为项目 {project.DisplayName} 生成 Visual Studio 项目文件...\n");
                AppendProgressOutput($"项目路径: {project.ProjectPath}\n");
                AppendProgressOutput(new string('-', 50) + "\n");

                // 查找关联的引擎
                UnrealEngineInfo engine = null;
                if (project.AssociatedEngine != null && project.AssociatedEngine.IsValid)
                {
                    engine = project.AssociatedEngine;
                    AppendProgressOutput($"使用项目关联的引擎: {engine.DisplayName} ({engine.Version})\n");
                }
                else if (!string.IsNullOrEmpty(project.EngineAssociation))
                {
                    AppendProgressOutput($"项目关联标识: {project.EngineAssociation}\n");

                    // 尝试从引擎管理器获取引擎信息
                    var engineManager = EngineManagerService.Instance;
                    await engineManager.LoadEngines(); // 确保引擎已加载
                    var engines = engineManager.GetValidEngines();

                    if (engines != null)
                    {
                        engine = engines.FirstOrDefault(e =>
                            e.Version == project.EngineAssociation ||
                            e.FullVersion == project.EngineAssociation ||
                            (e.BuildVersionInfo?.BranchName?.Contains(project.EngineAssociation) ?? false));

                        if (engine != null)
                        {
                            AppendProgressOutput($"找到匹配的引擎: {engine.DisplayName} ({engine.Version})\n");
                        }
                        else
                        {
                            // 如果没找到精确匹配，尝试使用第一个有效的引擎
                            engine = engines.FirstOrDefault();
                            if (engine != null)
                            {
                                AppendProgressOutput($"未找到精确匹配的引擎，使用默认引擎: {engine.DisplayName} ({engine.Version})\n");
                            }
                        }
                    }
                }

                // 如果仍未找到引擎，尝试使用任何有效的引擎
                if (engine == null)
                {
                    var engineManager = EngineManagerService.Instance;
                    await engineManager.LoadEngines();
                    var engines = engineManager.GetValidEngines();
                    engine = engines?.FirstOrDefault();

                    if (engine != null)
                    {
                        AppendProgressOutput($"使用系统中的第一个有效引擎: {engine.DisplayName} ({engine.Version})\n");
                    }
                }

                if (engine == null)
                {
                    throw new Exception("未找到有效的 Unreal Engine 安装。请确保至少安装了一个有效的 Unreal Engine 版本。");
                }

                AppendProgressOutput($"引擎路径: {engine.EnginePath}\n\n");

                var unrealBuildToolPath = Path.Combine(engine.EnginePath, "Engine", "Binaries", "DotNET", "UnrealBuildTool.exe");
                if (!File.Exists(unrealBuildToolPath))
                {
                    unrealBuildToolPath = Path.Combine(engine.EnginePath, "Engine", "Binaries", "DotNET", "UnrealBuildTool", "UnrealBuildTool.exe");
                }

                if (!File.Exists(unrealBuildToolPath))
                {
                    throw new Exception($"未找到 UnrealBuildTool.exe: {unrealBuildToolPath}\n请检查引擎安装是否完整。");
                }

                // 构建命令行参数
                var arguments = $"-projectfiles -project=\"{project.ProjectPath}\" -game -rocket -progress";

                AppendProgressOutput($"执行命令: {unrealBuildToolPath} {arguments}\n");
                AppendProgressOutput(new string('=', 60) + "\n");

                // 创建进程启动信息
                var startInfo = new ProcessStartInfo
                {
                    FileName = unrealBuildToolPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = engine.EnginePath
                };

                // 启动进程
                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    throw new Exception("无法启动 UnrealBuildTool 进程");
                }

                // 异步读取输出
                var outputTask = ReadProcessOutput(process);

                // 等待进程完成
                await process.WaitForExitAsync();

                // 等待输出读取完成
                await outputTask;

                if (process.ExitCode == 0)
                {
                    AppendProgressOutput(new string('=', 60) + "\n");
                    AppendProgressOutput($"成功为项目 {project.DisplayName} 生成 Visual Studio 项目文件\n");

                    // 等待几秒后关闭对话框
                    await Task.Delay(2000);
                    _progressDialog?.Hide();
                }
                else
                {
                    AppendProgressOutput(new string('=', 60) + "\n");
                    AppendProgressOutput($"生成 Visual Studio 项目文件失败 (退出代码: {process.ExitCode})\n");
                    AppendProgressOutput("请检查上面的输出信息以了解失败原因。\n");

                    // 保持对话框打开，让用户看到错误信息
                    // 用户需要手动关闭
                }
            }
            catch (Exception ex)
            {
                WriteDebug($"GenerateVisualStudioProjectFiles 失败: {ex.Message}");
                AppendProgressOutput($"\n错误: {ex.Message}\n");
                AppendProgressOutput("\n点击\"关闭\"按钮关闭此窗口\n");
                // 不隐藏对话框，让用户看到错误信息
            }
        }

        private void GridLayout_Click(object sender, RoutedEventArgs e) => SetLayoutMode(ProjectCardLayoutMode.Grid);
        private void ListLayout_Click(object sender, RoutedEventArgs e) => SetLayoutMode(ProjectCardLayoutMode.List);
        private void CompactLayout_Click(object sender, RoutedEventArgs e) => SetLayoutMode(ProjectCardLayoutMode.Compact);

        private void SetLayoutMode(ProjectCardLayoutMode mode)
        {
            if (_currentLayoutMode == mode) return;
            _currentLayoutMode = mode;
            SaveLayoutModeToSettings(mode);
            UpdateLayoutMode();
        }

        private T TryGetResource<T>(string key) where T : class
        {
            try
            {
                if (this.Resources != null && this.Resources.TryGetValue(key, out var obj))
                {
                    return obj as T;
                }
            }
            catch
            {
                // 忽略错误
            }

            return null;
        }

        private void UpdateLayoutMode()
        {
            if (ProjectsListView == null) return;

            switch (_currentLayoutMode)
            {
                case ProjectCardLayoutMode.Grid:
                    ProjectsListView.ItemsPanel = TryGetResource<ItemsPanelTemplate>("GridItemsPanel");
                    ProjectsListView.ItemTemplate = TryGetResource<DataTemplate>("GridProjectTemplate");
                    break;

                case ProjectCardLayoutMode.List:
                    ProjectsListView.ItemsPanel = TryGetResource<ItemsPanelTemplate>("ListItemsPanel");
                    ProjectsListView.ItemTemplate = TryGetResource<DataTemplate>("ListProjectTemplate");
                    break;

                case ProjectCardLayoutMode.Compact:
                    ProjectsListView.ItemsPanel = TryGetResource<ItemsPanelTemplate>("CompactItemsPanel");
                    ProjectsListView.ItemTemplate = TryGetResource<DataTemplate>("CompactProjectTemplate");
                    break;
            }

            // 同步 Segmented 选中项，但避免回调再次触发
            _suppressSegmentedEvent = true;
            if (LayoutSegmented != null)
            {
                LayoutSegmented.SelectedIndex = _currentLayoutMode switch
                {
                    ProjectCardLayoutMode.List => 1,
                    ProjectCardLayoutMode.Compact => 2,
                    _ => 0
                };
            }

            _suppressSegmentedEvent = false;
        }

        private void CreateProgressDialog()
        {
            // 创建输出文本框
            _progressOutputTextBox = new TextBox
            {
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                Height = 400,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
            };

            // 创建对话框
            _progressDialog = new ContentDialog
            {
                Title = "生成 Visual Studio 项目文件",
                Content = _progressOutputTextBox,
                CloseButtonText = "关闭",
                XamlRoot = this.XamlRoot,
                PrimaryButtonStyle = Application.Current.Resources["AccentButtonStyle"] as Style
            };
        }

        private void AppendProgressOutput(string text)
        {
            if (_progressOutputTextBox != null)
            {
                // 在UI线程上更新文本
                _progressOutputTextBox.DispatcherQueue?.TryEnqueue(() =>
                {
                    _progressOutputTextBox.Text += text;
                    // 自动滚动到底部
                    _progressOutputTextBox.SelectionStart = _progressOutputTextBox.Text.Length;
                });
            }
        }

        private async Task ReadProcessOutput(Process process)
        {
            try
            {
                // 同时读取标准输出和错误输出
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                // 等待读取完成
                var output = await outputTask;
                var error = await errorTask;

                // 显示输出
                if (!string.IsNullOrEmpty(output))
                {
                    var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        AppendProgressOutput(line + "\n");
                        await Task.Delay(10); // 小延迟以确保UI更新
                    }
                }

                // 显示错误输出
                if (!string.IsNullOrEmpty(error))
                {
                    AppendProgressOutput("\n--- 错误输出 ---\n");
                    var lines = error.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        AppendProgressOutput(line + "\n");
                        await Task.Delay(10); // 小延迟以确保UI更新
                    }
                }
            }
            catch (Exception ex)
            {
                AppendProgressOutput($"\n读取输出时出错: {ex.Message}\n");
            }
        }

        private async void SwitchEngineVersion(ProjectInfo project)
        {
            try
            {
                // 加载引擎列表
                var engineManager = EngineManagerService.Instance;
                await engineManager.LoadEngines();
                var validEngines = engineManager.GetValidEngines();

                if (validEngines == null || !validEngines.Any())
                {
                    await ShowErrorDialog("未找到有效的引擎安装。请先安装至少一个 Unreal Engine 版本。");
                    return;
                }

                // 创建引擎选择对话框
                var engineSelectionDialog = new ContentDialog
                {
                    Title = $"切换 {project.DisplayName} 的引擎版本",
                    Content = CreateEngineSelectionContent(project, validEngines),
                    PrimaryButtonText = "下一步",
                    CloseButtonText = "取消",
                    XamlRoot = this.XamlRoot
                };

                var result = await engineSelectionDialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    // 显示确认对话框
                    await ShowEngineSwitchConfirmation(project, validEngines);
                }
            }
            catch (Exception ex)
            {
                WriteDebug($"SwitchEngineVersion 失败: {ex.Message}");
                await ShowErrorDialog($"切换引擎版本失败: {ex.Message}");
            }
        }

        private async Task ShowEngineSwitchConfirmation(ProjectInfo project, IEnumerable<UnrealEngineInfo> validEngines)
        {
            try
            {
                if (_engineSelectionComboBox?.SelectedItem == null)
                {
                    WriteDebug("未选择引擎版本");
                    return;
                }

                var selectedItem = _engineSelectionComboBox.SelectedItem as ComboBoxItem;
                var selectedEngine = selectedItem?.Tag as UnrealEngineInfo;

                if (selectedEngine == null)
                {
                    WriteDebug("选择的引擎无效");
                    return;
                }

                // 检查是否选择了不同的引擎
                if (project.AssociatedEngine?.EnginePath == selectedEngine.EnginePath)
                {
                    if (StatusText != null)
                        StatusText.Text = "项目已使用选定的引擎版本";
                    return;
                }

                // 创建确认对话框
                var confirmationDialog = new ContentDialog
                {
                    Title = "⚠️ 重要警告：引擎版本切换确认",
                    Content = CreateConfirmationContent(project, selectedEngine),
                    PrimaryButtonText = "我理解风险，继续切换",
                    CloseButtonText = "取消",
                    XamlRoot = this.XamlRoot,
                    PrimaryButtonStyle = Application.Current.Resources["AccentButtonStyle"] as Style // 红色按钮样式
                };

                var result = await confirmationDialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    // 显示二次确认对话框（输入项目名称）
                    await ShowFinalConfirmation(project, selectedEngine, validEngines);
                }
            }
            catch (Exception ex)
            {
                WriteDebug($"ShowEngineSwitchConfirmation 失败: {ex.Message}");
                await ShowErrorDialog($"显示确认对话框失败: {ex.Message}");
            }
        }

        private ComboBox _engineSelectionComboBox;

        private UIElement CreateEngineSelectionContent(ProjectInfo project, IEnumerable<UnrealEngineInfo> validEngines)
        {
            var stackPanel = new StackPanel
            {
                Spacing = 10
            };

            var infoText = new TextBlock
            {
                Text = $"当前引擎: {project.GetEngineDisplayName()}\n请选择新的引擎版本:",
                TextWrapping = TextWrapping.Wrap
            };
            stackPanel.Children.Add(infoText);

            // 创建引擎选择下拉框
            _engineSelectionComboBox = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 10, 0, 0)
            };

            // 添加引擎选项
            int selectedIndex = -1;
            int index = 0;
            foreach (var engine in validEngines)
            {
                var displayText = $"{engine.DisplayName}";
                if (!string.IsNullOrEmpty(engine.FullVersion))
                    displayText += $" ({engine.FullVersion})";
                else if (!string.IsNullOrEmpty(engine.Version))
                    displayText += $" ({engine.Version})";

                var item = new ComboBoxItem
                {
                    Content = displayText,
                    Tag = engine
                };
                _engineSelectionComboBox.Items.Add(item);

                // 如果这是项目当前关联的引擎，设置为选中项
                if (project.AssociatedEngine?.EnginePath == engine.EnginePath)
                {
                    selectedIndex = index;
                }

                index++;
            }

            // 如果没有找到匹配的引擎，但项目有引擎关联标识，则尝试匹配
            if (selectedIndex == -1 && !string.IsNullOrEmpty(project.EngineAssociation))
            {
                index = 0;
                foreach (var engine in validEngines)
                {
                    if (engine.Version == project.EngineAssociation ||
                        engine.FullVersion == project.EngineAssociation ||
                        (engine.BuildVersionInfo?.BranchName?.Contains(project.EngineAssociation) ?? false))
                    {
                        selectedIndex = index;
                        break;
                    }

                    index++;
                }
            }

            // 设置默认选中项
            if (selectedIndex >= 0)
            {
                _engineSelectionComboBox.SelectedIndex = selectedIndex;
            }
            else if (_engineSelectionComboBox.Items.Count > 0)
            {
                _engineSelectionComboBox.SelectedIndex = 0;
            }

            stackPanel.Children.Add(_engineSelectionComboBox);

            var warningText = new TextBlock
            {
                Text = "注意：切换引擎版本后，您可能需要重新生成项目文件。",
                TextWrapping = TextWrapping.Wrap,
                FontStyle = Windows.UI.Text.FontStyle.Italic,
                Foreground = new SolidColorBrush(Colors.Orange)
            };
            stackPanel.Children.Add(warningText);

            return stackPanel;
        }

        private UIElement CreateConfirmationContent(ProjectInfo project, UnrealEngineInfo newEngine)
        {
            var stackPanel = new StackPanel
            {
                Spacing = 15
            };

            var warningIcon = new FontIcon
            {
                Glyph = "\uE7BA", // 警告图标
                Foreground = new SolidColorBrush(Colors.OrangeRed),
                FontSize = 24,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stackPanel.Children.Add(warningIcon);

            var titleText = new TextBlock
            {
                Text = "您即将更改项目引擎版本",
                FontSize = 16,
                TextAlignment = TextAlignment.Center
            };
            stackPanel.Children.Add(titleText);

            var infoText = new TextBlock
            {
                Text = $"项目: {project.DisplayName}\n" +
                       $"当前引擎: {project.GetEngineDisplayName()}\n" +
                       $"新引擎: {newEngine.DisplayName} ({newEngine.Version})",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0)
            };
            stackPanel.Children.Add(infoText);

            var warningMessages = new List<string>
            {
                "• 此操作将直接修改项目文件",
                "• 项目可能需要重新编译才能在新引擎版本中正常工作",
                "• 某些插件或代码可能与新引擎版本不兼容",
                "• 建议在切换前备份项目"
            };

            var warningList = new ItemsControl();
            foreach (var message in warningMessages)
            {
                var item = new TextBlock
                {
                    Text = message,
                    Foreground = new SolidColorBrush(Colors.OrangeRed),
                    TextWrapping = TextWrapping.Wrap
                };
                warningList.Items.Add(item);
            }

            stackPanel.Children.Add(warningList);

            var confirmText = new TextBlock
            {
                Text = "点击下方红色按钮确认您已了解风险并希望继续。",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0)
            };
            stackPanel.Children.Add(confirmText);

            return stackPanel;
        }

        private TextBox _projectNameConfirmationTextBox;

        private async Task ShowFinalConfirmation(ProjectInfo project, UnrealEngineInfo newEngine, IEnumerable<UnrealEngineInfo> validEngines)
        {
            try
            {
                var finalConfirmationDialog = new ContentDialog
                {
                    Title = "最终确认",
                    Content = CreateFinalConfirmationContent(project),
                    PrimaryButtonText = "确认切换引擎版本",
                    CloseButtonText = "取消",
                    XamlRoot = this.XamlRoot,
                    PrimaryButtonStyle = Application.Current.Resources["AccentButtonStyle"] as Style // 红色按钮样式
                };

                var result = await finalConfirmationDialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    // 验证项目名称输入
                    if (_projectNameConfirmationTextBox?.Text != project.DisplayName)
                    {
                        await ShowErrorDialog("项目名称不匹配，请输入正确的项目名称。");
                        return;
                    }

                    // 应用引擎切换
                    await ApplyEngineSwitch(project, newEngine, validEngines);
                }
            }
            catch (Exception ex)
            {
                WriteDebug($"ShowFinalConfirmation 失败: {ex.Message}");
                await ShowErrorDialog($"显示最终确认对话框失败: {ex.Message}");
            }
        }

        private UIElement CreateFinalConfirmationContent(ProjectInfo project)
        {
            var stackPanel = new StackPanel
            {
                Spacing = 15
            };

            var warningIcon = new FontIcon
            {
                Glyph = "\uE783", // 错误/停止图标
                Foreground = new SolidColorBrush(Colors.Red),
                FontSize = 24,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stackPanel.Children.Add(warningIcon);

            var titleText = new TextBlock
            {
                Text = "最后一次确认",
                FontSize = 16,
                TextAlignment = TextAlignment.Center
            };
            stackPanel.Children.Add(titleText);

            var instructionText = new TextBlock
            {
                Text = "为确保这是您的真实意图，请在下方输入框中输入项目名称以确认操作：",
                TextWrapping = TextWrapping.Wrap
            };
            stackPanel.Children.Add(instructionText);

            _projectNameConfirmationTextBox = new TextBox
            {
                PlaceholderText = "请输入项目名称",
                Margin = new Thickness(0, 10, 0, 0)
            };
            stackPanel.Children.Add(_projectNameConfirmationTextBox);

            var projectNameText = new TextBlock
            {
                Text = $"项目名称: {project.DisplayName}",
                FontStyle = Windows.UI.Text.FontStyle.Italic,
                Foreground = new SolidColorBrush(Colors.Gray),
                Margin = new Thickness(0, 5, 0, 0)
            };
            stackPanel.Children.Add(projectNameText);

            var warningText = new TextBlock
            {
                Text = "⚠️ 注意：此操作不可逆，将直接修改项目文件。",
                Foreground = new SolidColorBrush(Colors.Red),
                TextWrapping = TextWrapping.Wrap
            };
            stackPanel.Children.Add(warningText);

            return stackPanel;
        }

        private async Task ApplyEngineSwitch(ProjectInfo project, UnrealEngineInfo newEngine, IEnumerable<UnrealEngineInfo> validEngines)
        {
            try
            {
                // 更新项目引擎关联
                project.AssociatedEngine = newEngine;
                project.EngineAssociation = newEngine.Version;

                // 更新项目文件中的引擎关联
                await UpdateProjectEngineAssociation(project, newEngine);

                // 保存项目列表
                await SaveProjects();

                // 刷新UI
                ApplyFilters();

                if (StatusText != null)
                    StatusText.Text = $"已将 {project.DisplayName} 的引擎版本切换为 {newEngine.DisplayName}";

                WriteDebug($"成功将项目 {project.DisplayName} 的引擎切换为 {newEngine.DisplayName}");

                // 显示成功消息
                var successDialog = new ContentDialog
                {
                    Title = "成功",
                    Content = $"项目 {project.DisplayName} 的引擎版本已成功切换为 {newEngine.DisplayName}。\n\n建议您重新生成项目文件以确保兼容性。",
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await successDialog.ShowAsync();
            }
            catch (Exception ex)
            {
                WriteDebug($"ApplyEngineSwitch 失败: {ex.Message}");
                throw new Exception($"应用引擎切换失败: {ex.Message}");
            }
        }

        private async Task ApplyEngineSwitch(ProjectInfo project, IEnumerable<UnrealEngineInfo> validEngines)
        {
            try
            {
                if (_engineSelectionComboBox?.SelectedItem == null)
                {
                    WriteDebug("未选择引擎版本");
                    return;
                }

                var selectedItem = _engineSelectionComboBox.SelectedItem as ComboBoxItem;
                var selectedEngine = selectedItem?.Tag as UnrealEngineInfo;

                if (selectedEngine == null)
                {
                    WriteDebug("选择的引擎无效");
                    return;
                }

                // 检查是否选择了不同的引擎
                if (project.AssociatedEngine?.EnginePath == selectedEngine.EnginePath)
                {
                    if (StatusText != null)
                        StatusText.Text = "项目已使用选定的引擎版本";
                    return;
                }

                // 更新项目引擎关联
                project.AssociatedEngine = selectedEngine;
                project.EngineAssociation = selectedEngine.Version;

                // 更新项目文件中的引擎关联
                await UpdateProjectEngineAssociation(project, selectedEngine);

                // 保存项目列表
                await SaveProjects();

                // 刷新UI
                ApplyFilters();

                if (StatusText != null)
                    StatusText.Text = $"已将 {project.DisplayName} 的引擎版本切换为 {selectedEngine.DisplayName}";

                WriteDebug($"成功将项目 {project.DisplayName} 的引擎切换为 {selectedEngine.DisplayName}");
            }
            catch (Exception ex)
            {
                WriteDebug($"ApplyEngineSwitch 失败: {ex.Message}");
                throw new Exception($"应用引擎切换失败: {ex.Message}");
            }
        }

        private async Task UpdateProjectEngineAssociation(ProjectInfo project, UnrealEngineInfo newEngine)
        {
            try
            {
                if (!File.Exists(project.ProjectPath))
                {
                    throw new FileNotFoundException($"项目文件不存在: {project.ProjectPath}");
                }

                // 读取项目文件内容
                var content = await File.ReadAllTextAsync(project.ProjectPath);

                // 解析JSON
                using var jsonDoc = System.Text.Json.JsonDocument.Parse(content);
                var root = jsonDoc.RootElement;

                // 创建新的JSON对象
                var jsonObject = new Dictionary<string, object>();

                // 复制所有现有属性
                foreach (var property in root.EnumerateObject())
                {
                    switch (property.Value.ValueKind)
                    {
                        case System.Text.Json.JsonValueKind.String:
                            jsonObject[property.Name] = property.Value.GetString();
                            break;
                        case System.Text.Json.JsonValueKind.Number:
                            jsonObject[property.Name] = property.Value.GetDouble();
                            break;
                        case System.Text.Json.JsonValueKind.True:
                        case System.Text.Json.JsonValueKind.False:
                            jsonObject[property.Name] = property.Value.GetBoolean();
                            break;
                        case System.Text.Json.JsonValueKind.Array:
                            jsonObject[property.Name] = property.Value.EnumerateArray().ToArray();
                            break;
                        case System.Text.Json.JsonValueKind.Object:
                            jsonObject[property.Name] = property.Value;
                            break;
                        default:
                            jsonObject[property.Name] = property.Value.GetString();
                            break;
                    }
                }

                // 更新引擎关联
                jsonObject["EngineAssociation"] = newEngine.Version;

                // 转换回JSON字符串
                var options = new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                };
                var newContent = System.Text.Json.JsonSerializer.Serialize(jsonObject, options);

                // 写入文件
                await File.WriteAllTextAsync(project.ProjectPath, newContent);

                WriteDebug($"成功更新项目 {project.DisplayName} 的引擎关联为 {newEngine.Version}");
            }
            catch (Exception ex)
            {
                WriteDebug($"UpdateProjectEngineAssociation 失败: {ex.Message}");
                throw new Exception($"更新项目文件失败: {ex.Message}");
            }
        }

        private async Task OpenWithIDE(ProjectInfo project)
        {
            try
            {
                // 获取默认 IDE 设置
                var defaultIde = Settings.Get("Default.IDE", "VS");
                var idePathKey = $"IDE.Path.{defaultIde}";
                var idePath = Settings.Get(idePathKey, "");

                if (string.IsNullOrEmpty(idePath) || !File.Exists(idePath))
                {
                    await ShowErrorDialog("IDE 路径未设置或无效，请在设置页面配置 IDE 路径后再试。");
                    return;
                }

                // 查找项目对应的 .sln 文件
                var projectDir = Path.GetDirectoryName(project.ProjectPath);
                var projectName = Path.GetFileNameWithoutExtension(project.ProjectPath);
                var slnPath = Path.Combine(projectDir, $"{projectName}.sln");

                // 检查 .sln 文件是否存在
                if (!File.Exists(slnPath))
                {
                    // 尝试查找任何 .sln 文件
                    var slnFiles = Directory.GetFiles(projectDir, "*.sln", SearchOption.TopDirectoryOnly);
                    if (slnFiles.Length > 0)
                    {
                        slnPath = slnFiles[0];
                    }
                    else
                    {
                        await ShowErrorDialog("未找到解决方案文件，在项目目录中未找到 .sln 文件。请先生成项目文件。");
                        return;
                    }
                }

                ProcessStartInfo startInfo = defaultIde switch
                {
                    "VS" => new ProcessStartInfo
                    {
                        FileName = idePath,
                        Arguments = $"\"{slnPath}\"",
                        UseShellExecute = true
                    },
                    "RD" => new ProcessStartInfo
                    {
                        FileName = idePath,
                        Arguments = $"\"{slnPath}\"",
                        UseShellExecute = true
                    },
                    "VSCode" => new ProcessStartInfo
                    {
                        FileName = idePath,
                        Arguments = $"\"{projectDir}\"", // VS Code 通常打开目录而非 .sln 文件
                        UseShellExecute = true
                    },
                    _ => new ProcessStartInfo
                    {
                        FileName = idePath,
                        Arguments = $"\"{slnPath}\"",
                        UseShellExecute = true
                    }
                };

                Process.Start(startInfo);

                var ideName = defaultIde switch
                {
                    "VS" => "Visual Studio",
                    "RD" => "Rider",
                    "VSCode" => "Visual Studio Code",
                    _ => "IDE"
                };

                if (StatusText != null)
                    StatusText.Text = $"已在 {ideName} 中打开: {Path.GetFileName(slnPath)}";
            }
            catch (Exception ex)
            {
                WriteDebug($"OpenWithIDE 失败: {ex.Message}");
                await ShowErrorDialog($"打开 IDE 失败: {ex.Message}");
            }
        }

        private async Task ShowAutoSearchProgressDialog(string searchPath)
        {
            _isSearching = true;
            _searchCancellationTokenSource = new CancellationTokenSource();

            try
            {
                _searchProgressDialog = new ContentDialog
                {
                    Title = "自动搜索项目",
                    Content = CreateAutoSearchDialogContent(),
                    CloseButtonText = "取消",
                    XamlRoot = this.XamlRoot
                };

                // 设置关闭按钮事件
                _searchProgressDialog.Closing += async (s, args) =>
                {
                    if (_isSearching)
                    {
                        args.Cancel = true; // 阻止对话框关闭
                        var confirmDialog = new ContentDialog
                        {
                            Title = "确认取消",
                            Content = "确定要取消搜索吗？",
                            PrimaryButtonText = "是",
                            CloseButtonText = "否",
                            XamlRoot = this.XamlRoot
                        };

                        var result = await confirmDialog.ShowAsync();
                        if (result == ContentDialogResult.Primary)
                        {
                            _searchCancellationTokenSource.Cancel();
                            _isSearching = false;
                            // 不需要手动关闭对话框，系统会处理
                        }
                    }
                    // 如果不在搜索中，允许对话框正常关闭
                };

                // 显示对话框
                var dialogTask = _searchProgressDialog.ShowAsync();

                // 开始搜索
                var searchReport = await PerformAutoSearch(searchPath, _searchCancellationTokenSource.Token);

                // 搜索完成后，确保对话框关闭
                _isSearching = false;

                try
                {
                    // 尝试隐藏对话框
                    _searchProgressDialog?.Hide();
                }
                catch
                {
                    // 忽略可能发生的异常
                }

                // 等待对话框任务完成
                try
                {
                    await dialogTask;
                }
                catch
                {
                    // 忽略对话框关闭时可能发生的异常
                }

                // 显示搜索报告
                await ShowSearchReport(searchReport);

                // 应用过滤器以显示新添加的项目
                LoadEngineFilters();
                ApplyFilters();
            }
            catch (OperationCanceledException)
            {
                WriteDebug("搜索已被用户取消");
                if (StatusText != null) StatusText.Text = "搜索已取消";
            }
            catch (Exception ex)
            {
                WriteDebug($"ShowAutoSearchProgressDialog失败: {ex.Message}");
                if (StatusText != null) StatusText.Text = $"搜索失败: {ex.Message}";

                // 确保进度对话框已关闭后再显示错误对话框
                await CloseSearchProgressDialog();
                await ShowErrorDialog($"搜索失败: {ex.Message}");
            }
            finally
            {
                _isSearching = false;
                _searchCancellationTokenSource?.Dispose();
                _searchCancellationTokenSource = null;
                _searchProgressDialog = null;
            }
        }

        private async Task ShowSearchReport(SearchReport report)
        {
            var scrollViewer = new ScrollViewer
            {
                MaxHeight = 500,
                Padding = new Thickness(10)
            };

            var stackPanel = new StackPanel
            {
                Spacing = 10
            };

            var title = new TextBlock
            {
                Text = "自动搜索完成",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            };
            stackPanel.Children.Add(title);

            if (report.Cancelled)
            {
                var card = CreateStatusCard("搜索已被用户取消", "\uE783", Colors.Orange); // 警告图标
                stackPanel.Children.Add(card);
            }
            else if (!string.IsNullOrEmpty(report.Error))
            {
                var card = CreateStatusCard($"搜索过程中发生错误: {report.Error}", "\uE783", Colors.Red); // 错误图标
                stackPanel.Children.Add(card);
            }
            else
            {
                // 显示搜索统计信息
                var duration = report.EndTime - report.StartTime;
                var statsText = $"搜索路径: {report.SearchPath}\n" +
                                $"搜索耗时: {duration:mm\\:ss\\.fff}\n" +
                                $"扫描目录数: {report.ProcessedDirectories}\n" +
                                $"无法访问目录数: {report.InaccessibleDirectories}\n" +
                                $"处理失败目录数: {report.FailedDirectories}\n" +
                                $"找到项目文件数: {report.FoundProjects}\n" +
                                $"成功添加项目数: {report.AddedProjects}\n" +
                                $"跳过项目数: {report.SkippedProjects}\n" +
                                $"添加失败项目数: {report.FailedProjects}";

                var statsCard = CreateStatusCard(statsText, "\uE9D2", Colors.Blue); // 统计图标
                stackPanel.Children.Add(statsCard);

                // 如果有添加或跳过的项目，显示详细信息
                if (report.AddedProjects > 0 || report.SkippedProjects > 0 || report.FailedProjects > 0)
                {
                    var detailsTitle = new TextBlock
                    {
                        Text = "详细信息",
                        FontSize = 16,
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, 10, 0, 5)
                    };
                    stackPanel.Children.Add(detailsTitle);
                }
            }

            scrollViewer.Content = stackPanel;

            var dialog = new ContentDialog
            {
                Title = "搜索报告",
                Content = scrollViewer,
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot,
                Width = 600
            };

            await dialog.ShowAsync();
        }

        private async Task CloseSearchProgressDialog()
        {
            if (_searchProgressDialog != null)
            {
                try
                {
                    _searchProgressDialog.Hide();
                }
                catch
                {
                    // 忽略可能发生的异常
                }
            }
        }

        private async Task<SearchReport> PerformAutoSearch(string searchPath, CancellationToken cancellationToken)
        {
            var report = new SearchReport();
            report.SearchPath = searchPath;

            try
            {
                AppendSearchProgress($"开始在 {searchPath} 中搜索项目...\n");
                AppendSearchProgress("正在扫描目录，请稍候...\n\n");

                var projectFiles = await SearchForProjectFiles(searchPath, cancellationToken, report);

                AppendSearchProgress($"\n找到 {projectFiles.Count} 个项目文件\n");
                AppendSearchProgress("正在处理项目...\n\n");

                int processedCount = 0;
                foreach (var projectFile in projectFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        AppendSearchProgress($"处理: {Path.GetFileName(projectFile)}\n");

                        if (_allProjects.Any(p => p.ProjectPath.Equals(projectFile, StringComparison.OrdinalIgnoreCase)))
                        {
                            AppendSearchProgress("  -> 项目已存在，跳过\n");
                            report.SkippedProjects++;
                            continue;
                        }

                        var projectInfo = await CreateProjectInfo(projectFile);
                        if (projectInfo != null)
                        {
                            _allProjects.Add(projectInfo);
                            report.AddedProjects++;
                            AppendSearchProgress("  -> 添加成功\n");
                        }
                        else
                        {
                            report.FailedProjects++;
                            AppendSearchProgress("  -> 添加失败\n");
                        }

                        processedCount++;
                        // 每处理10个项目更新一次UI，避免过于频繁的UI更新
                        if (processedCount % 10 == 0)
                        {
                            await Task.Delay(1); // 让出UI线程，防止界面卡顿
                        }
                    }
                    catch (Exception ex)
                    {
                        report.FailedProjects++;
                        AppendSearchProgress($"  -> 添加失败: {ex.Message}\n");
                    }
                }

                // 保存项目列表
                if (report.AddedProjects > 0)
                {
                    AppendSearchProgress($"\n正在保存 {report.AddedProjects} 个新项目...\n");
                    await SaveProjects();
                    AppendSearchProgress("保存完成\n");
                }

                report.Completed = true;
                report.EndTime = DateTime.Now;
            }
            catch (OperationCanceledException)
            {
                report.Cancelled = true;
                throw;
            }
            catch (Exception ex)
            {
                report.Error = ex.Message;
                AppendSearchProgress($"\n搜索过程中发生错误: {ex.Message}\n");
            }
            finally
            {
                // 确保搜索状态被正确设置
                _isSearching = false;
            }

            return report;
        }


        private async Task<List<string>> SearchForProjectFiles(string searchPath, CancellationToken cancellationToken, SearchReport report)
        {
            var projectFiles = new List<string>();
            var directoriesToProcess = new Queue<string>();
            directoriesToProcess.Enqueue(searchPath);

            report.StartTime = DateTime.Now;
            int lastUpdate = 0;

            try
            {
                // 估算总目录数（用于进度条）
                int totalDirectories = await Task.Run(() => EstimateDirectoryCount(searchPath));
                bool determinateProgress = totalDirectories > 0;

                // 设置进度条为确定进度模式
                if (determinateProgress)
                {
                    await _searchProgressBar.DispatcherQueue.EnqueueAsync(() =>
                    {
                        _searchProgressBar.IsIndeterminate = false;
                        _searchProgressBar.Value = 0;
                        _searchProgressBar.Maximum = totalDirectories;
                        _searchProgressBar.Visibility = Visibility.Visible;
                    });
                }
                else
                {
                    await _searchProgressBar.DispatcherQueue.EnqueueAsync(() =>
                    {
                        _searchProgressBar.IsIndeterminate = true;
                        _searchProgressBar.Visibility = Visibility.Visible;
                    });
                }

                while (directoriesToProcess.Count > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var currentDirectory = directoriesToProcess.Dequeue();
                    report.ProcessedDirectories++;

                    try
                    {
                        // 更新进度条
                        if (determinateProgress && report.ProcessedDirectories <= totalDirectories)
                        {
                            await _searchProgressBar.DispatcherQueue.EnqueueAsync(() =>
                            {
                                _searchProgressBar.Value = report.ProcessedDirectories;
                            });
                        }
                        else if (!determinateProgress)
                        {
                            // 对于不确定进度的情况，偶尔更新一下以显示活动状态
                            await _searchProgressBar.DispatcherQueue.EnqueueAsync(() =>
                            {
                                _searchProgressBar.IsIndeterminate = true;
                            });
                        }

                        // 更新进度文本，但不要过于频繁
                        if (report.ProcessedDirectories - lastUpdate > 50)
                        {
                            AppendSearchProgress($"\r已扫描 {report.ProcessedDirectories} 个目录，找到 {projectFiles.Count} 个项目文件");
                            lastUpdate = report.ProcessedDirectories;

                            // 让出UI线程，防止界面卡顿
                            await Task.Delay(1);
                        }

                        // 搜索当前目录中的uproject文件
                        var files = Directory.GetFiles(currentDirectory, "*.uproject");
                        projectFiles.AddRange(files);
                        report.FoundProjects += files.Length;

                        // 添加子目录到队列
                        var subDirectories = Directory.GetDirectories(currentDirectory);
                        foreach (var subDir in subDirectories)
                        {
                            // 跳过常见的非项目目录以提高搜索效率
                            var dirName = Path.GetFileName(subDir);
                            if (dirName.Equals("Binaries", StringComparison.OrdinalIgnoreCase) ||
                                dirName.Equals("Intermediate", StringComparison.OrdinalIgnoreCase) ||
                                dirName.Equals("Saved", StringComparison.OrdinalIgnoreCase) ||
                                dirName.Equals("Content", StringComparison.OrdinalIgnoreCase) ||
                                dirName.Equals("DerivedDataCache", StringComparison.OrdinalIgnoreCase) ||
                                dirName.Equals(".git", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            directoriesToProcess.Enqueue(subDir);
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        report.InaccessibleDirectories++;
                        AppendSearchProgress($"\n无法访问目录: {currentDirectory}\n");
                    }
                    catch (Exception ex)
                    {
                        report.FailedDirectories++;
                        AppendSearchProgress($"\n处理目录时出错 {currentDirectory}: {ex.Message}\n");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                report.Error = ex.Message;
            }
            finally
            {
                // 隐藏进度条
                await _searchProgressBar.DispatcherQueue.EnqueueAsync(() =>
                {
                    _searchProgressBar.Visibility = Visibility.Collapsed;
                });
            }

            return projectFiles;
        }

        private TextBox _searchProgressTextBox;

        private int EstimateDirectoryCount(string searchPath)
        {
            try
            {
                // 快速估算目录数量，只检查前几层目录
                int count = 1; // 包含根目录
                var directoriesToCheck = new Queue<string>();
                directoriesToCheck.Enqueue(searchPath);

                int levelsChecked = 0;
                int dirsPerLevel = 0;

                while (directoriesToCheck.Count > 0 && levelsChecked < 3) // 只检查前3层
                {
                    var currentDirectory = directoriesToCheck.Dequeue();
                    dirsPerLevel++;

                    try
                    {
                        var subDirectories = Directory.GetDirectories(currentDirectory);
                        foreach (var subDir in subDirectories)
                        {
                            var dirName = Path.GetFileName(subDir);
                            // 跳过常见的非项目目录
                            if (!dirName.Equals("Binaries", StringComparison.OrdinalIgnoreCase) &&
                                !dirName.Equals("Intermediate", StringComparison.OrdinalIgnoreCase) &&
                                !dirName.Equals("Saved", StringComparison.OrdinalIgnoreCase) &&
                                !dirName.Equals("Content", StringComparison.OrdinalIgnoreCase) &&
                                !dirName.Equals("DerivedDataCache", StringComparison.OrdinalIgnoreCase) &&
                                !dirName.Equals(".git", StringComparison.OrdinalIgnoreCase))
                            {
                                directoriesToCheck.Enqueue(subDir);
                            }
                        }
                    }
                    catch
                    {
                        // 忽略访问错误
                    }

                    // 如果当前层级处理完毕，进入下一层级
                    if (dirsPerLevel >= Math.Pow(5, levelsChecked)) // 估算每层目录数
                    {
                        count += directoriesToCheck.Count;
                        levelsChecked++;
                        dirsPerLevel = 0;
                    }
                }

                return count > 0 ? count : 1;
            }
            catch
            {
                return 0; // 无法估算时返回0，使用不确定进度模式
            }
        }

        private UIElement CreateStatusCard(string text, string glyph, Color color)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(30, color.R, color.G, color.B)),
                BorderBrush = new SolidColorBrush(color),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(15)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new FontIcon
            {
                Glyph = glyph,
                Foreground = new SolidColorBrush(color),
                FontSize = 20,
                Margin = new Thickness(0, 0, 10, 0)
            };
            Grid.SetColumn(icon, 0);
            grid.Children.Add(icon);

            var textBlock = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Colors.White)
            };
            Grid.SetColumn(textBlock, 1);
            grid.Children.Add(textBlock);

            card.Child = grid;
            return card;
        }

        private UIElement CreateAutoSearchDialogContent()
        {
            var stackPanel = new StackPanel
            {
                Spacing = 15
            };

            var headerPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            var searchIcon = new FontIcon
            {
                Glyph = "\uE721", // 搜索图标
                FontSize = 20
            };

            var infoText = new TextBlock
            {
                Text = "正在自动搜索项目文件，请稍候...",
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Center
            };

            headerPanel.Children.Add(searchIcon);
            headerPanel.Children.Add(infoText);
            stackPanel.Children.Add(headerPanel);

            // 添加进度条
            _searchProgressBar = new ProgressBar
            {
                IsIndeterminate = true, // 默认为不确定进度
                Height = 12,
                Visibility = Visibility.Collapsed,
                CornerRadius = new CornerRadius(6)
            };
            stackPanel.Children.Add(_searchProgressBar);

            // 创建带边框的文本框
            var textBoxBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 98, 98, 98)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4)
            };

            _searchProgressTextBox = new TextBox
            {
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                Height = 300,
                Width = 600,
                FontFamily = new FontFamily(Settings.Get("Console.Font", "Consolas")),
                FontSize = 12,
                BorderThickness = new Thickness(0), // 移除默认边框
                Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30))
            };

            textBoxBorder.Child = _searchProgressTextBox;
            stackPanel.Children.Add(textBoxBorder);

            return stackPanel;
        }


        private async void AppendSearchProgress(string text)
        {
            if (_searchProgressTextBox != null)
            {
                // 使用Dispatcher在UI线程上安全更新
                await _searchProgressTextBox.DispatcherQueue.EnqueueAsync(() =>
                {
                    // 根据文本内容添加颜色编码
                    if (text.StartsWith("处理:"))
                    {
                        _searchProgressTextBox.Text += text;
                    }
                    else if (text.StartsWith("  -> 添加成功"))
                    {
                        _searchProgressTextBox.Text += text.Replace("  -> 添加成功", "  -> 添加成功 ✓");
                    }
                    else if (text.StartsWith("  -> 项目已存在"))
                    {
                        _searchProgressTextBox.Text += text.Replace("  -> 项目已存在", "  -> 项目已存在 ⚠");
                    }
                    else if (text.StartsWith("  -> 添加失败") || text.Contains("错误"))
                    {
                        _searchProgressTextBox.Text += text.Replace("  -> 添加失败", "  -> 添加失败 ✗");
                    }
                    else
                    {
                        _searchProgressTextBox.Text += text;
                    }

                    // 自动滚动到底部
                    _searchProgressTextBox.SelectionStart = _searchProgressTextBox.Text.Length;
                });
            }
        }

        private void LayoutSegmented_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var index = LayoutSegmented?.SelectedIndex ?? 0;
            SetLayoutMode(index == 1 ? ProjectCardLayoutMode.List :
                index == 2 ? ProjectCardLayoutMode.Compact :
                ProjectCardLayoutMode.Grid);
        }

        private void ProjectImage_ImageFailed(object sender, Microsoft.UI.Xaml.ExceptionRoutedEventArgs e)
        {
            try
            {
                if (sender is Image img)
                {
                    // 构造绝对文件路径：<exe目录>\Assets\MdiUnreal.png
                    var placeholderPath = Path.Combine(AppContext.BaseDirectory, "Assets", "MdiUnreal.png");

                    // 如果你还没把图片复制到输出目录，请设置该文件 属性 -> Copy to Output Directory = Copy always/PreserveNewest
                    if (File.Exists(placeholderPath))
                    {
                        // 用绝对 file 路径创建 Uri（会被解析为 file:///C:/...）
                        var uri = new Uri(placeholderPath, UriKind.Absolute);
                        img.Source = new BitmapImage(uri);
                    }
                    else
                    {
                        // 找不到占位图就清空，避免再次触发失败循环
                        img.Source = null;
                    }
                }
            }
            catch (Exception ex)
            {
                WriteDebug($"Error: {ex.Message}");
            }
        }

        private void ProjectCard_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is Grid root && root.FindName("HoverOverlay") is Grid overlay)
            {
                overlay.Visibility = Visibility.Visible;
                overlay.IsHitTestVisible = true; // 只有显示时才接管点击
            }
        }

        private void ProjectCard_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is Grid root && root.FindName("HoverOverlay") is Grid overlay)
            {
                overlay.Visibility = Visibility.Collapsed;
                overlay.IsHitTestVisible = false; // 隐藏时不拦截命中测试
            }
        }

        private async Task RehydrateProjectsAsync(List<ProjectInfo> list)
        {
            var tasks = list.Select(async (p, idx) =>
            {
                // 如果来自存储的数据不完整，用真实 .uproject 重新解析一次
                if ((p.Modules == null || p.Modules.Count == 0) &&
                    File.Exists(p.ProjectPath))
                {
                    var fresh = await CreateProjectInfo(p.ProjectPath); // 这个内部会解析 Modules/Plugins/Description 等
                    if (fresh != null)
                    {
                        // 把关键信息拷回（也可以直接替换引用，看你是否需要保留对象实例）
                        p.EngineAssociation = fresh.EngineAssociation;
                        p.Description = fresh.Description;
                        p.Category = fresh.Category;
                        p.Modules = fresh.Modules ?? new List<ProjectModule>();
                        p.Plugins = fresh.Plugins ?? new List<ProjectPlugin>();
                        p.LastModified = fresh.LastModified;
                        p.AssociatedEngine = fresh.AssociatedEngine;
                        p.RefreshThumbnail(); // 也会顺带 CheckGitStatus
                    }
                }
                else
                {
                    // 至少刷新一下缩略图/Git
                    p.RefreshThumbnail();
                }

                // 异步计算项目大小（只做一次）
                if (p.ProjectSize == 0 && Directory.Exists(p.ProjectDirectory))
                {
                    p.ProjectSize = await CalculateProjectSizeAsync(p.ProjectDirectory);
                }
            });

            await Task.WhenAll(tasks);
        }

        private Task<long> CalculateProjectSizeAsync(string root)
        {
            // 建议排除这些大且无关的目录
            string[] excludeDirs = new[]
            {
                ".git", ".vs", ".idea",
                "Binaries", "DerivedDataCache", "Intermediate",
                "Saved" // 视需要保留或排除
            };

            return Task.Run<long>(() =>
            {
                long total = 0;
                try
                {
                    var stack = new Stack<string>();
                    stack.Push(root);

                    while (stack.Count > 0)
                    {
                        var dir = stack.Pop();

                        // 排除目录
                        var name = Path.GetFileName(dir);
                        if (excludeDirs.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        // 子目录
                        try
                        {
                            foreach (var sub in Directory.EnumerateDirectories(dir))
                                stack.Push(sub);
                        }
                        catch
                        {
                            /* ignore */
                        }

                        // 文件
                        try
                        {
                            foreach (var file in Directory.EnumerateFiles(dir))
                            {
                                try
                                {
                                    total += new FileInfo(file).Length;
                                }
                                catch
                                {
                                }
                            }
                        }
                        catch
                        {
                            /* ignore */
                        }
                    }
                }
                catch
                {
                    /* ignore */
                }

                return total;
            });
        }
    }

    public enum ProjectCardLayoutMode
    {
        Grid,
        List,
        Compact
    }

    public class SearchReport
    {
        public string SearchPath
        {
            get;
            set;
        }

        public DateTime StartTime
        {
            get;
            set;
        }

        public DateTime EndTime
        {
            get;
            set;
        }

        public int ProcessedDirectories
        {
            get;
            set;
        }

        public int InaccessibleDirectories
        {
            get;
            set;
        }

        public int FailedDirectories
        {
            get;
            set;
        }

        public int FoundProjects
        {
            get;
            set;
        }

        public int AddedProjects
        {
            get;
            set;
        }

        public int SkippedProjects
        {
            get;
            set;
        }

        public int FailedProjects
        {
            get;
            set;
        }

        public bool Completed
        {
            get;
            set;
        }

        public bool Cancelled
        {
            get;
            set;
        }

        public string Error
        {
            get;
            set;
        }
    }
}