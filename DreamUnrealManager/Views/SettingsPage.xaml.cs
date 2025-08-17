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
using DreamUnrealManager.Contracts.Services;
using DreamUnrealManager.Helpers;
using DreamUnrealManager.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml.Controls.Primitives;
using Path = ABI.Microsoft.UI.Xaml.Shapes.Path;

namespace DreamUnrealManager.Views
{
    public sealed partial class SettingsPage : Page
    {
        public SettingsViewModel ViewModel
        {
            get;
        }

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
            ViewModel = App.GetService<SettingsViewModel>();

            this.InitializeComponent();

            _engineManager = EngineManagerService.Instance;

            Loaded += SettingsPage_Loaded;
        }


        private async void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadFonts();
            LoadIdeSettings();
            LoadDefaultIdeSetting();
            LoadRiderLaunchMethod();
            UpdateIdePathUI();

            AutoDetectIdePaths();
            OnLoaded_SyncThemeSelection();

            AcrylicTintOpacitySettingSlider.Value = acrylicSettings.TintOpacity * 100;
            AcrylicTintLuminosityOpacitySettingSlider.Value = acrylicSettings.TintLuminosityOpacity * 100;
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

        private void IdePathTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                var defaultIde = SettingsService.Get("Default.IDE", "VS");
                var idePathKey = $"IDE.Path.{defaultIde}";
                SettingsService.Set(idePathKey, textBox.Text);
            }
        }

        private async void AutoDetectIdeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var defaultIde = SettingsService.Get("Default.IDE", "VS");
                var detectedPath = await DetectIdePath(defaultIde);

                if (!string.IsNullOrEmpty(detectedPath) && File.Exists(detectedPath))
                {
                    IdePathTextBox.Text = detectedPath;
                    var ideName = GetIdeDisplayName(defaultIde);
                    await App.GetService<IDialogService>().ShowErrorDialog("自动检测完成", $"已找到 {ideName} 的安装路径:\n{detectedPath}");
                }
                else
                {
                    await App.GetService<IDialogService>().ShowErrorDialog("自动检测完成", "未找到IDE的安装路径，请手动选择。");
                }
            }
            catch (Exception ex)
            {
                await App.GetService<IDialogService>().ShowErrorDialog("自动检测失败", ex.Message);
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
                var defaultIde = SettingsService.Get("Default.IDE", "VS");
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
                        await App.GetService<IDialogService>().ShowErrorDialog("文件选择错误",
                            $"请选择正确的IDE可执行文件。\n\n" +
                            $"当前选择的IDE: {GetIdeDisplayName(defaultIde)}\n" +
                            $"应选择的文件: {GetExpectedExecutableName(defaultIde)}");
                    }
                }
            }
            catch (Exception ex)
            {
                await App.GetService<IDialogService>().ShowErrorDialog("选择文件失败", ex.Message);
            }
        }

        private void FontCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_fontInitDone) return; // 忽略初始化阶段触发
            if (FontCombo.SelectedItem is FontOption opt)
            {
                // 仅保存到设置；不改页面字体
                SettingsService.Set("Console.Font", opt.Name);
            }
        }

        private void DefaultIdeComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem item)
            {
                var ideTag = item.Tag?.ToString() ?? "VS";
                SettingsService.Set("Default.IDE", ideTag);

                // 更新IDE路径设置的UI显示
                UpdateIdePathUI();
            }
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
            var defaultIde = SettingsService.Get("Default.IDE", "VS");

            switch (defaultIde)
            {
                case "VS":
                    IdePathTextBox.PlaceholderText = "例如: C:\\Program Files\\Microsoft Visual Studio\\2022\\Community\\Common7\\IDE\\devenv.exe";
                    IdePathTextBox.Text = SettingsService.Get("IDE.Path.VS", "");
                    RiderLauncherMethodSettingCard.Visibility = Visibility.Collapsed;
                    IdePathSettingCard.Header = "Visual Studio 可执行文件路径";
                    break;
                case "RD":
                    IdePathTextBox.PlaceholderText = "例如: C:\\Program Files\\JetBrains\\JetBrains Rider\\bin\\rider64.exe";
                    IdePathTextBox.Text = SettingsService.Get("IDE.Path.RD", "");
                    RiderLauncherMethodSettingCard.Visibility = Visibility.Visible;
                    IdePathSettingCard.Header = "Rider 可执行文件路径";
                    break;
                case "VSCode":
                    IdePathTextBox.PlaceholderText = "例如: C:\\Users\\{用户名}\\AppData\\Local\\Programs\\Microsoft VS Code\\Code.exe";
                    IdePathTextBox.Text = SettingsService.Get("IDE.Path.VSCode", "");
                    RiderLauncherMethodSettingCard.Visibility = Visibility.Collapsed;
                    IdePathSettingCard.Header = "VSCode 路径";
                    break;
                default:
                    IdePathTextBox.PlaceholderText = "请选择IDE可执行文件路径";
                    break;
            }
        }


        private void LoadFonts()
        {
            // 从设置读取；若无则用 Consolas
            var desiredName = SettingsService.Get("Console.Font", "Consolas");

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
            var defaultIde = SettingsService.Get("Default.IDE", "VS");
            var idePathKey = $"IDE.Path.{defaultIde}";
            var idePath = SettingsService.Get(idePathKey, "");
            if (!string.IsNullOrEmpty(idePath))
            {
                IdePathTextBox.Text = idePath;
            }
        }

        private void LoadDefaultIdeSetting()
        {
            // 加载已保存的默认 IDE 设置
            var defaultIde = SettingsService.Get("Default.IDE", "VS");

            // 根据保存的值设置选中项
            switch (defaultIde)
            {
                case "VS":
                    DefaultIdeComboBox.SelectedIndex = 0;
                    RiderLauncherMethodSettingCard.Visibility = Visibility.Collapsed;
                    break;
                case "RD":
                    DefaultIdeComboBox.SelectedIndex = 1;
                    RiderLauncherMethodSettingCard.Visibility = Visibility.Visible;
                    break;
                case "VSCode":
                    DefaultIdeComboBox.SelectedIndex = 2;
                    RiderLauncherMethodSettingCard.Visibility = Visibility.Collapsed;
                    break;
                default:
                    DefaultIdeComboBox.SelectedIndex = 3; // None
                    break;
            }
        }

        private void LoadRiderLaunchMethod()
        {
            var method = SettingsService.Get("IDE.Rider.LaunchMethod", "SOLUTION");
            switch (method)
            {
                case "SOLUTION":
                {
                    RiderLauncherMethodComboBox.SelectedIndex = 0;
                    break;
                }
                case "UPROJECT":
                {
                    RiderLauncherMethodComboBox.SelectedIndex = 1;
                    break;
                }
                default:
                {
                    RiderLauncherMethodComboBox.SelectedIndex = 0;
                    break;
                }
            }
        }

        private async void AutoDetectIdePaths()
        {
            try
            {
                // 检查是否已经设置过IDE路径，如果设置过则跳过自动检测
                var defaultIde = SettingsService.Get("Default.IDE", "VS");
                var idePathKey = $"IDE.Path.{defaultIde}";
                var existingPath = SettingsService.Get(idePathKey, "");

                // 如果当前IDE类型已经有设置的路径，则不进行自动检测
                if (!string.IsNullOrEmpty(existingPath) && File.Exists(existingPath))
                {
                    return;
                }

                // 根据默认IDE类型自动搜索路径
                var detectedPath = await DetectIdePath(defaultIde);
                if (!string.IsNullOrEmpty(detectedPath) && File.Exists(detectedPath))
                {
                    // 保存检测到的路径
                    SettingsService.Set(idePathKey, detectedPath);

                    // 更新UI
                    if (IdePathTextBox != null)
                    {
                        IdePathTextBox.Text = detectedPath;
                    }
                }
            }
            catch (Exception ex)
            {
                // 静默处理自动检测错误，不打扰用户
                System.Diagnostics.Debug.WriteLine($"自动检测IDE路径失败: {ex.Message}");
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

        private void RiderLauncherMethodComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var tag = ((ComboBoxItem)RiderLauncherMethodComboBox.SelectedItem).Tag as string;
            SettingsService.Set("IDE.Rider.LaunchMethod", tag);
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