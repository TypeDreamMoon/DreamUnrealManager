using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using CommunityToolkit.WinUI.Controls;
using DreamUnrealManager.Models;
using DreamUnrealManager.Services;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace DreamUnrealManager.Views
{
    public sealed partial class LauncherPage : Page
    {
        // 数据
        private List<ProjectInfo> _allProjects = new();
        private readonly ObservableCollection<ProjectInfo> _filteredProjects = new();
        public ObservableCollection<ProjectInfo> FilteredProjects => _filteredProjects;

        // 当前筛选状态
        private string _currentSearchText = "";
        private string _currentEngineFilter = "ALL_ENGINES";
        private string _currentSortOrder = "LastUsed";
        private bool _onlyFavorites = false;

        // 服务
        private readonly IProjectRepository _repo;
        private readonly IProjectFactory _factory;
        private readonly IProjectFilter _filter;
        private readonly IProjectSearchService _search;
        private readonly IBuildService _build;
        private readonly IIdeLauncher _ide;
        private readonly IEngineSwitchService _engineSwitch;
        private readonly IDialogService _dialogs;
        private readonly IUnrealProjectService _uproj;

        // 进度
        private int _metaTotal;
        private int _metaDone;

        public LauncherPage()
        {
            // 先初始化服务（避免 XAML 初始化期间触发的事件用到空引用）
            _repo = new ProjectRepository();
            _filter = new ProjectFilter();
            _factory = new ProjectFactory(new EngineResolver());
            _search = new ProjectSearchService(_factory);
            _build = new BuildService();
            _ide = new IdeLauncher();
            _engineSwitch = new EngineSwitchService();
            _dialogs = new DialogService();
            _uproj = new UnrealProjectService();

            InitializeComponent(); // 再加载 XAML
            Loaded += LauncherPage_Loaded;
        }


        private async void LauncherPage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                SetStatus("正在加载项目...");
                _allProjects = await _repo.LoadAsync();
                LoadEngineFilters();
                ApplyFilters();

                FavoriteFrontToggle.IsChecked = Settings.Get("Launcher.FavoriteFirst", true);

                // 两阶段补全（元数据→体积）
                _ = RehydrateProjectsAsync(_allProjects);

                SetStatus("就绪");
            }
            catch (Exception ex)
            {
                SetStatus($"加载失败：{ex.Message}");
            }
        }

        #region 进度/调度/工具

        private void SetStatus(string text)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (StatusText != null) StatusText.Text = text;
            });
        }

        public Visibility BoolToVisibility(bool v) => v ? Visibility.Visible : Visibility.Collapsed;

        private void ShowGlobalProgress(int done, int total, bool show)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (GlobalOverlay != null)
                    GlobalOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

                if (show && total > 0)
                {
                    var pct = (int)((done / (double)total) * 100);
                    if (GlobalOverlayText != null)
                        GlobalOverlayText.Text = $"正在加载项目信息... {pct}% ({done}/{total})";
                }
                else
                {
                    if (GlobalOverlayText != null)
                        GlobalOverlayText.Text = "就绪";
                }
            });
        }


        private void RunOnUI(Action action)
        {
            var dq = DispatcherQueue;
            if (dq == null)
            {
                action();
                return;
            }

            if (dq.HasThreadAccess)
            {
                action();
            }
            else
            {
                _ = dq.TryEnqueue(() => action()); // 这里用 lambda 包一层
            }
        }

        #endregion

        #region 两阶段加载

        private async Task RehydrateProjectsAsync(List<ProjectInfo> list)
        {
            var items = list.ToList();
            _metaTotal = items.Count;
            _metaDone = 0;

            ShowGlobalProgress(0, _metaTotal, true);

            // 阶段A：元数据（决定遮罩+全局进度）
            var semA = new SemaphoreSlim(Math.Max(2, Environment.ProcessorCount - 1));
            var metaTasks = items.Select(async p =>
            {
                await semA.WaitAsync();
                try
                {
                    RunOnUI(() => p.IsLoadingMeta = true);

                    if (File.Exists(p.ProjectPath))
                    {
                        var fresh = await _factory.CreateAsync(p.ProjectPath);
                        if (fresh != null)
                        {
                            // 回填关键字段并统一刷新派生属性
                            RunOnUI(() =>
                            {
                                p.UpdateFrom(fresh);
                                p.RefreshThumbnail();
                            });
                        }
                    }
                    else
                    {
                        p.IsValid = false;
                    }
                }
                catch
                {
                    /* ignore */
                }
                finally
                {
                    RunOnUI(() => p.IsLoadingMeta = false);
                    var d = Interlocked.Increment(ref _metaDone);
                    ShowGlobalProgress(d, _metaTotal, true);
                    semA.Release();
                }
            });

            await Task.WhenAll(metaTasks);
            ShowGlobalProgress(_metaTotal, _metaTotal, false);

            // 阶段B：体积（慢，不影响全局进度）
            _ = Task.Run(async () =>
            {
                var semB = new SemaphoreSlim(2);
                var sizeTasks = items.Select(async p =>
                {
                    if (p.ProjectSize > 0 || !Directory.Exists(p.ProjectDirectory)) return;
                    await semB.WaitAsync();
                    try
                    {
                        RunOnUI(() => p.IsSizing = true);
                        var bytes = await CalculateProjectSizeAsync(p.ProjectDirectory);
                        RunOnUI(() => p.SetProjectSize(bytes));
                    }
                    catch
                    {
                    }
                    finally
                    {
                        RunOnUI(() => p.IsSizing = false);
                        semB.Release();
                    }
                });
                await Task.WhenAll(sizeTasks);
            });
        }

        private static Task<long> CalculateProjectSizeAsync(string root)
        {
            return Task.Run(() =>
            {
                long total = 0;
                try
                {
                    // 可按需排除
                    var excludes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ".git", "Intermediate", "Binaries", "DerivedDataCache", ".vs", "Saved/Logs"
                    };

                    foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                            if (excludes.Any(ex => rel.StartsWith(ex, StringComparison.OrdinalIgnoreCase)))
                                continue;

                            total += new FileInfo(file).Length;
                        }
                        catch
                        {
                        }
                    }
                }
                catch
                {
                }

                return total;
            });
        }

        #endregion

        #region UI 事件：搜索/筛选/排序/清除

        private void ProjectSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                _currentSearchText = sender.Text ?? "";
                ApplyFilters();
            }
        }

        private void EngineFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_filter == null) return; // 保险
            if (sender is ComboBox cb && cb.SelectedItem is ComboBoxItem item)
            {
                _currentEngineFilter = item.Tag?.ToString() ?? "ALL_ENGINES";
                ApplyFilters();
            }
        }

        private void SortOrderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_filter == null) return; // 保险
            if (sender is ComboBox cb && cb.SelectedItem is ComboBoxItem item)
            {
                _currentSortOrder = item.Tag?.ToString() ?? "LastUsed";
                ApplyFilters();
            }
        }

        private void ClearFilters_Click(object sender, RoutedEventArgs e)
        {
            if (ProjectSearchBox != null) ProjectSearchBox.Text = "";
            _currentSearchText = "";
            if (EngineFilterComboBox != null) EngineFilterComboBox.SelectedIndex = 0;
            _currentEngineFilter = "ALL_ENGINES";
            if (SortOrderComboBox != null) SortOrderComboBox.SelectedIndex = 0;
            _currentSortOrder = "LastUsed";
            ApplyFilters();
        }

        private void ApplyFilters()
        {
            if (_allProjects == null) _allProjects = new List<ProjectInfo>();
            if (_filter == null) return;

            var opts = new ProjectFilterOptions
            {
                SearchText = _currentSearchText ?? "",
                EngineFilter = _currentEngineFilter ?? "ALL_ENGINES",
                SortOrder = _currentSortOrder ?? "LastUsed",
                OnlyFavorites = FavoriteOnlyToggle?.IsChecked == true, // 工具栏开关
                FavoriteFirst = Settings.Get("Launcher.FavoriteFirst", true) // 收藏置顶
            };

            var result = _filter.FilterAndSort(_allProjects, opts)?.ToList() ?? new List<ProjectInfo>();

            _filteredProjects.Clear();
            foreach (var p in result) _filteredProjects.Add(p);

            UpdateEmptyState();
            UpdateFilterStats();
        }


        private void LoadEngineFilters()
        {
            try
            {
                EngineFilterComboBox.Items.Clear();
                EngineFilterComboBox.Items.Add(new ComboBoxItem { Content = "所有引擎版本", Tag = "ALL_ENGINES" });

                var versions = _allProjects.Select(p => p.EngineAssociation)
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Distinct()
                    .OrderByDescending(v => v);
                foreach (var v in versions)
                {
                    EngineFilterComboBox.Items.Add(new ComboBoxItem { Content = $"UE {v}", Tag = v });
                }

                EngineFilterComboBox.SelectedIndex = 0;
            }
            catch
            {
            }
        }

        private void UpdateFilterStats()
        {
            try
            {
                var total = _allProjects.Count;
                var filtered = _filteredProjects.Count;
                if (FilterStatsText != null)
                    FilterStatsText.Text = total == filtered ? $"显示 {total} 个项目" : $"显示 {filtered} / {total} 个项目";
            }
            catch
            {
            }
        }

        private void UpdateEmptyState()
        {
            try
            {
                if (EmptyStatePanel == null || ProjectsListView == null) return;

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
            catch
            {
            }
        }

        #endregion

        #region 布局切换（Segmented）

        private void LayoutSegmented_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProjectsListView == null || LayoutSegmented == null) return;

            var index = LayoutSegmented.SelectedIndex;
            switch (index)
            {
                case 0: // 网格
                    ProjectsListView.ItemsPanel = (ItemsPanelTemplate)Resources["GridItemsPanel"];
                    ProjectsListView.ItemTemplate = (DataTemplate)Resources["GridProjectTemplate"];
                    break;
                case 1: // 列表
                    ProjectsListView.ItemsPanel = (ItemsPanelTemplate)Resources["ListItemsPanel"];
                    ProjectsListView.ItemTemplate = (DataTemplate)Resources["ListProjectTemplate"];
                    break;
                case 2: // 紧凑
                    ProjectsListView.ItemsPanel = (ItemsPanelTemplate)Resources["CompactItemsPanel"];
                    ProjectsListView.ItemTemplate = (DataTemplate)Resources["CompactProjectTemplate"];
                    break;
            }
        }

        #endregion

        #region 按钮/菜单

        private async void AddProject_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker
                {
                    ViewMode = PickerViewMode.List,
                    SuggestedStartLocation = PickerLocationId.Desktop
                };
                picker.FileTypeFilter.Add(".uproject");

                var window = App.MainWindow;
                if (window != null)
                {
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                    WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                }

                var file = await picker.PickSingleFileAsync();
                if (file == null) return;

                var created = await _factory.CreateAsync(file.Path);
                if (created != null && !_allProjects.Any(p => p.ProjectPath == created.ProjectPath))
                {
                    _allProjects.Add(created);
                    await _repo.SaveAsync(_allProjects);
                    LoadEngineFilters();
                    ApplyFilters();
                }
            }
            catch (Exception ex)
            {
                await _dialogs.ShowMessageAsync("错误", $"添加项目失败：{ex.Message}");
            }
        }

        private async void RefreshProjects_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetStatus("正在刷新项目列表...");
                var freshList = new List<ProjectInfo>();
                foreach (var p in _allProjects.ToList())
                {
                    if (!File.Exists(p.ProjectPath)) continue;
                    var fresh = await _factory.CreateAsync(p.ProjectPath);
                    if (fresh != null)
                    {
                        fresh.LastUsed = p.LastUsed;
                        freshList.Add(fresh);
                    }
                }

                _allProjects = freshList;
                await _repo.SaveAsync(_allProjects);
                LoadEngineFilters();
                ApplyFilters();

                // 体积后台算
                _ = RehydrateProjectsAsync(_allProjects);
                SetStatus("项目列表已刷新");
            }
            catch (Exception ex)
            {
                await _dialogs.ShowMessageAsync("错误", $"刷新失败：{ex.Message}");
            }
        }

        private async void AutoSearchProjects_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var folderPicker = new Windows.Storage.Pickers.FolderPicker();
                folderPicker.FileTypeFilter.Add("*");
                var window = App.MainWindow;
                if (window != null)
                {
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                    WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);
                }

                var folder = await folderPicker.PickSingleFolderAsync();
                if (folder == null) return;

                SetStatus("正在搜索项目...");
                var progress = new Progress<int>(v => SetStatus($"正在搜索... {v}%"));
                var found = await _search.SearchAsync(folder.Path, progress);

                // 合并去重
                var newOnes = found.Where(f => !_allProjects.Any(p => p.ProjectPath == f.ProjectPath)).ToList();
                _allProjects.AddRange(newOnes);

                await _repo.SaveAsync(_allProjects);
                LoadEngineFilters();
                ApplyFilters();

                // 补全加载
                _ = RehydrateProjectsAsync(newOnes);
                SetStatus($"搜索完成：新增 {newOnes.Count} 项目");
            }
            catch (OperationCanceledException)
            {
                SetStatus("已取消搜索");
            }
            catch (Exception ex)
            {
                await _dialogs.ShowMessageAsync("错误", $"自动搜索失败：{ex.Message}");
            }
        }

        private async void ContextMenu_ShowDetails_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem item || item.Tag is not ProjectInfo p) return;

            var dlg = new ContentDialog
            {
                Title = $"项目详情 — {p.DisplayName}",
                PrimaryButtonText = "关闭",
                XamlRoot = this.XamlRoot,
            };

            // ✅ 关键：放宽对话框自身的模板宽度限制
            dlg.Resources["ContentDialogMinWidth"] = 780d;
            dlg.Resources["ContentDialogMaxWidth"] = 1000d;
            dlg.Resources["ContentDialogMaxHeight"] = 720d;

            // ❗不要再给内容 root 设 MaxWidth（会再次被卡住）
            var root = new StackPanel
            {
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Stretch
                // 不要 Width/MaxWidth
            };

            // 头部 + 详情表格
            var header = BuildHeader(p);
            var detailsGrid = BuildDetailsGrid(p);

            root.Children.Add(header);
            root.Children.Add(new ScrollViewer
            {
                Content = detailsGrid,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 480
            });

            dlg.Content = root;
            await dlg.ShowAsync();
        }

        private FrameworkElement BuildHeader(ProjectInfo p)
        {
            var header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Margin = new Thickness(0, 0, 0, 12)
            };

            // 左：缩略图（优先 ProjectIconPath / ThumbnailPath，都是本地文件）
            var img = new Image
            {
                Width = 96,
                Height = 96,
                Stretch = Stretch.UniformToFill
            };
            img.ImageFailed += ProjectImage_ImageFailed; // 你已有的兜底

            string imagePath = null;
            if (!string.IsNullOrWhiteSpace(p.ProjectIconPath) && File.Exists(p.ProjectIconPath))
                imagePath = p.ProjectIconPath;
            else if (!string.IsNullOrWhiteSpace(p.ThumbnailPath) && File.Exists(p.ThumbnailPath))
                imagePath = p.ThumbnailPath;

            if (imagePath != null)
            {
                // 本地文件要用 file:/// URI
                var uri = new Uri($"file:///{imagePath.Replace("\\", "/")}");
                img.Source = new BitmapImage(uri);
            }

            header.Children.Add(new Border
            {
                Width = 96,
                Height = 96,
                CornerRadius = new CornerRadius(8),
                Child = img
            });

            // 右：标题/描述/引擎
            var titleStack = new StackPanel
            {
                Spacing = 2,
                VerticalAlignment = VerticalAlignment.Center
            };

            titleStack.Children.Add(new TextBlock
            {
                Text = p.DisplayName,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold
            });

            if (!string.IsNullOrWhiteSpace(p.DescriptionDisplay))
            {
                titleStack.Children.Add(new TextBlock
                {
                    Text = p.DescriptionDisplay,
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.85
                });
            }

            titleStack.Children.Add(new TextBlock
            {
                Text = p.EngineDisplayName,
                Style = Application.Current.Resources["CaptionTextBlockStyle"] as Style,
                Foreground = new SolidColorBrush(Colors.Gray)
            });

            header.Children.Add(titleStack);
            return header;
        }

        private Grid BuildDetailsGrid(ProjectInfo p)
        {
            var g = new Grid { RowSpacing = 8, ColumnSpacing = 12 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            int r = -1;

            void Add(string label, string value, FrameworkElement extra = null)
            {
                r++;
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var labelBlock = new TextBlock
                {
                    Text = label,
                    Style = Application.Current.Resources["CaptionTextBlockStyle"] as Style,
                    Opacity = 0.8
                };
                var valueBlock = new TextBlock
                {
                    Text = value ?? "-",
                    TextWrapping = TextWrapping.Wrap
                };

                Grid.SetRow(labelBlock, r);
                Grid.SetColumn(labelBlock, 0);
                Grid.SetRow(valueBlock, r);
                Grid.SetColumn(valueBlock, 1);
                g.Children.Add(labelBlock);
                g.Children.Add(valueBlock);

                if (extra != null)
                {
                    r++;
                    g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    Grid.SetRow(extra, r);
                    Grid.SetColumn(extra, 1); // 现在类型匹配
                    g.Children.Add(extra);
                }
            }

            // 基础信息
            Add("项目路径", p.ProjectPath, CreatePathButtons(p));
            Add("项目目录", p.ProjectDirectory);
            Add("引擎", p.EngineDisplayName);
            if (p.AssociatedEngine != null)
            {
                Add("引擎路径", p.AssociatedEngine.EnginePath);
                var ver = !string.IsNullOrEmpty(p.AssociatedEngine.FullVersion) ? p.AssociatedEngine.FullVersion : p.AssociatedEngine.Version;
                Add("引擎版本", ver);
            }

            Add("文件版本", p.FileVersion > 0 ? p.FileVersion.ToString() : "-");
            Add("最后修改", p.LastModifiedString);
            Add("最近使用", p.GetLastUsedString());

            // 体积/模块/插件
            Add("项目大小", p.ProjectSizeString);
            Add("模块数量", p.ModulesCount.ToString());
            Add("启用插件", p.EnabledPluginsCount.ToString());
            Add("主要插件", p.GetMainPluginsList());

            var pluginFiles = EnumerateProjectPlugins(p);
            Add("项目内插件", pluginFiles.Length > 0 ? $"{pluginFiles.Length} 个" : "0",
                CreatePluginsButtons(pluginFiles));

            // 其他
            if (p.TargetPlatforms != null && p.TargetPlatforms.Length > 0)
                Add("目标平台", string.Join(", ", p.TargetPlatforms));
            Add("C++ 项目", p.IsCPlusPlusProject ? "是" : "否");
            Add("Git", p.GitInfoString);

            return g;
        }

        private FrameworkElement CreatePathButtons(ProjectInfo p)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };

            var openBtn = new Button { Content = "打开文件夹" };
            openBtn.Click += (_, __) =>
            {
                try
                {
                    if (Directory.Exists(p.ProjectDirectory))
                        Process.Start("explorer.exe", p.ProjectDirectory);
                }
                catch
                {
                }
            };

            var copyBtn = new Button { Content = "复制路径" };
            copyBtn.Click += (_, __) =>
            {
                try
                {
                    var dp = new DataPackage();
                    dp.SetText(p.ProjectPath);
                    Clipboard.SetContent(dp);
                }
                catch
                {
                }
            };

            sp.Children.Add(openBtn);
            sp.Children.Add(copyBtn);
            return sp;
        }

        private void StartProjectButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ProjectInfo p)
            {
                _uproj.LaunchProject(p);
            }
        }

        private async void StartWithIdeButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ProjectInfo p)
            {
                await _ide.LaunchAsync(p);
            }
        }

        private void MoreCommandsButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ProjectInfo p)
            {
                // 你原来有一个菜单构造，这里也可以维持；
                // 简化：直接打开右键菜单（如果模板里已经绑定 ContextFlyout，就不需要这里做）
                btn.Flyout?.ShowAt(btn);
            }
        }

        // 右键菜单
        private void ContextMenu_LaunchProject_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is ProjectInfo p) _uproj.LaunchProject(p);
        }

        private async void ContextMenu_OpenWithIDE_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is ProjectInfo p) await _ide.LaunchAsync(p);
        }

        private void ContextMenu_OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is ProjectInfo p)
            {
                if (Directory.Exists(p.ProjectDirectory))
                    Process.Start("explorer.exe", p.ProjectDirectory);
            }
        }

        private async void ContextMenu_GenerateVSProject_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is ProjectInfo p)
            {
                SetStatus("正在生成 VS 项目文件...");
                var ok = await _build.GenerateProjectFilesAsync(p.ProjectPath);
                SetStatus(ok ? "生成成功" : "生成失败");
                if (!ok) await _dialogs.ShowMessageAsync("提示", "生成 VS 项目文件失败，请检查引擎路径与 UBT。");
            }
        }

        private void ContextMenu_RefreshProject_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is ProjectInfo p) RefreshProject(p);
        }

        private async void ContextMenu_SwitchEngine_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is ProjectInfo p)
            {
                try
                {
                    var mgr = EngineManagerService.Instance;
                    await mgr.LoadEngines();
                    var engines = mgr.GetValidEngines() ?? Enumerable.Empty<UnrealEngineInfo>();

                    var dialog = new ContentDialog
                    {
                        Title = $"切换 {p.DisplayName} 的引擎版本",
                        PrimaryButtonText = "确定",
                        CloseButtonText = "取消",
                        XamlRoot = this.XamlRoot
                    };

                    var combo = new ComboBox { MinWidth = 300, Margin = new Thickness(0, 10, 0, 0) };
                    foreach (var eng in engines)
                    {
                        var text = !string.IsNullOrEmpty(eng.FullVersion) ? eng.FullVersion :
                            !string.IsNullOrEmpty(eng.Version) ? eng.Version :
                            eng.DisplayName;
                        combo.Items.Add(new ComboBoxItem { Content = $"{eng.DisplayName} ({text})", Tag = eng });
                    }

                    if (combo.Items.Count > 0) combo.SelectedIndex = 0;
                    dialog.Content = combo;

                    var result = await dialog.ShowAsync();
                    if (result == ContentDialogResult.Primary && combo.SelectedItem is ComboBoxItem cb && cb.Tag is UnrealEngineInfo selected)
                    {
                        var ok = await _engineSwitch.SwitchAsync(p, selected.Version);
                        if (ok)
                        {
                            p.EngineAssociation = selected.Version;
                            p.AssociatedEngine = selected;
                            p.NotifyDerived();
                            await _repo.SaveAsync(_allProjects);
                            SetStatus($"已切换到 {selected.DisplayName}");
                        }
                        else
                        {
                            await _dialogs.ShowMessageAsync("提示", "切换失败：无法写入 .uproject。");
                        }
                    }
                }
                catch (Exception ex)
                {
                    await _dialogs.ShowMessageAsync("错误", $"切换失败：{ex.Message}");
                }
            }
        }

        private async void ContextMenu_RemoveProject_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is ProjectInfo p)
            {
                var ok = await _dialogs.ShowConfirmAsync("确认移除",
                    $"确定要从列表中移除项目 \"{p.DisplayName}\" 吗？\n\n注意：这不会删除项目文件，只是从启动器中移除。");
                if (!ok) return;

                _allProjects.Remove(p);
                await _repo.SaveAsync(_allProjects);
                LoadEngineFilters();
                ApplyFilters();
            }
        }

        private void RefreshProject(ProjectInfo project)
        {
            // 只刷新缩略图与 Git
            try
            {
                project.RefreshThumbnail();
            }
            catch
            {
            }
        }

        private void OpenProjectFolder_Click(object sender, RoutedEventArgs e)
        {
            SetStatus("请使用项目卡片上的“更多操作”或右键菜单打开文件夹");
        }

        private void ProjectCard_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.FindName("HoverOverlay") is UIElement overlay)
                overlay.Visibility = Visibility.Visible;
        }

        private void ProjectCard_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.FindName("HoverOverlay") is UIElement overlay)
                overlay.Visibility = Visibility.Collapsed;
        }

        private static string[] EnumerateProjectPlugins(ProjectInfo p)
        {
            try
            {
                if (p == null || string.IsNullOrWhiteSpace(p.ProjectDirectory))
                    return Array.Empty<string>();

                var pluginsRoot = Path.Combine(p.ProjectDirectory, "Plugins");
                if (!Directory.Exists(pluginsRoot))
                    return Array.Empty<string>();

                // 递归查找所有 .uplugin
                return Directory.GetFiles(pluginsRoot, "*.uplugin", SearchOption.AllDirectories);
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private FrameworkElement CreatePluginsButtons(string[] pluginFiles)
        {
            var panel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalSpacing = 10,
                VerticalSpacing = 10
            };

            if (pluginFiles == null || pluginFiles.Length == 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "未在 Plugins 目录找到 .uplugin",
                    Opacity = 0.8
                });
                return panel;
            }

            foreach (var file in pluginFiles)
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var dir = Path.GetDirectoryName(file);

                var btn = new Button
                {
                    Content = name,
                    Tag = dir,
                    MinWidth = 90,
                    Padding = new Thickness(10, 4, 10, 4)
                };

                btn.Click += (_, __) =>
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                            Process.Start("explorer.exe", dir);
                    }
                    catch
                    {
                        /* 忽略 */
                    }
                };

                panel.Children.Add(btn);
            }

            return panel;
        }

        private async void ToggleFavorite_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ProjectInfo p)
            {
                p.ToggleFavorite();
                // 收藏变化后立即保存并刷新过滤/排序
                await _repo.SaveAsync(_allProjects);
                ApplyFilters();
            }
        }

        private async void ContextMenu_ToggleFavorite_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is ProjectInfo p)
            {
                p.ToggleFavorite();
                await _repo.SaveAsync(_allProjects);
                ApplyFilters();
            }
        }

        private void FavoriteOnlyToggle_Checked(object sender, RoutedEventArgs e)
        {
            _onlyFavorites = FavoriteOnlyToggle?.IsChecked == true;
            ApplyFilters();
        }

        private void FavoriteFrontToggle_Checked(object sender, RoutedEventArgs e)
        {
            Settings.Set("Launcher.FavoriteFirst", FavoriteFrontToggle?.IsChecked == true);
            ApplyFilters();
        }

        #endregion

        #region 图像失败占位

        private void ProjectImage_ImageFailed(object sender, ExceptionRoutedEventArgs e)
        {
            try
            {
                if (sender is Image img)
                {
                    string path = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "MdiUnreal.png");
                    img.Source = new BitmapImage(new Uri(path));
                }
            }
            catch
            {
            }
        }

        #endregion
    }
}