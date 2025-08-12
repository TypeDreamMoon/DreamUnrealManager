using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing.Text;
using System.Linq;
using Windows.Storage.Pickers;
using DreamUnrealManager.Models;
using DreamUnrealManager.Services;
using Windows.UI;
using DreamUnrealManager.Helpers;
using Microsoft.UI;
using Microsoft.UI.Xaml.Controls.Primitives;
using Path = ABI.Microsoft.UI.Xaml.Shapes.Path;

namespace DreamUnrealManager.Views
{
    public sealed partial class SettingsPage : Page
    {
        private readonly EngineManagerService _engineManager;

        public ObservableCollection<FontOption> AvailableFonts
        {
            get;
        } = new();

        private bool _fontInitDone;
        private AcrylicSettingsService acrylicSettings
        {
            get;
        } = AcrylicSettingsService.Instance;

        public SettingsPage()
        {
            this.InitializeComponent();
            _engineManager = EngineManagerService.Instance;

            Loaded += SettingsPage_Loaded;
        }


        private async void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadEngines();
            LoadFonts();
            LoadIdeSettings();
            LoadDefaultIdeSetting();
            UpdateIdePathUI();

            AutoDetectIdePaths();
            OnLoaded_SyncThemeSelection();

            AcrylicTintOpacitySettingSlider.Value = acrylicSettings.TintOpacity * 100;
            AcrylicTintLuminosityOpacitySettingSlider.Value = acrylicSettings.TintLuminosityOpacity * 100;

            CloseBackgroundImageButton.IsChecked = BackgroundSettingsService.Instance.BackgroundOpacity == 0;
        }

        private void OnLoaded_SyncThemeSelection()
        {
            var current = ThemeService.Load(); // System/Light/Dark
            var tag = current switch
            {
                AppThemeOption.Light => "Light",
                AppThemeOption.Dark => "Dark",
                _ => "Default"
            };
            SelectComboItemByTag(tag);
        }

        private void CreateEngineItem(UnrealEngineInfo engine)
        {
            var border = new Border
            {
                BorderBrush = new SolidColorBrush(Colors.LightGray),
                CornerRadius = new CornerRadius(4.0),
                Margin = new Thickness(2.0),
                Padding = new Thickness(15.0, 10.0, 15.0, 10.0)
            };

            // border.Background = new AcrylicBrush()
            // {
            //     TintColor = (Color)Application.Current.Resources["SystemRevealChromeMediumColor"],
            //     TintOpacity = 0.5
            // };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 引擎信息
            var infoPanel = new StackPanel { Spacing = 5 };

            var nameText = new TextBlock
            {
                Text = engine.DisplayName,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 16
            };
            infoPanel.Children.Add(nameText);

            var pathText = new TextBlock
            {
                Text = engine.EnginePath,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 128, 128, 128)), // Gray
                FontSize = 12
            };
            infoPanel.Children.Add(pathText);

            var detailsPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 15 };

            var versionText = new TextBlock
            {
                Text = !string.IsNullOrEmpty(engine.FullVersion) ? $"版本: {engine.FullVersion}" : $"版本: {engine.Version ?? "未知"}",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 128, 128, 128)), // Gray
                FontSize = 12
            };
            detailsPanel.Children.Add(versionText);

            // 如果有构建信息，显示更详细的信息
            if (engine.BuildVersionInfo != null)
            {
                var changelistText = new TextBlock
                {
                    Text = $"CL: {engine.BuildVersionInfo.Changelist}",
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 128, 128, 128)), // Gray
                    FontSize = 12
                };
                detailsPanel.Children.Add(changelistText);
            }

            var statusColor = engine.IsValid
                ? Color.FromArgb(255, 0, 128, 0)
                : // Green
                Color.FromArgb(255, 255, 0, 0); // Red

            var statusText = new TextBlock
            {
                Text = engine.StatusText,
                Foreground = new SolidColorBrush(statusColor),
                FontSize = 12
            };
            detailsPanel.Children.Add(statusText);

            infoPanel.Children.Add(detailsPanel);
            Grid.SetColumn(infoPanel, 0);
            grid.Children.Add(infoPanel);

            // 状态指示器
            var indicator = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = new SolidColorBrush(statusColor),
                Margin = new Thickness(10.0, 0, 10.0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(indicator, 1);
            grid.Children.Add(indicator);

            // 操作按钮
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                VerticalAlignment = VerticalAlignment.Center
            };

            var editButton = new Button
            {
                Content = "编辑",
                FontSize = 12,
                Padding = new Thickness(12.0, 6.0, 12.0, 6.0),
                Tag = engine
            };
            editButton.Click += EditEngine_Click;
            buttonPanel.Children.Add(editButton);

            var deleteButton = new Button
            {
                Content = "删除",
                FontSize = 12,
                Padding = new Thickness(12.0, 6.0, 12.0, 6.0),
                Tag = engine
            };
            deleteButton.Click += DeleteEngine_Click;
            buttonPanel.Children.Add(deleteButton);

            Grid.SetColumn(buttonPanel, 2);
            grid.Children.Add(buttonPanel);

            border.Child = grid;
            EnginesStackPanel.Children.Add(border);
        }

        private async void AddEngine_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new AddEngineDialog();
            dialog.XamlRoot = this.XamlRoot;

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(dialog.EngineDisplayName))
                    {
                        await ShowErrorDialog("添加引擎失败", "请输入显示名称");
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(dialog.EnginePath))
                    {
                        await ShowErrorDialog("添加引擎失败", "请选择引擎路径");
                        return;
                    }

                    await _engineManager.AddEngine(dialog.EngineDisplayName, dialog.EnginePath);
                    await LoadEngines();
                }
                catch (Exception ex)
                {
                    await ShowErrorDialog("添加引擎失败", ex.Message);
                }
            }
        }

        private async void EditEngine_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is UnrealEngineInfo engine)
            {
                var dialog = new AddEngineDialog(engine);
                dialog.XamlRoot = this.XamlRoot;

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    try
                    {
                        engine.DisplayName = dialog.EngineDisplayName;
                        engine.EnginePath = dialog.EnginePath;
                        await _engineManager.UpdateEngine(engine);
                        await LoadEngines();
                    }
                    catch (Exception ex)
                    {
                        await ShowErrorDialog("更新引擎失败", ex.Message);
                    }
                }
            }
        }

        private async void DeleteEngine_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is UnrealEngineInfo engine)
            {
                var dialog = new ContentDialog
                {
                    Title = "确认删除",
                    Content = $"确定要删除引擎 \"{engine.DisplayName}\" 吗？",
                    PrimaryButtonText = "删除",
                    CloseButtonText = "取消",
                    XamlRoot = this.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    try
                    {
                        await _engineManager.RemoveEngine(engine);
                        await LoadEngines();
                    }
                    catch (Exception ex)
                    {
                        await ShowErrorDialog("删除引擎失败", ex.Message);
                    }
                }
            }
        }

        private async void AutoDetect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _engineManager.AutoDetectEngines();
                await LoadEngines();

                var dialog = new ContentDialog
                {
                    Title = "自动检测完成",
                    Content = "已完成自动检测，请查看引擎列表。",
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("自动检测失败", ex.Message);
            }
        }

        private async void RefreshEngines_Click(object sender, RoutedEventArgs e)
        {
            await LoadEngines();
        }

        private void IdePathTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                var defaultIde = Settings.Get("Default.IDE", "VS");
                var idePathKey = $"IDE.Path.{defaultIde}";
                Settings.Set(idePathKey, textBox.Text);
            }
        }

        private async void AutoDetectIdeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (IdePathDescription != null)
                {
                    IdePathDescription.Text = "正在自动检测IDE路径...";
                }

                var defaultIde = Settings.Get("Default.IDE", "VS");
                var detectedPath = await DetectIdePath(defaultIde);

                if (!string.IsNullOrEmpty(detectedPath) && File.Exists(detectedPath))
                {
                    IdePathTextBox.Text = detectedPath;
                    var ideName = GetIdeDisplayName(defaultIde);
                    await ShowErrorDialog("自动检测完成", $"已找到 {ideName} 的安装路径:\n{detectedPath}");
                }
                else
                {
                    await ShowErrorDialog("自动检测完成", "未找到IDE的安装路径，请手动选择。");
                }
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("自动检测失败", ex.Message);
            }
            finally
            {
                UpdateIdePathUI(); // 恢复描述文本
            }
        }

        private async void BrowseIdePath_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var filePicker = new FileOpenPicker();
                filePicker.SuggestedStartLocation = PickerLocationId.ComputerFolder;

                // 根据当前选择的IDE类型设置特定的文件过滤器
                var defaultIde = Settings.Get("Default.IDE", "VS");
                switch (defaultIde)
                {
                    case "VS":
                        // Visual Studio 可执行文件
                        filePicker.FileTypeFilter.Add(".exe");
                        filePicker.CommitButtonText = "选择 devenv.exe";
                        filePicker.SettingsIdentifier = "VSPathPicker";
                        break;
                    case "RD":
                        // Rider 可执行文件
                        filePicker.FileTypeFilter.Add(".exe");
                        filePicker.CommitButtonText = "选择 rider64.exe";
                        filePicker.SettingsIdentifier = "RiderPathPicker";
                        break;
                    case "VSCode":
                        // VS Code 可执行文件
                        filePicker.FileTypeFilter.Add(".exe");
                        filePicker.CommitButtonText = "选择 Code.exe";
                        filePicker.SettingsIdentifier = "VSCodePathPicker";
                        break;
                    default:
                        filePicker.FileTypeFilter.Add(".exe");
                        filePicker.SettingsIdentifier = "IDEPathPicker";
                        break;
                }

                var window = App.MainWindow;
                if (window != null)
                {
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                    WinRT.Interop.InitializeWithWindow.Initialize(filePicker, hwnd);
                }

                var file = await filePicker.PickSingleFileAsync();
                if (file != null)
                {
                    // 验证选择的文件是否符合当前IDE类型
                    if (ValidateIdeExecutable(file.Path, defaultIde))
                    {
                        IdePathTextBox.Text = file.Path;
                    }
                    else
                    {
                        await ShowErrorDialog("文件选择错误",
                            $"请选择正确的IDE可执行文件。\n\n" +
                            $"当前选择的IDE: {GetIdeDisplayName(defaultIde)}\n" +
                            $"应选择的文件: {GetExpectedExecutableName(defaultIde)}");
                    }
                }
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("选择文件失败", ex.Message);
            }
        }

        private void FontCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_fontInitDone) return; // 忽略初始化阶段触发
            if (FontCombo.SelectedItem is FontOption opt)
            {
                // 仅保存到设置；不改页面字体
                Settings.Set("Console.Font", opt.Name);
            }
        }

        private void DefaultIdeComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem item)
            {
                var ideTag = item.Tag?.ToString() ?? "VS";
                Settings.Set("Default.IDE", ideTag);

                // 更新IDE路径设置的UI显示
                UpdateIdePathUI();
            }
        }

        private async System.Threading.Tasks.Task LoadEngines()
        {
            try
            {
                await _engineManager.LoadEngines();

                // 清空现有项目
                EnginesStackPanel.Children.Clear();

                // 添加每个引擎项目
                foreach (var engine in _engineManager.Engines)
                {
                    CreateEngineItem(engine);
                }
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("加载引擎列表失败", ex.Message);
            }
        }

        private async System.Threading.Tasks.Task ShowErrorDialog(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
        }

        private bool ValidateIdeExecutable(string filePath, string ideType)
        {
            try
            {
                var fileName = System.IO.Path.GetFileName(filePath);

                return ideType switch
                {
                    "VS" => string.Equals(fileName, "devenv.exe", StringComparison.OrdinalIgnoreCase),
                    "RD" => string.Equals(fileName, "rider64.exe", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(fileName, "rider.exe", StringComparison.OrdinalIgnoreCase),
                    "VSCode" => string.Equals(fileName, "code.exe", StringComparison.OrdinalIgnoreCase),
                    _ => true // 对于未知类型，不进行验证
                };
            }
            catch
            {
                return false;
            }
        }

        private string GetExpectedExecutableName(string ideType)
        {
            return ideType switch
            {
                "VS" => "devenv.exe",
                "RD" => "rider64.exe 或 rider.exe",
                "VSCode" => "code.exe",
                _ => "IDE可执行文件"
            };
        }

        private string GetIdeDisplayName(string ideType)
        {
            return ideType switch
            {
                "VS" => "Visual Studio",
                "RD" => "Rider",
                "VSCode" => "Visual Studio Code",
                _ => "IDE"
            };
        }

        private void UpdateIdePathUI()
        {
            var defaultIde = Settings.Get("Default.IDE", "VS");

            switch (defaultIde)
            {
                case "VS":
                    IdePathHeader.Text = "Visual Studio 路径:";
                    IdePathTextBox.PlaceholderText = "例如: C:\\Program Files\\Microsoft Visual Studio\\2022\\Community\\Common7\\IDE\\devenv.exe";
                    IdePathTextBox.Text = Settings.Get("IDE.Path.VS", "");
                    IdePathDescription.Text = "设置 Visual Studio 的可执行文件路径";
                    break;
                case "RD":
                    IdePathHeader.Text = "Rider 路径:";
                    IdePathTextBox.PlaceholderText = "例如: C:\\Program Files\\JetBrains\\JetBrains Rider\\bin\\rider64.exe";
                    IdePathTextBox.Text = Settings.Get("IDE.Path.RD", "");
                    IdePathDescription.Text = "设置 Rider 的可执行文件路径";
                    break;
                case "VSCode":
                    IdePathHeader.Text = "VS Code 路径:";
                    IdePathTextBox.PlaceholderText = "例如: C:\\Users\\{用户名}\\AppData\\Local\\Programs\\Microsoft VS Code\\Code.exe";
                    IdePathTextBox.Text = Settings.Get("IDE.Path.VSCode", "");
                    IdePathDescription.Text = "设置 Visual Studio Code 的可执行文件路径";
                    break;
                default:
                    IdePathHeader.Text = "IDE 路径:";
                    IdePathTextBox.PlaceholderText = "请选择IDE可执行文件路径";
                    IdePathDescription.Text = "根据选择的默认IDE，路径指向对应的可执行文件";
                    break;
            }
        }


        private void LoadFonts()
        {
            // 从设置读取；若无则用 Consolas
            var desiredName = Settings.Get("Console.Font", "Consolas");

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 用 GDI 枚举已安装字体，并合并进去（去重 + 排序）
            try
            {
                using var collection = new InstalledFontCollection();
                var names = collection.Families
                    .Select(ff => ff.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

                foreach (var name in names)
                    if (seen.Add(name))
                        AvailableFonts.Add(new FontOption(name));
            }
            catch
            {
                // 忽略：枚举失败就用上面的 preferred 列表
            }

            // 确保设置中保存的字体在列表里（即使没被枚举到）
            if (seen.Add(desiredName))
                AvailableFonts.Add(new FontOption(desiredName));

            // 只设置下拉框的选中项，不改变页面字体
            FontCombo.SelectedItem = AvailableFonts.FirstOrDefault(f =>
                                         string.Equals(f.Name, desiredName, StringComparison.OrdinalIgnoreCase))
                                     ?? AvailableFonts.FirstOrDefault();

            _fontInitDone = true;
        }

        private void LoadIdeSettings()
        {
            var defaultIde = Settings.Get("Default.IDE", "VS");
            var idePathKey = $"IDE.Path.{defaultIde}";
            var idePath = Settings.Get(idePathKey, "");
            if (!string.IsNullOrEmpty(idePath))
            {
                IdePathTextBox.Text = idePath;
            }
        }

        private void LoadDefaultIdeSetting()
        {
            // 加载已保存的默认 IDE 设置
            var defaultIde = Settings.Get("Default.IDE", "VS");

            // 根据保存的值设置选中项
            switch (defaultIde)
            {
                case "VS":
                    DefaultIdeComboBox.SelectedIndex = 0;
                    break;
                case "RD":
                    DefaultIdeComboBox.SelectedIndex = 1;
                    break;
                case "VSCode":
                    DefaultIdeComboBox.SelectedIndex = 2;
                    break;
                default:
                    DefaultIdeComboBox.SelectedIndex = 3; // None
                    break;
            }
        }

        private async void AutoDetectIdePaths()
        {
            try
            {
                // 检查是否已经设置过IDE路径，如果设置过则跳过自动检测
                var defaultIde = Settings.Get("Default.IDE", "VS");
                var idePathKey = $"IDE.Path.{defaultIde}";
                var existingPath = Settings.Get(idePathKey, "");

                // 如果当前IDE类型已经有设置的路径，则不进行自动检测
                if (!string.IsNullOrEmpty(existingPath) && File.Exists(existingPath))
                {
                    return;
                }

                // 显示正在检测的提示
                if (IdePathDescription != null)
                {
                    IdePathDescription.Text = "正在自动检测IDE路径...";
                }

                // 根据默认IDE类型自动搜索路径
                var detectedPath = await DetectIdePath(defaultIde);
                if (!string.IsNullOrEmpty(detectedPath) && File.Exists(detectedPath))
                {
                    // 保存检测到的路径
                    Settings.Set(idePathKey, detectedPath);

                    // 更新UI
                    if (IdePathTextBox != null)
                    {
                        IdePathTextBox.Text = detectedPath;
                    }

                    if (IdePathDescription != null)
                    {
                        IdePathDescription.Text = $"自动检测到 {GetIdeDisplayName(defaultIde)} 路径";
                    }
                }
                else
                {
                    if (IdePathDescription != null)
                    {
                        IdePathDescription.Text = "自动检测完成，未找到IDE路径，请手动选择";
                    }
                }
            }
            catch (Exception ex)
            {
                // 静默处理自动检测错误，不打扰用户
                System.Diagnostics.Debug.WriteLine($"自动检测IDE路径失败: {ex.Message}");
                if (IdePathDescription != null)
                {
                    IdePathDescription.Text = "根据选择的默认IDE，路径指向对应的可执行文件";
                }
            }
        }

        private async Task<string> DetectIdePath(string ideType)
        {
            return ideType switch
            {
                "VS" => await DetectVisualStudioPath(),
                "RD" => await DetectRiderPath(),
                "VSCode" => await DetectVSCodePath(),
                _ => string.Empty
            };
        }

        private async Task<string> DetectVisualStudioPath()
        {
            // 获取所有驱动器
            var drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed).ToArray();

            // 常见的Visual Studio安装相对路径
            var relativePaths = new[]
            {
                @"Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\devenv.exe",
                @"Program Files\Microsoft Visual Studio\2022\Professional\Common7\IDE\devenv.exe",
                @"Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\devenv.exe",
                @"Program Files (x86)\Microsoft Visual Studio\2019\Community\Common7\IDE\devenv.exe",
                @"Program Files (x86)\Microsoft Visual Studio\2019\Professional\Common7\IDE\devenv.exe",
                @"Program Files (x86)\Microsoft Visual Studio\2019\Enterprise\Common7\IDE\devenv.exe",
                @"Program Files (x86)\Microsoft Visual Studio 14.0\Common7\IDE\devenv.exe", // VS 2015
            };

            // 遍历所有驱动器和路径组合
            foreach (var drive in drives)
            {
                foreach (var relativePath in relativePaths)
                {
                    var fullPath = System.IO.Path.Combine(drive.Name, relativePath);
                    if (File.Exists(fullPath))
                    {
                        return fullPath;
                    }
                }
            }

            return string.Empty;
        }

        private async Task<string> DetectRiderPath()
        {
            // 获取所有驱动器
            var drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed).ToArray();

            // 常见的Rider安装相对路径
            var relativePaths = new[]
            {
                @"Program Files\JetBrains\JetBrains Rider\bin\rider64.exe",
                @"Program Files (x86)\JetBrains\JetBrains Rider\bin\rider64.exe",
                @"Program Files\JetBrains\Rider\bin\rider64.exe",
                @"Program Files (x86)\JetBrains\Rider\bin\rider64.exe",
            };

            // 遍历所有驱动器和路径组合
            foreach (var drive in drives)
            {
                foreach (var relativePath in relativePaths)
                {
                    var fullPath = System.IO.Path.Combine(drive.Name, relativePath);
                    if (File.Exists(fullPath))
                    {
                        return fullPath;
                    }
                }
            }

            return string.Empty;
        }

        private async Task<string> DetectVSCodePath()
        {
            // 获取所有驱动器
            var drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed).ToArray();

            // 常见的VS Code安装相对路径
            var username = Environment.UserName;
            var relativePaths = new[]
            {
                $@"Users\{username}\AppData\Local\Programs\Microsoft VS Code\Code.exe",
                @"Program Files\Microsoft VS Code\Code.exe",
                @"Program Files (x86)\Microsoft VS Code\Code.exe",
            };

            // 遍历所有驱动器和路径组合
            foreach (var drive in drives)
            {
                foreach (var relativePath in relativePaths)
                {
                    var fullPath = System.IO.Path.Combine(drive.Name, relativePath);
                    if (File.Exists(fullPath))
                    {
                        return fullPath;
                    }
                }
            }

            return string.Empty;
        }

        private void OpenConfigFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 获取配置文件目录路径
                var configDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DreamUnrealManager");

                // 确保目录存在
                Directory.CreateDirectory(configDir);

                // 使用资源管理器打开目录
                Process.Start(new ProcessStartInfo()
                {
                    FileName = configDir,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (Exception ex)
            {
                // 如果发生错误，显示一个内容对话框
                var dialog = new ContentDialog
                {
                    Title = "错误",
                    Content = $"无法打开配置文件目录: {ex.Message}",
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };

                _ = dialog.ShowAsync();
            }
        }

        private void ThemeMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ThemeModeComboBox?.SelectedItem is not ComboBoxItem item) return;
            var tag = item.Tag as string;
            if (string.IsNullOrWhiteSpace(tag)) return;

            var opt = tag switch
            {
                "Light" => AppThemeOption.Light,
                "Dark" => AppThemeOption.Dark,
                _ => AppThemeOption.System, // "Default"
            };

            // 保存并全局应用
            ThemeService.Save(opt);
            ThemeService.ApplyToWindow(App.MainWindow, opt);
        }

        private void SelectComboItemByTag(string tag)
        {
            if (ThemeModeComboBox is null) return;
            foreach (var obj in ThemeModeComboBox.Items)
            {
                if (obj is ComboBoxItem cbi &&
                    string.Equals(cbi.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
                {
                    ThemeModeComboBox.SelectedItem = cbi;
                    break;
                }
            }
        }

        private void AcrylicTintOpacitySettingSlider_OnValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            acrylicSettings.TintOpacity = e.NewValue / 100.0f;
        }

        private void AcrylicTintLuminosityOpacitySettingSlider_OnValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            acrylicSettings.TintLuminosityOpacity = e.NewValue / 100.0f;
        }

        private void CloseBackgroundImageButton_OnChanged(object sender, RoutedEventArgs e)
        {
            if (CloseBackgroundImageButton?.IsChecked ?? false)
            {
                BackgroundSettingsService.Instance.BackgroundOpacity = 0.0f;
            }
            else
            {
                BackgroundSettingsService.Instance.BackgroundOpacity = 0.5f;
            }
        }
    }

    public sealed class FontOption
    {
        public string Name
        {
            get;
        }

        public FontFamily FontFamily
        {
            get;
        }

        public FontOption(string name)
        {
            Name = name;
            FontFamily = new FontFamily(name);
        }

        public override string ToString() => Name;
    }

    // 添加引擎对话框
    public sealed class AddEngineDialog : ContentDialog
    {
        private TextBox _displayNameTextBox;
        private TextBox _pathTextBox;
        private UnrealEngineInfo _editingEngine;

        public string EngineDisplayName => _displayNameTextBox?.Text ?? "";
        public string EnginePath => _pathTextBox?.Text ?? "";

        public AddEngineDialog(UnrealEngineInfo editingEngine = null)
        {
            _editingEngine = editingEngine;

            Title = editingEngine == null ? "添加引擎" : "编辑引擎";
            PrimaryButtonText = editingEngine == null ? "添加" : "保存";
            CloseButtonText = "取消";

            CreateContent();
        }

        private void CreateContent()
        {
            var panel = new StackPanel { Spacing = 15 };

            // 显示名称
            panel.Children.Add(new TextBlock { Text = "显示名称:" });
            _displayNameTextBox = new TextBox
            {
                PlaceholderText = "例如: Unreal Engine 5.3",
                Text = _editingEngine?.DisplayName ?? ""
            };
            panel.Children.Add(_displayNameTextBox);

            // 引擎路径
            panel.Children.Add(new TextBlock { Text = "引擎路径:" });
            var pathPanel = new Grid();
            pathPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pathPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _pathTextBox = new TextBox
            {
                PlaceholderText = "例如: C:\\Program Files\\Epic Games\\UE_5.3",
                Text = _editingEngine?.EnginePath ?? ""
            };
            Grid.SetColumn(_pathTextBox, 0);
            pathPanel.Children.Add(_pathTextBox);

            var browseButton = new Button
            {
                Content = "浏览...",
                Margin = new Thickness(10.0, 0, 0, 0)
            };
            browseButton.Click += BrowseButton_Click;
            Grid.SetColumn(browseButton, 1);
            pathPanel.Children.Add(browseButton);

            panel.Children.Add(pathPanel);

            Content = panel;
        }

        private async void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var folderPicker = new FolderPicker();
                folderPicker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
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
                    _pathTextBox.Text = folder.Path;

                    // 如果显示名称为空，尝试从路径自动生成
                    if (string.IsNullOrWhiteSpace(_displayNameTextBox.Text))
                    {
                        var folderName = System.IO.Path.GetFileName(folder.Path);
                        if (folderName.StartsWith("UE_"))
                        {
                            var version = folderName.Replace("UE_", "").Replace("_", ".");
                            _displayNameTextBox.Text = $"Unreal Engine {version}";
                        }
                        else
                        {
                            _displayNameTextBox.Text = folderName;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 忽略选择文件夹失败的情况
                System.Diagnostics.Debug.WriteLine($"Failed to pick folder: {ex.Message}");
            }
        }
    }
}