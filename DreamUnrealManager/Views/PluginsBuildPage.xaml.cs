using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DreamUnrealManager.Services;
using DreamUnrealManager.Models;
using Windows.UI;
using Microsoft.UI;

namespace DreamUnrealManager.Views
{
    public sealed partial class PluginsBuildPage : Page
    {
        private CancellationTokenSource _buildCancellationTokenSource;
        private bool _isBuildInProgress = false;
        private Process _currentProcess;
        private readonly EngineManagerService _engineManager;
        private PluginInfo _currentPluginInfo;
        private List<CheckBox> _engineCheckBoxes = new List<CheckBox>();

        // 批量构建相关字段
        private List<UnrealEngineInfo> _selectedEnginesForBatch;
        private int _currentBatchIndex;
        private List<string> _batchBuildResults;

        // 错误和警告跟踪
        private List<BuildIssue> _buildIssues = new List<BuildIssue>();
        private int _errorCount = 0;
        private int _warningCount = 0;
        private UnrealEngineInfo _currentBuildingEngine;

        public PluginsBuildPage()
        {
            this.InitializeComponent();
            _engineManager = EngineManagerService.Instance;
            this.Loaded += PluginsBuildPage_Loaded;
        }

        #region 构建问题类定义

        public enum BuildIssueType
        {
            Error,
            Warning
        }

        public class BuildIssue
        {
            public BuildIssueType Type
            {
                get;
                set;
            }

            public string Message
            {
                get;
                set;
            }

            public string Engine
            {
                get;
                set;
            }

            public DateTime Timestamp
            {
                get;
                set;
            }

            public string SourceFile
            {
                get;
                set;
            }

            public int LineNumber
            {
                get;
                set;
            }
        }

        #endregion

        private async void PluginsBuildPage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await InitializePage();

                // 初始化导航状态为就绪
                UpdateNavigationStatus(false, 0, "就绪");

                // 页面加载完成后显示欢迎消息
                WriteToTerminal("插件构建页面已就绪", TerminalMessageType.Success);

                TerminalOutput.FontFamily = new FontFamily(SettingsService.Get("Console.Font", "Consolas"));

                WriteToTerminal("Powered by Dream Moon. © 2025", TerminalMessageType.Info);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"页面加载失败: {ex.Message}");
                UpdateNavigationStatus(false, 0, "初始化失败");
            }
        }


        private async Task InitializePage()
        {
            try
            {
                var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                OutputPathTextBox.Text = Path.Combine(documentsPath, "PluginsBuilded");

                await LoadEngineVersions();

                // 确保页面完全加载后再显示初始化消息
                if (this.IsLoaded)
                {
                    WriteToTerminal("页面初始化完成", TerminalMessageType.Success);
                }

                UpdateIssuesCounts();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"初始化错误: {ex.Message}");
                // 初始化时的错误不显示在终端中
            }
        }


        private async Task LoadEngineVersions()
        {
            try
            {
                await _engineManager.LoadEngines();

                EngineVersionComboBox.Items.Clear();
                var validEngines = _engineManager.GetValidEngines();

                if (validEngines != null)
                {
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
                        EngineVersionComboBox.Items.Add(item);
                    }
                }

                if (EngineVersionComboBox.Items.Count > 0)
                    EngineVersionComboBox.SelectedIndex = 0;

                LoadBatchEnginesList();

                // 只有在页面完全加载后才显示加载消息
                if (this.IsLoaded)
                {
                    WriteToTerminal($"已加载 {validEngines?.Count() ?? 0} 个有效引擎版本", TerminalMessageType.Info);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载引擎版本失败: {ex.Message}");
                // 初始化时的错误不显示在终端中
            }
        }


        private void LoadBatchEnginesList()
        {
            try
            {
                EngineVersionsCheckList.Children.Clear();
                _engineCheckBoxes.Clear();

                var validEngines = _engineManager.GetValidEngines();
                if (validEngines != null)
                {
                    foreach (var engine in validEngines)
                    {
                        var displayText = $"{engine.DisplayName}";
                        if (!string.IsNullOrEmpty(engine.FullVersion))
                            displayText += $" ({engine.FullVersion})";
                        else if (!string.IsNullOrEmpty(engine.Version))
                            displayText += $" ({engine.Version})";

                        var checkBox = new CheckBox
                        {
                            Content = displayText,
                            Tag = engine,
                            Margin = new Thickness(0, 2, 0, 2)
                        };

                        _engineCheckBoxes.Add(checkBox);
                        EngineVersionsCheckList.Children.Add(checkBox);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载批量构建列表失败: {ex.Message}");
                // 初始化时的错误不显示在终端中
            }
        }


        #region 终端输出方法

        private enum TerminalMessageType
        {
            Info,
            Success,
            Warning,
            Error,
            Command
        }

        private void OnConsoleOutputReceived(string line)
        {
            WriteToTerminal(line, TerminalMessageType.Info);
        }

        private void WriteToTerminal(string message, TerminalMessageType messageType = TerminalMessageType.Info)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    // 如果页面还没加载完成，直接返回
                    if (!this.IsLoaded || TerminalOutput == null)
                    {
                        return;
                    }

                    // 过滤掉一些噪音信息
                    if (ShouldFilterMessage(message))
                    {
                        return;
                    }

                    // 检查是否包含构建失败的关键字
                    if (message.Contains("BUILD FAILED", StringComparison.OrdinalIgnoreCase))
                    {
                        messageType = TerminalMessageType.Error;
                        // 如果正在构建过程中，更新状态
                        if (_isBuildInProgress)
                        {
                            UpdateCurrentEngineInfo(_currentBuildingEngine, "构建失败");
                            UpdateNavigationStatus(true, 1, "构建失败");
                            UpdateBuildProgressBarState(true);

                            // 取消当前构建过程
                            _buildCancellationTokenSource?.Cancel();
                        }
                    }

                    var timestamp = DateTime.Now.ToString("HH:mm:ss");
                    var paragraph = new Paragraph();

                    // 添加时间戳
                    var timeRun = new Run { Text = $"[{timestamp}] " };
                    timeRun.Foreground = new SolidColorBrush(Colors.Gray);
                    paragraph.Inlines.Add(timeRun);

                    // 根据消息类型设置颜色
                    var messageRun = new Run { Text = message };
                    switch (messageType)
                    {
                        case TerminalMessageType.Success:
                            messageRun.Foreground = new SolidColorBrush(Colors.LightGreen);
                            break;
                        case TerminalMessageType.Warning:
                            messageRun.Foreground = new SolidColorBrush(Colors.Yellow);
                            break;
                        case TerminalMessageType.Error:
                            messageRun.Foreground = new SolidColorBrush(Colors.LightCoral);
                            break;
                        case TerminalMessageType.Command:
                            messageRun.Foreground = new SolidColorBrush(Colors.Cyan);
                            break;
                        default:
                            messageRun.Foreground = new SolidColorBrush(Colors.LightGray);
                            break;
                    }

                    paragraph.Inlines.Add(messageRun);

                    TerminalOutput.Blocks.Add(paragraph);

                    // 检查是否包含错误或警告
                    CheckForIssues(message, messageType);

                    // 自动滚动到底部，确保 ScrollViewer 存在
                    if (TerminalScrollViewer != null)
                    {
                        TerminalScrollViewer.ScrollToVerticalOffset(TerminalScrollViewer.ExtentHeight);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"WriteToTerminal error: {ex.Message}");
                }
            });
        }


        // 改进 CheckForIssues 方法，添加更智能的错误检测
        private void CheckForIssues(string message, TerminalMessageType messageType)
        {
            try
            {
                var lowerMessage = message.ToLower();

                // 首先检查是否是已知的误报
                if (IsFalseError(message))
                {
                    return;
                }

                var isError = IsRealError(message, messageType);
                var isWarning = IsRealWarning(message, messageType);

                if (isError || isWarning)
                {
                    var issue = new BuildIssue
                    {
                        Type = isError ? BuildIssueType.Error : BuildIssueType.Warning,
                        Message = message,
                        Engine = _currentBuildingEngine?.DisplayName ?? "未知引擎",
                        Timestamp = DateTime.Now,
                        SourceFile = ExtractSourceFile(message),
                        LineNumber = ExtractLineNumber(message)
                    };

                    _buildIssues.Add(issue);

                    if (isError)
                        _errorCount++;
                    else
                        _warningCount++;

                    AddIssueToList(issue);
                    UpdateIssuesCounts();
                    ShowIssuesCard();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CheckForIssues error: {ex.Message}");
            }
        }


        private string ExtractSourceFile(string message)
        {
            try
            {
                // 尝试提取类似 "C:\path\file.cpp(123)" 的文件路径
                var match = System.Text.RegularExpressions.Regex.Match(message, @"([A-Za-z]:[^:]*\.(cpp|h|cs|hpp|c))\((\d+)\)");
                if (match.Success)
                {
                    return System.IO.Path.GetFileName(match.Groups[1].Value);
                }

                // 尝试提取其他格式的文件路径
                match = System.Text.RegularExpressions.Regex.Match(message, @"([A-Za-z]:[^:]*\.(cpp|h|cs|hpp|c))");
                if (match.Success)
                {
                    return System.IO.Path.GetFileName(match.Groups[1].Value);
                }

                return "";
            }
            catch
            {
                return "";
            }
        }

        private int ExtractLineNumber(string message)
        {
            try
            {
                var match = System.Text.RegularExpressions.Regex.Match(message, @"\((\d+)\)");
                if (match.Success && int.TryParse(match.Groups[1].Value, out int lineNumber))
                {
                    return lineNumber;
                }

                return 0;
            }
            catch
            {
                return 0;
            }
        }

        private void AddIssueToList(BuildIssue issue)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    var border = new Border
                    {
                        Background = issue.Type == BuildIssueType.Error
                            ? new SolidColorBrush(Color.FromArgb(30, 255, 0, 0)) // 红色半透明
                            : new SolidColorBrush(Color.FromArgb(30, 255, 255, 0)), // 黄色半透明
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(10),
                        Margin = new Thickness(0, 1, 0, 1)
                    };

                    var grid = new Grid();
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    // 错误/警告图标
                    var icon = new FontIcon
                    {
                        Glyph = issue.Type == BuildIssueType.Error ? "\uE783" : "\uE7BA",
                        FontSize = 14,
                        Foreground = issue.Type == BuildIssueType.Error
                            ? new SolidColorBrush(Colors.Red)
                            : new SolidColorBrush(Colors.Orange),
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(0, 0, 8, 0)
                    };
                    Grid.SetColumn(icon, 0);
                    grid.Children.Add(icon);

                    // 消息内容
                    var contentPanel = new StackPanel();

                    var messageText = new TextBlock
                    {
                        Text = issue.Message,
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 13,
                        Foreground = issue.Type == BuildIssueType.Error
                            ? new SolidColorBrush(Colors.Red)
                            : new SolidColorBrush(Colors.Orange)
                    };
                    contentPanel.Children.Add(messageText);

                    var detailsPanel = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 10,
                        Margin = new Thickness(0, 5, 0, 0)
                    };

                    if (!string.IsNullOrEmpty(issue.SourceFile))
                    {
                        var fileText = new TextBlock
                        {
                            Text = issue.LineNumber > 0 ? $"{issue.SourceFile}:{issue.LineNumber}" : issue.SourceFile,
                            FontSize = 11,
                            Foreground = new SolidColorBrush(Colors.Gray),
                            FontFamily = new FontFamily("Consolas")
                        };
                        detailsPanel.Children.Add(fileText);
                    }

                    var engineText = new TextBlock
                    {
                        Text = issue.Engine,
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Colors.Gray)
                    };
                    detailsPanel.Children.Add(engineText);

                    contentPanel.Children.Add(detailsPanel);
                    Grid.SetColumn(contentPanel, 1);
                    grid.Children.Add(contentPanel);

                    // 时间戳
                    var timeText = new TextBlock
                    {
                        Text = issue.Timestamp.ToString("HH:mm:ss"),
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Colors.Gray),
                        VerticalAlignment = VerticalAlignment.Top
                    };
                    Grid.SetColumn(timeText, 2);
                    grid.Children.Add(timeText);

                    border.Child = grid;

                    // 如果这是第一个问题，移除占位符文本
                    if (IssuesListPanel.Children.Count == 1 &&
                        IssuesListPanel.Children[0] is TextBlock placeholder &&
                        placeholder.Text == "暂无错误或警告")
                    {
                        IssuesListPanel.Children.Clear();
                    }

                    IssuesListPanel.Children.Insert(0, border); // 最新的问题显示在顶部

                    // 自动滚动到顶部显示最新问题
                    IssuesScrollViewer.ScrollToVerticalOffset(0);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"AddIssueToList error: {ex.Message}");
                }
            });
        }

        private void UpdateIssuesCounts()
        {
            // 确保在 UI 线程上执行，并检查控件是否已初始化
            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    if (ErrorCountText != null && WarningCountText != null)
                    {
                        ErrorCountText.Text = _errorCount.ToString();
                        WarningCountText.Text = _warningCount.ToString();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"UpdateIssuesCounts error: {ex.Message}");
                }
            });
        }


        private void ShowIssuesCard()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                IssuesCard.Visibility = Visibility.Visible;
            });
        }

        private void UpdateCurrentEngineInfo(UnrealEngineInfo engine, string step = "准备中...")
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    _currentBuildingEngine = engine;
                    BuildStatusCard.Visibility = Visibility.Visible;

                    CurrentEngineNameText.Text = $"当前引擎: {engine?.DisplayName ?? "未知"}";
                    CurrentEngineVersionText.Text = engine != null
                        ? $"版本: {engine.FullVersion ?? engine.Version ?? "未知"} | 路径: {Path.GetFileName(engine.EnginePath)}"
                        : "";
                    CurrentBuildStepText.Text = step;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"UpdateCurrentEngineInfo error: {ex.Message}");
                }
            });
        }

        private void UpdateBuildProgress(int percentage, string step = null)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    CurrentBuildProgressBar.Value = percentage;
                    BuildProgressText.Text = $"{percentage}%";

                    if (!string.IsNullOrEmpty(step))
                    {
                        CurrentBuildStepText.Text = step;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"UpdateBuildProgress error: {ex.Message}");
                }
            });
        }

        private void UpdateBuildProgressBarState(bool bIsError)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    CurrentBuildProgressBar.ShowError = bIsError;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"UpdateBuildProgress error: {ex.Message}");
                }
            });
        }

        private void ClearTerminal()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    TerminalOutput.Blocks.Clear();
                    var paragraph = new Paragraph();
                    var run = new Run { Text = "终端已清空" };
                    run.Foreground = new SolidColorBrush(Colors.Green);
                    paragraph.Inlines.Add(run);
                    TerminalOutput.Blocks.Add(paragraph);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ClearTerminal error: {ex.Message}");
                }
            });
        }

        #endregion

        #region 事件处理

        private void BuildModeRadio_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                if (SingleBuildRadio?.IsChecked == true)
                {
                    if (SingleBuildPanel != null) SingleBuildPanel.Visibility = Visibility.Visible;
                    if (BatchBuildPanel != null) BatchBuildPanel.Visibility = Visibility.Collapsed;
                    if (BatchProgressPanel != null) BatchProgressPanel.Visibility = Visibility.Collapsed;
                }
                else if (BatchBuildRadio?.IsChecked == true)
                {
                    if (SingleBuildPanel != null) SingleBuildPanel.Visibility = Visibility.Collapsed;
                    if (BatchBuildPanel != null) BatchBuildPanel.Visibility = Visibility.Visible;
                    if (BatchProgressPanel != null) BatchProgressPanel.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                WriteToTerminal($"切换构建模式时出错: {ex.Message}", TerminalMessageType.Error);
            }
        }

        private void SelectAllEngines_Click(object sender, RoutedEventArgs e)
        {
            foreach (var checkBox in _engineCheckBoxes)
                checkBox.IsChecked = true;
        }

        private void DeselectAllEngines_Click(object sender, RoutedEventArgs e)
        {
            foreach (var checkBox in _engineCheckBoxes)
                checkBox.IsChecked = false;
        }

        private void SelectUE5Only_Click(object sender, RoutedEventArgs e)
        {
            foreach (var checkBox in _engineCheckBoxes)
            {
                var engine = checkBox.Tag as UnrealEngineInfo;
                checkBox.IsChecked = engine?.BuildVersionInfo?.MajorVersion == 5;
            }
        }

        private void ClearIssues_Click(object sender, RoutedEventArgs e)
        {
            _buildIssues.Clear();
            _errorCount = 0;
            _warningCount = 0;

            IssuesListPanel.Children.Clear();
            var placeholder = new TextBlock
            {
                Text = "暂无错误或警告",
                Foreground = new SolidColorBrush(Colors.Gray),
                FontStyle = Windows.UI.Text.FontStyle.Italic,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 20, 0, 20)
            };
            IssuesListPanel.Children.Add(placeholder);

            UpdateIssuesCounts();
            WriteToTerminal("已清空错误和警告列表", TerminalMessageType.Info);
        }

        private void ToggleIssues_Click(object sender, RoutedEventArgs e)
        {
            if (IssuesScrollViewer.Visibility == Visibility.Visible)
            {
                IssuesScrollViewer.Visibility = Visibility.Collapsed;
                CollapseIssuesButton.Content = "展开";
            }
            else
            {
                IssuesScrollViewer.Visibility = Visibility.Visible;
                CollapseIssuesButton.Content = "收起";
            }
        }

        private void ClearTerminal_Click(object sender, RoutedEventArgs e)
        {
            ClearTerminal();
        }

        private void ScrollToBottom_Click(object sender, RoutedEventArgs e)
        {
            TerminalScrollViewer.ScrollToVerticalOffset(TerminalScrollViewer.ExtentHeight);
        }

        private async void ExportLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var savePicker = new Windows.Storage.Pickers.FileSavePicker();
                savePicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
                savePicker.FileTypeChoices.Add("文本文件", new List<string>() { ".txt" });
                savePicker.FileTypeChoices.Add("日志文件", new List<string>() { ".log" });
                savePicker.SuggestedFileName = $"PluginBuildLog_{DateTime.Now:yyyyMMdd_HHmmss}";

                var window = App.MainWindow;
                if (window != null)
                {
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                    WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hwnd);
                }

                var file = await savePicker.PickSaveFileAsync();
                if (file != null)
                {
                    // 获取终端中的所有文本内容
                    var logContent = ExtractTerminalContent();

                    // 写入文件
                    await Windows.Storage.FileIO.WriteTextAsync(file, logContent);

                    WriteToTerminal($"日志已导出到: {file.Path}", TerminalMessageType.Success);
                }
            }
            catch (Exception ex)
            {
                WriteToTerminal($"导出日志失败: {ex.Message}", TerminalMessageType.Error);
            }
        }

        private async void ExportErrors_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var savePicker = new Windows.Storage.Pickers.FileSavePicker();
                savePicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
                savePicker.FileTypeChoices.Add("文本文件", new List<string>() { ".txt" });
                savePicker.FileTypeChoices.Add("日志文件", new List<string>() { ".log" });
                savePicker.SuggestedFileName = $"PluginBuildErrors_{DateTime.Now:yyyyMMdd_HHmmss}";

                var window = App.MainWindow;
                if (window != null)
                {
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                    WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hwnd);
                }

                var file = await savePicker.PickSaveFileAsync();
                if (file != null)
                {
                    // 获取所有错误信息
                    var errorContent = ExtractErrorContent();

                    // 写入文件
                    await Windows.Storage.FileIO.WriteTextAsync(file, errorContent);

                    WriteToTerminal($"错误信息已导出到: {file.Path}", TerminalMessageType.Success);
                }
            }
            catch (Exception ex)
            {
                WriteToTerminal($"导出错误信息失败: {ex.Message}", TerminalMessageType.Error);
            }
        }

        #endregion

        #region 构建方法

        private async void StartBuild_Click(object sender, RoutedEventArgs e)
        {
            if (_isBuildInProgress)
                return;

            if (!ValidateInputs())
                return;

            // 清空之前的问题列表
            ClearIssues_Click(null, null);

            _isBuildInProgress = true;
            _buildCancellationTokenSource = new CancellationTokenSource();

            StartBuildButton.IsEnabled = false;
            StopBuildButton.IsEnabled = true;

            // 更新导航栏状态：开始构建
            UpdateNavigationStatus(true, 1, "插件构建中...");

            WriteToTerminal("=== 开始插件构建 ===", TerminalMessageType.Success);

            // 显示插件详细信息
            if (_currentPluginInfo != null)
            {
                WriteToTerminal($"插件名称: {_currentPluginInfo.GetDisplayName()}", TerminalMessageType.Info);
                WriteToTerminal($"插件版本: {_currentPluginInfo.GetVersionString()}", TerminalMessageType.Info);
                WriteToTerminal($"作者: {_currentPluginInfo.CreatedBy ?? "未知"}", TerminalMessageType.Info);
                WriteToTerminal($"模块数量: {_currentPluginInfo.GetModuleCount()}", TerminalMessageType.Info);
            }

            try
            {
                if (SingleBuildRadio.IsChecked == true)
                {
                    await PerformSingleBuildAsync(_buildCancellationTokenSource.Token);
                }
                else
                {
                    // 批量构建
                    var selectedEngines = GetSelectedEnginesForBatch();
                    UpdateNavigationStatus(true, selectedEngines.Count, $"批量构建中 (共{selectedEngines.Count}个引擎)");
                    await PerformBatchBuildAsync(_buildCancellationTokenSource.Token);
                }

                // 构建成功完成
                UpdateNavigationStatus(false, 0, "构建完成");
                WriteToTerminal("=== 构建成功完成 ===", TerminalMessageType.Success);
            }
            catch (OperationCanceledException)
            {
                WriteToTerminal("构建已被用户取消", TerminalMessageType.Warning);
                UpdateNavigationStatus(false, 0, "构建已取消");
                UpdateCurrentEngineInfo(null, "已取消");
            }
            catch (Exception ex)
            {
                WriteToTerminal($"构建过程中发生错误: {ex.Message}", TerminalMessageType.Error);
                UpdateNavigationStatus(false, 0, $"构建失败: {ex.Message}");
                UpdateCurrentEngineInfo(null, "构建失败");
            }
            finally
            {
                _isBuildInProgress = false;
                StartBuildButton.IsEnabled = true;
                StopBuildButton.IsEnabled = false;
                _buildCancellationTokenSource?.Dispose();
                _buildCancellationTokenSource = null;
                BuildStatusCard.Visibility = Visibility.Collapsed;

                // 延迟3秒后清除状态
                await Task.Delay(3000);
                if (!_isBuildInProgress) // 确保没有新的构建开始
                {
                    UpdateNavigationStatus(false, 0, "就绪");
                }
            }
        }


        private async Task PerformSingleBuildAsync(CancellationToken cancellationToken)
        {
            var engine = GetSelectedEngine();
            UpdateCurrentEngineInfo(engine, "开始构建");
            await ExecuteRunUATCommand(engine, cancellationToken);
        }

        private async Task PerformBatchBuildAsync(CancellationToken cancellationToken)
        {
            _selectedEnginesForBatch = GetSelectedEnginesForBatch();
            _batchBuildResults = new List<string>();

            WriteToTerminal($"开始批量构建，共 {_selectedEnginesForBatch.Count} 个引擎版本", TerminalMessageType.Info);

            // 更新为批量构建状态
            UpdateNavigationStatus(true, _selectedEnginesForBatch.Count, $"批量构建 (0/{_selectedEnginesForBatch.Count})");

            for (int i = 0; i < _selectedEnginesForBatch.Count; i++)
            {
                var engine = _selectedEnginesForBatch[i];

                // 更新批量构建进度
                UpdateNavigationStatus(true, _selectedEnginesForBatch.Count, $"批量构建 ({i + 1}/{_selectedEnginesForBatch.Count})");

                UpdateBatchProgress($"构建 {engine.DisplayName} ({i + 1}/{_selectedEnginesForBatch.Count})",
                    i, _selectedEnginesForBatch.Count);

                UpdateCurrentEngineInfo(engine, $"批量构建 {i + 1}/{_selectedEnginesForBatch.Count}");

                WriteToTerminal($"=== 开始构建 {engine.DisplayName} ({engine.FullVersion ?? engine.Version}) ===", TerminalMessageType.Info);

                try
                {
                    await ExecuteRunUATCommand(engine, cancellationToken);

                    var result = $"✓ {engine.DisplayName}: 构建成功";
                    _batchBuildResults.Add(result);
                    WriteToTerminal(result, TerminalMessageType.Success);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    var result = $"✗ {engine.DisplayName}: 构建失败 - {ex.Message}";
                    _batchBuildResults.Add(result);
                    WriteToTerminal(result, TerminalMessageType.Error);

                    if (StopOnErrorCheckBox.IsChecked.GetValueOrDefault())
                    {
                        WriteToTerminal("根据设置，遇到错误时停止批量构建", TerminalMessageType.Warning);
                        UpdateNavigationStatus(true, 1, "批量构建因错误停止");
                        break;
                    }
                }
            }

            // 显示批量构建结果摘要
            WriteToTerminal("=== 批量构建完成 ===", TerminalMessageType.Info);
            foreach (var result in _batchBuildResults)
            {
                WriteToTerminal(result, result.StartsWith("✓") ? TerminalMessageType.Success : TerminalMessageType.Error);
            }

            var successCount = _batchBuildResults.Count(r => r.StartsWith("✓"));
            var failCount = _batchBuildResults.Count(r => r.StartsWith("✗"));

            UpdateBatchProgress($"批量构建完成: {successCount} 成功, {failCount} 失败",
                _selectedEnginesForBatch.Count, _selectedEnginesForBatch.Count);

            UpdateCurrentEngineInfo(null, "批量构建完成");

            // 更新最终状态
            if (failCount > 0)
            {
                UpdateNavigationStatus(false, 0, $"批量构建完成: {successCount}成功 {failCount}失败");
            }
            else
            {
                UpdateNavigationStatus(false, 0, $"批量构建完成: 全部{successCount}个成功");
            }
        }

        private async Task ExecuteRunUATCommand(UnrealEngineInfo engine, CancellationToken cancellationToken)
        {
            UpdateCurrentEngineInfo(engine, "验证引擎配置");
            UpdateNavigationStatus(true, 1, $"构建 {engine.DisplayName}");

            WriteToTerminal($"验证引擎配置: {engine.DisplayName}", TerminalMessageType.Info);

            var runUATPath = Path.Combine(engine.EnginePath, "Engine", "Build", "BatchFiles", "RunUAT.bat");
            if (!File.Exists(runUATPath))
                throw new Exception($"RunUAT.bat 不存在: {runUATPath}");

            WriteToTerminal($"RunUAT.bat 路径: {runUATPath}", TerminalMessageType.Info);

            var outputPath = GetFinalOutputPath(engine);
            Directory.CreateDirectory(outputPath);

            var command = BuildRunUATCommand(engine);
            WriteToTerminal($"执行命令: {command}", TerminalMessageType.Command);

            UpdateCurrentEngineInfo(engine, "启动 UAT 构建进程");
            UpdateBuildProgress(10, "启动构建进程");
            UpdateBuildProgressBarState(false);
            UpdateNavigationStatus(true, 1, $"正在编译 {engine.DisplayName}");

            // 用于检测构建是否失败
            var buildFailed = false;
            var localCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // 事件处理
            void OutputHandler(string line)
            {
                // 检查是否包含构建失败信息
                if (line.Contains("BUILD FAILED", StringComparison.OrdinalIgnoreCase))
                {
                    buildFailed = true;
                    WriteToTerminal("检测到 BUILD FAILED，正在终止构建过程...", TerminalMessageType.Error);
                    UpdateCurrentEngineInfo(engine, "构建失败");
                    UpdateNavigationStatus(true, 1, "构建失败");

                    // 取消当前构建任务
                    localCancellationTokenSource.Cancel();
                }

                WriteToTerminal(line, TerminalMessageType.Info);
                UpdateBuildProgressFromOutput(line);
                UpdateNavigationStatusFromOutput(line, engine);
            }

            ConsoleService.Instance.OutputReceived += OutputHandler;

            try
            {
                await ConsoleService.Instance.ExecuteCommandAsync(command, localCancellationTokenSource.Token);

                // 等待构建完成（可根据实际情况优化等待方式）
                while (!localCancellationTokenSource.Token.IsCancellationRequested)
                {
                    await Task.Delay(1000, localCancellationTokenSource.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // 如果是我们主动取消的（因为BUILD FAILED），则抛出特定异常
                if (buildFailed)
                {
                    WriteToTerminal("构建因 BUILD FAILED 而终止", TerminalMessageType.Error);
                    throw new Exception("构建过程中检测到 BUILD FAILED 错误");
                }
                else
                {
                    WriteToTerminal("构建进程已被用户终止", TerminalMessageType.Warning);
                    UpdateNavigationStatus(false, 0, "构建已取消");
                    throw;
                }
            }
            finally
            {
                ConsoleService.Instance.OutputReceived -= OutputHandler;
                localCancellationTokenSource.Dispose();

                // 如果检测到构建失败，确保抛出异常
                if (buildFailed)
                {
                    throw new Exception("构建过程中检测到 BUILD FAILED 错误");
                }
            }
        }


        private void UpdateBuildProgressFromOutput(string output)
        {
            try
            {
                var lowerOutput = output.ToLower();

                if (lowerOutput.Contains("parsing") || lowerOutput.Contains("loading"))
                {
                    UpdateBuildProgress(20, "解析项目文件");
                }
                else if (lowerOutput.Contains("compiling") || lowerOutput.Contains("building"))
                {
                    UpdateBuildProgress(50, "正在编译插件");
                }
                else if (lowerOutput.Contains("linking"))
                {
                    UpdateBuildProgress(70, "正在链接");
                }
                else if (lowerOutput.Contains("packaging") || lowerOutput.Contains("copying"))
                {
                    UpdateBuildProgress(80, "正在打包插件");
                }
                else if (lowerOutput.Contains("success") || lowerOutput.Contains("completed"))
                {
                    UpdateBuildProgress(90, "构建接近完成");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateBuildProgressFromOutput error: {ex.Message}");
            }
        }

        // ... 其他方法保持不变，但需要添加一些事件处理 ...

        private async void BrowseSourcePath_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var filePicker = new FileOpenPicker();
                filePicker.SuggestedStartLocation = PickerLocationId.Desktop;
                filePicker.FileTypeFilter.Add(".uplugin");

                var window = App.MainWindow;
                if (window != null)
                {
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                    WinRT.Interop.InitializeWithWindow.Initialize(filePicker, hwnd);
                }

                var file = await filePicker.PickSingleFileAsync();
                if (file != null)
                {
                    SourcePathTextBox.Text = file.Path;
                    WriteToTerminal($"已选择插件文件: {file.Path}", TerminalMessageType.Success);
                    await LoadPluginInfo(file.Path);
                }
            }
            catch (Exception ex)
            {
                WriteToTerminal($"选择插件文件时出错: {ex.Message}", TerminalMessageType.Error);
            }
        }

        private async void BrowseOutputPath_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var folderPicker = new FolderPicker();
                folderPicker.SuggestedStartLocation = PickerLocationId.Desktop;
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
                    OutputPathTextBox.Text = folder.Path;
                    WriteToTerminal($"已选择输出路径: {folder.Path}", TerminalMessageType.Success);
                }
            }
            catch (Exception ex)
            {
                WriteToTerminal($"选择输出路径时出错: {ex.Message}", TerminalMessageType.Error);
            }
        }

        private void PreviewCommand_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!ValidateInputs())
                    return;

                var commands = new List<string>();

                if (SingleBuildRadio.IsChecked == true)
                {
                    var engine = GetSelectedEngine();
                    var command = BuildRunUATCommand(engine);
                    commands.Add(command);
                }
                else
                {
                    var engines = GetSelectedEnginesForBatch();
                    foreach (var engine in engines)
                    {
                        var command = BuildRunUATCommand(engine);
                        commands.Add($"# {engine.DisplayName}:");
                        commands.Add(command);
                        commands.Add("");
                    }
                }

                CommandPreviewText.Text = string.Join("\n\n", commands);
                CommandPreviewPanel.Visibility = Visibility.Visible;
                WriteToTerminal("已生成构建命令预览", TerminalMessageType.Info);
            }
            catch (Exception ex)
            {
                WriteToTerminal($"生成命令预览时出错: {ex.Message}", TerminalMessageType.Error);
            }
        }

        private void OpenOutput_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var outputPath = OutputPathTextBox.Text;
                if (Directory.Exists(outputPath))
                {
                    Process.Start("explorer.exe", outputPath);
                    WriteToTerminal($"已打开输出目录: {outputPath}", TerminalMessageType.Success);
                }
                else
                {
                    WriteToTerminal($"输出目录不存在: {outputPath}", TerminalMessageType.Warning);
                }
            }
            catch (Exception ex)
            {
                WriteToTerminal($"打开输出目录时出错: {ex.Message}", TerminalMessageType.Error);
            }
        }

        private void StopBuild_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_buildCancellationTokenSource != null && !_buildCancellationTokenSource.Token.IsCancellationRequested)
                {
                    _buildCancellationTokenSource.Cancel();

                    if (_currentProcess != null && !_currentProcess.HasExited)
                    {
                        _currentProcess.Kill();
                        WriteToTerminal("正在强制终止构建进程...", TerminalMessageType.Warning);
                    }

                    WriteToTerminal("正在取消构建...", TerminalMessageType.Warning);
                    UpdateCurrentEngineInfo(_currentBuildingEngine, "正在取消...");
                }
            }
            catch (Exception ex)
            {
                WriteToTerminal($"取消构建时出错: {ex.Message}", TerminalMessageType.Error);
            }
        }

        // 添加终端大小调整的事件处理
        private void TerminalSizeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    if (int.TryParse(selectedItem.Tag?.ToString(), out int height))
                    {
                        // 确保 TerminalScrollViewer 已经初始化
                        if (TerminalScrollViewer != null)
                        {
                            TerminalScrollViewer.Height = height;

                            // 只有在页面完全加载后才显示调整消息
                            if (this.IsLoaded)
                            {
                                WriteToTerminal($"终端高度已调整为 {height}px", TerminalMessageType.Info);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"调整终端大小时出错: {ex.Message}");
                // 不在初始化时显示错误，避免循环调用
            }
        }

        #endregion

        #region 导航状态管理

        /// <summary>
        /// 更新导航栏中的 InfoBadge 状态
        /// </summary>
        /// <param name="isActive">是否有活动任务</param>
        /// <param name="taskCount">任务数量</param>
        /// <param name="statusText">状态文本</param>
        private void UpdateNavigationStatus(bool isActive, int taskCount, string statusText)
        {
            try
            {
                // 使用全局状态服务更新
                if (isActive)
                {
                    Services.PluginsBuildStatusService.Instance.UpdateStatus(isActive, taskCount, statusText);
                }
                else
                {
                    Services.PluginsBuildStatusService.Instance.UpdateStatus(false, 0, statusText);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateNavigationStatus error: {ex.Message}");
            }
        }

        /// <summary>
        /// 根据构建输出更新导航状态
        /// </summary>
        private void UpdateNavigationStatusFromOutput(string output, UnrealEngineInfo engine)
        {
            try
            {
                var lowerOutput = output.ToLower();

                if (lowerOutput.Contains("parsing") || lowerOutput.Contains("loading"))
                {
                    UpdateNavigationStatus(true, 1, $"解析项目: {engine.DisplayName}");
                }
                else if (lowerOutput.Contains("compiling") || lowerOutput.Contains("building"))
                {
                    UpdateNavigationStatus(true, 1, $"正在编译: {engine.DisplayName}");
                }
                else if (lowerOutput.Contains("linking"))
                {
                    UpdateNavigationStatus(true, 1, $"正在链接: {engine.DisplayName}");
                }
                else if (lowerOutput.Contains("packaging") || lowerOutput.Contains("copying"))
                {
                    UpdateNavigationStatus(true, 1, $"正在打包: {engine.DisplayName}");
                }
                else if (lowerOutput.Contains("success") || lowerOutput.Contains("completed"))
                {
                    UpdateNavigationStatus(true, 1, $"即将完成: {engine.DisplayName}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateNavigationStatusFromOutput error: {ex.Message}");
            }
        }

        #endregion

        #region 辅助方法

        // 包括: LoadPluginInfo, DisplayPluginInfo, ValidateInputs, GetSelectedEngine, 
        // GetSelectedEnginesForBatch, BuildRunUATCommand, GetFinalOutputPath, 
        // GetSelectedBuildType, GetSelectedPlatforms, UpdateBatchProgress 等

        private async Task LoadPluginInfo(string pluginPath)
        {
            try
            {
                if (!File.Exists(pluginPath) || !pluginPath.EndsWith(".uplugin"))
                {
                    WriteToTerminal("错误: 请选择有效的 .uplugin 文件", TerminalMessageType.Error);
                    return;
                }

                var content = await File.ReadAllTextAsync(pluginPath, Encoding.UTF8);
                _currentPluginInfo = JsonSerializer.Deserialize<PluginInfo>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (_currentPluginInfo != null)
                {
                    DisplayPluginInfo(_currentPluginInfo);
                    WriteToTerminal($"成功解析插件信息: {_currentPluginInfo.GetDisplayName()} v{_currentPluginInfo.GetVersionString()}", TerminalMessageType.Success);
                }
                else
                {
                    WriteToTerminal("警告: 无法解析插件文件内容", TerminalMessageType.Warning);
                }
            }
            catch (JsonException ex)
            {
                WriteToTerminal($"解析插件文件失败: {ex.Message}", TerminalMessageType.Error);
                _currentPluginInfo = null;
                HidePluginInfo();
            }
            catch (Exception ex)
            {
                WriteToTerminal($"读取插件文件时出错: {ex.Message}", TerminalMessageType.Error);
                _currentPluginInfo = null;
                HidePluginInfo();
            }
        }

        private bool ShouldFilterMessage(string message)
        {
            // 过滤一些噪音消息
            var noisePatterns = new[]
            {
                @"^LogTemp:\s*Display:",
                @"^LogInit:\s*Display:",
                @"^LogCore:\s*Display:",
                @"^LogEngine:\s*Display:",
                @"^LogOutputDevice:",
                @"^LogModuleManager:",
                @"^LogWindows:",
                @"Reading BuildConfiguration",
                @"Executing actions \(\d+ in parallel\)",
                @"^\s*$", // 空行
                @"^Running Internal UnrealHeaderTool.*-WarningsAsErrors -installed$",
                @"Determining max actions to execute",
                @"Building UnrealHeaderTool",
                @"Parsing headers for",
                @"Generated code for target",
                @"Total execution time:"
            };

            return noisePatterns.Any(pattern =>
                System.Text.RegularExpressions.Regex.IsMatch(message, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        }


        private void DisplayPluginInfo(PluginInfo pluginInfo)
        {
            try
            {
                PluginInfoPanel.Visibility = Visibility.Visible;

                PluginFriendlyNameText.Text = pluginInfo.GetDisplayName();
                PluginVersionText.Text = pluginInfo.GetVersionString();
                PluginDescriptionText.Text = string.IsNullOrEmpty(pluginInfo.Description) ? "无描述" : pluginInfo.Description;
                PluginCreatedByText.Text = string.IsNullOrEmpty(pluginInfo.CreatedBy) ? "未知" : pluginInfo.CreatedBy;

                var moduleInfos = new List<string>();
                if (pluginInfo.Modules?.Count > 0)
                {
                    var runtimeModules = pluginInfo.Modules.Count(m => m.Type == "Runtime" || m.Type == "RuntimeAndProgram");
                    var editorModules = pluginInfo.Modules.Count(m => m.Type == "Editor" || m.Type == "UncookedOnly");
                    var otherModules = pluginInfo.Modules.Count - runtimeModules - editorModules;

                    if (runtimeModules > 0) moduleInfos.Add($"{runtimeModules} 运行时");
                    if (editorModules > 0) moduleInfos.Add($"{editorModules} 编辑器");
                    if (otherModules > 0) moduleInfos.Add($"{otherModules} 其他");

                    moduleInfos.Add($"(共 {pluginInfo.Modules.Count} 个模块)");
                }
                else
                {
                    moduleInfos.Add("无模块信息");
                }

                PluginModulesText.Text = string.Join(", ", moduleInfos);

                if (string.IsNullOrWhiteSpace(PluginNameTextBox.Text))
                {
                    PluginNameTextBox.PlaceholderText = $"留空将使用: {pluginInfo.GetDisplayName()}";
                }
            }
            catch (Exception ex)
            {
                WriteToTerminal($"显示插件信息时出错: {ex.Message}", TerminalMessageType.Error);
            }
        }

        private void HidePluginInfo()
        {
            try
            {
                PluginInfoPanel.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HidePluginInfo error: {ex.Message}");
            }
        }

        // 在类的字段部分添加这些变量
        // 错误过滤关键字列表
        private readonly HashSet<string> _falseErrorKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Running Internal UnrealHeaderTool",
            "LogTemp:",
            "LogInit:",
            "LogCore:",
            "LogEngine:",
            "UATHelper:",
            "AutomationTool:",
            "BuildCommand.Execute:",
            "Program.Main:",
            "UnrealBuildTool.Main:",
            "LogModuleManager:",
            "LogOutputDevice:",
            "LogWindows:",
            "LogD3D11RHI:",
            "Reading BuildConfiguration",
            "Determining max actions",
            "Executing actions",
            "Building UnrealHeaderTool",
            "Parsing headers",
            "Generated code",
            "Total execution time"
        };

        private bool IsFalseError(string message)
        {
            // 检查是否包含误报关键字
            if (_falseErrorKeywords.Any(keyword => message.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            // 检查特定模式
            var patterns = new[]
            {
                @"Running Internal UnrealHeaderTool.*\.uproject",
                @"LogTemp:\s*Display:",
                @"LogInit:\s*Display:",
                @"LogCore:\s*Display:",
                @"LogEngine:\s*Display:",
                @"UATHelper:\s*.*",
                @"AutomationTool:\s*.*",
                @"BuildCommand\.Execute:\s*.*",
                @"Program\.Main:\s*.*",
                @"Reading BuildConfiguration.*",
                @"Determining max actions.*",
                @"Executing actions.*",
                @"Building.*\.exe.*",
                @"Parsing headers.*",
                @"Generated code.*",
                @"Total execution time.*",
                @"LogModuleManager:\s*.*",
                @"LogOutputDevice:\s*.*"
            };

            foreach (var pattern in patterns)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(message, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsRealError(string message, TerminalMessageType messageType)
        {
            if (messageType == TerminalMessageType.Error)
                return true;

            var lowerMessage = message.ToLower();

            // 真正的错误模式
            var errorPatterns = new[]
            {
                @"\berror\s*C\d+:",
                @"\berror\s*LNK\d+:",
                @"\berror\s*MSB\d+:",
                @"fatal\s*error:",
                @"compilation\s*failed",
                @"build\s*failed",
                @"failed\s*to\s*compile",
                @"unresolved\s*external\s*symbol",
                @"undefined\s*reference",
                @"syntax\s*error",
                @"parse\s*error",
                @".*\.cpp\(\d+\):\s*error",
                @".*\.h\(\d+\):\s*error",
                @".*\.cs\(\d+\):\s*error",
                @"exception\s*thrown",
                @"access\s*violation",
                @"segmentation\s*fault"
            };

            return errorPatterns.Any(pattern =>
                System.Text.RegularExpressions.Regex.IsMatch(message, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        }

        private bool IsRealWarning(string message, TerminalMessageType messageType)
        {
            if (messageType == TerminalMessageType.Warning)
                return true;

            var lowerMessage = message.ToLower();

            // 真正的警告模式
            var warningPatterns = new[]
            {
                @"\bwarning\s*C\d+:",
                @"\bwarning\s*LNK\d+:",
                @"\bwarning\s*MSB\d+:",
                @".*\.cpp\(\d+\):\s*warning",
                @".*\.h\(\d+\):\s*warning",
                @".*\.cs\(\d+\):\s*warning",
                @"deprecated",
                @"obsolete",
                @"unreachable\s*code",
                @"unused\s*variable",
                @"unused\s*parameter",
                @"conversion\s*warning",
                @"truncation\s*warning"
            };

            return warningPatterns.Any(pattern =>
                System.Text.RegularExpressions.Regex.IsMatch(message, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        }


        private bool ValidateInputs()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(SourcePathTextBox.Text))
                {
                    WriteToTerminal("错误: 请选择插件 .uplugin 文件", TerminalMessageType.Error);
                    return false;
                }

                if (!File.Exists(SourcePathTextBox.Text) || !SourcePathTextBox.Text.EndsWith(".uplugin"))
                {
                    WriteToTerminal("错误: 请选择有效的 .uplugin 文件", TerminalMessageType.Error);
                    return false;
                }

                if (string.IsNullOrWhiteSpace(OutputPathTextBox.Text))
                {
                    WriteToTerminal("错误: 请选择输出路径", TerminalMessageType.Error);
                    return false;
                }

                if (SingleBuildRadio.IsChecked == true)
                {
                    if (EngineVersionComboBox.SelectedItem == null)
                    {
                        WriteToTerminal("错误: 请选择引擎版本", TerminalMessageType.Error);
                        return false;
                    }

                    var selectedEngine = GetSelectedEngine();
                    if (selectedEngine == null || !selectedEngine.IsValid)
                    {
                        WriteToTerminal("错误: 选择的引擎无效或不存在", TerminalMessageType.Error);
                        return false;
                    }
                }
                else if (BatchBuildRadio.IsChecked == true)
                {
                    var selectedEngines = GetSelectedEnginesForBatch();
                    if (!selectedEngines.Any())
                    {
                        WriteToTerminal("错误: 批量构建需要至少选择一个引擎版本", TerminalMessageType.Error);
                        return false;
                    }

                    var invalidEngines = selectedEngines.Where(e => !e.IsValid).ToList();
                    if (invalidEngines.Any())
                    {
                        WriteToTerminal($"错误: 以下引擎无效: {string.Join(", ", invalidEngines.Select(e => e.DisplayName))}", TerminalMessageType.Error);
                        return false;
                    }
                }
                else
                {
                    WriteToTerminal("错误: 请选择构建模式（单个或批量）", TerminalMessageType.Error);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                WriteToTerminal($"验证输入时出错: {ex.Message}", TerminalMessageType.Error);
                return false;
            }
        }

        private UnrealEngineInfo GetSelectedEngine()
        {
            try
            {
                var selectedItem = EngineVersionComboBox.SelectedItem as ComboBoxItem;
                return selectedItem?.Tag as UnrealEngineInfo;
            }
            catch (Exception ex)
            {
                WriteToTerminal($"获取选择引擎时出错: {ex.Message}", TerminalMessageType.Error);
                return null;
            }
        }

        private List<UnrealEngineInfo> GetSelectedEnginesForBatch()
        {
            try
            {
                return _engineCheckBoxes
                    .Where(cb => cb.IsChecked == true)
                    .Select(cb => cb.Tag as UnrealEngineInfo)
                    .Where(engine => engine != null)
                    .ToList();
            }
            catch (Exception ex)
            {
                WriteToTerminal($"获取批量引擎列表时出错: {ex.Message}", TerminalMessageType.Error);
                return new List<UnrealEngineInfo>();
            }
        }

        private string BuildRunUATCommand(UnrealEngineInfo engine)
        {
            var runUATPath = Path.Combine(engine.EnginePath, "Engine", "Build", "BatchFiles", "RunUAT.bat");
            var pluginPath = SourcePathTextBox.Text;
            var outputPath = GetFinalOutputPath(engine);

            var args = new List<string>
            {
                "BuildPlugin",
                $"-Plugin=\"{pluginPath}\"",
                $"-Package=\"{outputPath}\"",
                "-Rocket"
            };

            var buildConfig = GetSelectedBuildType();
            if (buildConfig != "Development")
            {
                args.Add($"-{buildConfig}");
            }

            var platforms = GetSelectedPlatforms();
            if (platforms.Length > 0)
            {
                args.Add($"-TargetPlatforms={string.Join("+", platforms)}");
            }

            args.Add("-VS2022");

            if (CleanBuildCheckBox.IsChecked.GetValueOrDefault())
            {
                args.Add("-Clean");
            }

            return $"\"{runUATPath}\" {string.Join(" ", args)}";
        }

        private string GetPluginBuildName()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(PluginNameTextBox.Text))
                    return PluginNameTextBox.Text.Trim();

                if (_currentPluginInfo != null && !string.IsNullOrEmpty(_currentPluginInfo.FriendlyName))
                    return _currentPluginInfo.FriendlyName;

                if (!string.IsNullOrEmpty(SourcePathTextBox.Text))
                    return Path.GetFileNameWithoutExtension(SourcePathTextBox.Text);

                return "UnnamedPlugin";
            }
            catch (Exception ex)
            {
                WriteToTerminal($"获取插件构建名称时出错: {ex.Message}", TerminalMessageType.Error);
                return "UnnamedPlugin";
            }
        }

        private string GetFinalOutputPath(UnrealEngineInfo engine)
        {
            var basePath = OutputPathTextBox.Text;
            var pluginName = GetPluginBuildName();
            var versionSuffix = _currentPluginInfo != null ? $"_v{_currentPluginInfo.GetVersionString()}" : "";
            var engineVersion = engine?.Version ?? "Unknown";

            return Path.Combine(basePath, $"{pluginName}{versionSuffix}_{engineVersion}");
        }

        private string GetSelectedBuildType()
        {
            if (DevelopmentRadio.IsChecked.GetValueOrDefault())
                return "Development";
            if (ShippingRadio.IsChecked.GetValueOrDefault())
                return "Shipping";
            if (DebugRadio.IsChecked.GetValueOrDefault())
                return "Debug";
            return "Development";
        }

        private string[] GetSelectedPlatforms()
        {
            var platforms = new List<string>();

            if (Win64CheckBox.IsChecked.GetValueOrDefault())
                platforms.Add("Win64");
            if (Win32CheckBox.IsChecked.GetValueOrDefault())
                platforms.Add("Win32");
            if (MacCheckBox.IsChecked.GetValueOrDefault())
                platforms.Add("Mac");
            if (LinuxCheckBox.IsChecked.GetValueOrDefault())
                platforms.Add("Linux");
            if (AndroidCheckBox.IsChecked.GetValueOrDefault())
                platforms.Add("Android");
            if (iOSCheckBox.IsChecked.GetValueOrDefault())
                platforms.Add("IOS");

            return platforms.ToArray();
        }

        private void UpdateBatchProgress(string status, int current, int total)
        {
            try
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (BatchProgressStatusText != null) BatchProgressStatusText.Text = status;
                    if (BatchProgressText != null) BatchProgressText.Text = $"{current}/{total}";
                    if (BatchProgressBar != null && total > 0)
                    {
                        BatchProgressBar.Value = (current * 100.0) / total;
                    }
                });
            }
            catch (Exception ex)
            {
                WriteToTerminal($"更新批量进度时出错: {ex.Message}", TerminalMessageType.Error);
            }
        }

        private string ExtractTerminalContent()
        {
            var content = new System.Text.StringBuilder();

            // 遍历所有段落并提取文本
            foreach (var block in TerminalOutput.Blocks)
            {
                if (block is Paragraph paragraph)
                {
                    var paragraphText = new System.Text.StringBuilder();
                    foreach (var inline in paragraph.Inlines)
                    {
                        if (inline is Run run)
                        {
                            paragraphText.Append(run.Text);
                        }
                    }

                    content.AppendLine(paragraphText.ToString());
                }
            }

            return content.ToString();
        }

        private string ExtractErrorContent()
        {
            var content = new System.Text.StringBuilder();

            // 标题和基本信息
            content.AppendLine($"Dream Unreal Manager 构建插件错误报告");
            content.AppendLine($"生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            content.AppendLine(new string('=', 60));

            // 构建信息摘要
            if (_currentPluginInfo != null)
            {
                content.AppendLine($"插件名称: {_currentPluginInfo.GetDisplayName()}");
                content.AppendLine($"插件版本: {_currentPluginInfo.GetVersionString()}");
            }

            content.AppendLine($"错误总数: {_errorCount}    警告总数: {_warningCount}");
            content.AppendLine(new string('=', 60));

            // 显示执行的 RunUAT 命令（如果可用）
            if (_selectedEnginesForBatch != null && _selectedEnginesForBatch.Any())
            {
                content.AppendLine("执行的构建命令:");
                foreach (var engine in _selectedEnginesForBatch.Take(1)) // 只显示第一个引擎的命令作为示例
                {
                    var command = BuildRunUATCommand(engine);
                    content.AppendLine($"RunUAT.bat 路径: {Path.Combine(engine.EnginePath, "Engine", "Build", "BatchFiles", "RunUAT.bat")}");
                    content.AppendLine($"执行命令: {command}");
                }

                content.AppendLine();
            }
            else if (_currentBuildingEngine != null)
            {
                var command = BuildRunUATCommand(_currentBuildingEngine);
                content.AppendLine("执行的构建命令:");
                content.AppendLine($"RunUAT.bat 路径: {Path.Combine(_currentBuildingEngine.EnginePath, "Engine", "Build", "BatchFiles", "RunUAT.bat")}");
                content.AppendLine($"执行命令: {command}");
                content.AppendLine();
            }

            // 错误和警告详情
            if (_buildIssues.Any())
            {
                // 分组显示错误和警告
                var errors = _buildIssues.Where(i => i.Type == BuildIssueType.Error)
                    .OrderBy(i => i.Timestamp)
                    .ToList();
                var warnings = _buildIssues.Where(i => i.Type == BuildIssueType.Warning)
                    .OrderBy(i => i.Timestamp)
                    .ToList();

                // 显示错误（如果存在）
                if (errors.Any())
                {
                    content.AppendLine($"【错误信息】(共 {errors.Count} 项)");
                    content.AppendLine(new string('-', 30));

                    // 错误
                    for (int i = 0; i < errors.Count; i++)
                    {
                        var error = errors[i];
                        content.AppendLine($"{i + 1}. [{error.Timestamp:HH:mm:ss}] {error.Engine}");

                        // 1) 从原始 message 中优先提取完整路径和行号
                        var m = System.Text.RegularExpressions.Regex.Match(
                            error.Message,
                            @"([A-Za-z]:[^\r\n:]*\.(?:cpp|h|cs|hpp|c))(?:\((\d+)\))?"
                        );
                        var fullPath = m.Success ? m.Groups[1].Value : error.SourceFile;
                        var lineNo = m.Success && m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : error.LineNumber;

                        // 2) 去掉前缀中的“路径(行号): ”，得到纯错误文本
                        string msgOnly = error.Message;
                        if (m.Success)
                        {
                            int end = m.Index + m.Length;
                            msgOnly = error.Message.Substring(end).TrimStart(':', ' ', '-', '\t');
                        }

                        // 3) 分两行输出：文件路径（含行号）一行，错误文本一行
                        if (!string.IsNullOrEmpty(fullPath))
                            content.AppendLine($"   文件: {fullPath}" + (lineNo > 0 ? $":{lineNo}" : ""));

                        content.AppendLine($"   错误: {msgOnly}");
                        content.AppendLine();
                    }
                }

                // 显示警告（如果存在）
                if (warnings.Any())
                {
                    content.AppendLine($"【警告信息】(共 {warnings.Count} 项)");
                    content.AppendLine(new string('-', 30));

                    // 警告（同样按两行展示）
                    for (int i = 0; i < warnings.Count; i++)
                    {
                        var warning = warnings[i];
                        content.AppendLine($"{i + 1}. [{warning.Timestamp:HH:mm:ss}] {warning.Engine}");

                        var m = System.Text.RegularExpressions.Regex.Match(
                            warning.Message,
                            @"([A-Za-z]:[^\r\n:]*\.(?:cpp|h|cs|hpp|c))(?:\((\d+)\))?"
                        );
                        var fullPath = m.Success ? m.Groups[1].Value : warning.SourceFile;
                        var lineNo = m.Success && m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : warning.LineNumber;

                        string msgOnly = warning.Message;
                        if (m.Success)
                        {
                            int end = m.Index + m.Length;
                            msgOnly = warning.Message.Substring(end).TrimStart(':', ' ', '-', '\t');
                        }

                        if (!string.IsNullOrEmpty(fullPath))
                            content.AppendLine($"   文件: {fullPath}" + (lineNo > 0 ? $":{lineNo}" : ""));

                        content.AppendLine($"   警告: {msgOnly}");
                        content.AppendLine();
                    }
                }
            }
            else
            {
                content.AppendLine("构建过程中未检测到错误或警告。");
            }

            return content.ToString();
        }

        #endregion
    }
}