using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using Windows.Storage.Pickers;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media;
using System.Linq;
using DreamUnrealManager.Models;
using DreamUnrealManager.Services;
using WinRT;

namespace DreamUnrealManager.Views
{
    public sealed partial class LauncherPage : Page
    {
        private List<ProjectInfo> _allProjects;
        private List<ProjectInfo> _filteredProjects;
        private string _currentSearchText = "";
        private string _currentEngineFilter = "ALL_ENGINES";
        private string _currentSortOrder = "LastUsed";

        public LauncherPage()
        {
            try
            {
                this.InitializeComponent();
                _allProjects = new List<ProjectInfo>();
                _filteredProjects = new List<ProjectInfo>();
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

                    WriteDebug($"解析项目文件成功: {projectInfo.DisplayName}");
                }
                catch (Exception ex)
                {
                    WriteDebug($"解析项目文件失败，使用默认值: {ex.Message}");
                    projectInfo.EngineAssociation = "未知版本";
                    projectInfo.Description = "无法读取项目描述";
                }

                return projectInfo;
            }
            catch (Exception ex)
            {
                WriteDebug($"CreateProjectInfo失败: {ex.Message}");
                return null;
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

                _filteredProjects = filtered.ToList();

                WriteDebug($"筛选完成，从 {_allProjects.Count} 筛选出 {_filteredProjects.Count} 个项目");

                UpdateProjectsUI();
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

        private void UpdateProjectsUI()
        {
            try
            {
                WriteDebug("开始更新项目UI");

                if (ProjectsContainer == null)
                {
                    WriteDebug("ProjectsContainer为空，跳过更新");
                    return;
                }

                ProjectsContainer.Children.Clear();

                foreach (var project in _filteredProjects)
                {
                    try
                    {
                        var projectCard = CreateProjectCard(project);
                        ProjectsContainer.Children.Add(projectCard);
                    }
                    catch (Exception ex)
                    {
                        WriteDebug($"创建项目卡片失败 {project.DisplayName}: {ex.Message}");
                    }
                }

                WriteDebug($"UI更新完成，显示 {_filteredProjects.Count} 个项目卡片");

                if (StatusText != null)
                    StatusText.Text = $"显示 {_filteredProjects.Count} 个项目";
            }
            catch (Exception ex)
            {
                WriteDebug($"UpdateProjectsUI失败: {ex.Message}");
                if (StatusText != null)
                    StatusText.Text = $"更新UI失败: {ex.Message}";
            }
        }

        private Border CreateProjectCard(ProjectInfo project)
        {
            try
            {
                var border = new Border
                {
                    Margin = new Thickness(0, 8, 0, 8),
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    CornerRadius = new CornerRadius(6),
                    BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Gray),
                    BorderThickness = new Thickness(1),
                    MinHeight = 120,
                    Padding = new Thickness(0) // 移除所有内边距
                };

                var grid = new Grid();
                // 缩略图列，信息列，按钮列
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 缩略图列
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 信息列
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 按钮列

                // 左侧正方形缩略图 - 完全贴边，无间距
                var thumbnailContainer = CreateThumbnailContainer(project);
                Grid.SetColumn(thumbnailContainer, 0);
                grid.Children.Add(thumbnailContainer);

                // 中间信息面板 - 添加左边距与缩略图分离
                var infoPanel = new StackPanel
                {
                    Spacing = 8,
                    Margin = new Thickness(20, 16, 16, 16), // 左边距20px与缩略图分离
                    VerticalAlignment = VerticalAlignment.Center
                };

                var nameText = new TextBlock
                {
                    Text = project.DisplayName ?? "未知项目",
                    FontSize = 20,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                };
                infoPanel.Children.Add(nameText);

                var descText = new TextBlock
                {
                    Text = project.GetDescription(),
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray),
                    TextWrapping = TextWrapping.Wrap,
                    MaxLines = 2
                };
                infoPanel.Children.Add(descText);

                var engineText = new TextBlock
                {
                    Text = $"引擎: {project.GetEngineDisplayName()}",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray)
                };
                infoPanel.Children.Add(engineText);

                var modifiedText = new TextBlock
                {
                    Text = $"修改: {project.LastModified:yyyy/MM/dd}",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray)
                };
                infoPanel.Children.Add(modifiedText);

                Grid.SetColumn(infoPanel, 1);
                grid.Children.Add(infoPanel);

                // 右侧按钮面板
                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 16, 20, 16) // 右边距
                };

                var launchButton = new Button
                {
                    Content = "启动项目",
                    MinWidth = 100,
                    Tag = project
                };
                launchButton.Click += (s, e) => LaunchProject(project);
                buttonPanel.Children.Add(launchButton);

                var moreButton = new Button
                {
                    Content = "更多操作",
                    Tag = project
                };
                moreButton.Click += (s, e) => ShowProjectMenu(project, moreButton);
                buttonPanel.Children.Add(moreButton);

                Grid.SetColumn(buttonPanel, 2);
                grid.Children.Add(buttonPanel);

                border.Child = grid;
                return border;
            }
            catch (Exception ex)
            {
                WriteDebug($"CreateProjectCard失败: {ex.Message}");

                return new Border
                {
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Red),
                    CornerRadius = new CornerRadius(2),
                    Padding = new Thickness(15),
                    Child = new TextBlock
                    {
                        Text = $"加载项目失败: {project?.DisplayName ?? "未知"}",
                        Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
                    }
                };
            }
        }

        // 创建正方形缩略图容器，添加圆角和边距
        private FrameworkElement CreateThumbnailContainer(ProjectInfo project)
        {
            const int thumbnailSize = 120; // 正方形尺寸

            // 构建缩略图路径
            var thumbnailPath = string.Empty;
            if (!string.IsNullOrEmpty(project.ProjectDirectory))
            {
                thumbnailPath = Path.Combine(project.ProjectDirectory, "Saved", "AutoScreenshot.png");
            }

            // 检查缩略图是否存在
            if (!string.IsNullOrEmpty(thumbnailPath) && File.Exists(thumbnailPath))
            {
                try
                {
                    // 有缩略图时，使用 Border 包裹 Image 并添加圆角
                    var thumbnailImage = new Image
                    {
                        Stretch = Stretch.UniformToFill,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    var bitmapImage = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                    bitmapImage.UriSource = new Uri(thumbnailPath);
                    thumbnailImage.Source = bitmapImage;

                    // 使用 Border 添加圆角效果
                    var imageContainer = new Border
                    {
                        Width = thumbnailSize,
                        Height = thumbnailSize,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(12, 12, 0, 12), // 添加边距：左边距12，上下边距12，右边距0
                        CornerRadius = new CornerRadius(8), // 添加圆角
                        Child = thumbnailImage
                    };

                    return imageContainer;
                }
                catch
                {
                    // 图片加载失败，使用默认样式
                }
            }

            // 没有缩略图或加载失败时，创建默认的正方形缩略图，同样添加圆角和边距
            var defaultContainer = new Border
            {
                Width = thumbnailSize,
                Height = thumbnailSize,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 12, 0, 12), // 添加边距：左边距12，上下边距12，右边距0
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(8), // 添加圆角
                Background = new LinearGradientBrush
                {
                    StartPoint = new Windows.Foundation.Point(0, 0),
                    EndPoint = new Windows.Foundation.Point(1, 1),
                    GradientStops =
                    {
                        new GradientStop { Color = Microsoft.UI.Colors.DarkSlateBlue, Offset = 0 },
                        new GradientStop { Color = Microsoft.UI.Colors.SlateBlue, Offset = 0.5 },
                        new GradientStop { Color = Microsoft.UI.Colors.MediumSlateBlue, Offset = 1 }
                    }
                }
            };

            // 添加项目图标
            var icon = new FontIcon
            {
                Glyph = "\uE7C3", // 项目图标
                FontSize = 48,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.8
            };

            defaultContainer.Child = icon;
            return defaultContainer;
        }

        private void ShowProjectMenu(ProjectInfo project, Button moreButton)
        {
            try
            {
                var flyout = new MenuFlyout();

                var openFolderItem = new MenuFlyoutItem { Text = "打开项目文件夹", Tag = project };
                openFolderItem.Click += (s, e) => OpenProjectFolder(project);
                flyout.Items.Add(openFolderItem);

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
                if (EmptyStatePanel == null || ProjectsContainer == null) return;

                if (_filteredProjects.Count == 0)
                {
                    EmptyStatePanel.Visibility = Visibility.Visible;
                    ProjectsContainer.Visibility = Visibility.Collapsed;

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
                    ProjectsContainer.Visibility = Visibility.Visible;
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
                            refreshedProject.LastUsed = project.LastUsed;
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
    }
}