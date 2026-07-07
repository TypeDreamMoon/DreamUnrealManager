using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DreamUnrealManager.Helpers;
using DreamUnrealManager.Models;
using DreamUnrealManager.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.UI;

namespace DreamUnrealManager.Views
{
    public sealed partial class FabCompliancePage : Page
    {
        // Segoe Fluent Icons 字形
        private const string GlyphPass = "";        // CheckMark
        private const string GlyphFail = "";        // ErrorBadge
        private const string GlyphWarning = "";     // Warning
        private const string GlyphInfo = "";        // Info
        private const string GlyphManual = "";      // Flag

        private readonly FabComplianceService _service = new();
        private ComplianceReport? _report;
        private bool _isAnalyzing;
        private bool _isUnderGit;
        private bool _isFixing;

        // 分组的展示顺序与中文标题。
        private static readonly (ComplianceCategory Category, string Title)[] CategoryOrder =
        {
            (ComplianceCategory.CodePlugin, "代码插件 (.uplugin)"),
            (ComplianceCategory.Content, "内容与文件"),
            (ComplianceCategory.ProductListing, "商品页信息"),
            (ComplianceCategory.Documentation, "文档"),
            (ComplianceCategory.Quality, "质量"),
            (ComplianceCategory.Legal, "法务")
        };

        public FabCompliancePage()
        {
            this.InitializeComponent();
            NavigationCacheMode = NavigationCacheMode.Required;
        }

        private void BrowseUPlugin_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                var filePath = Win32DialogHelper.PickSingleFile(hwnd, "选择插件文件", "插件描述文件 (*.uplugin)|*.uplugin");
                if (!string.IsNullOrWhiteSpace(filePath))
                {
                    UPluginPathTextBox.Text = filePath;
                }
            }
            catch (Exception ex)
            {
                SetStatusMessage($"选择插件文件时出错: {ex.Message}", isError: true);
            }
        }

        private async void Analyze_Click(object sender, RoutedEventArgs e)
        {
            await RunAnalysisAsync(UPluginPathTextBox.Text?.Trim() ?? string.Empty);
        }

        private async Task RunAnalysisAsync(string path)
        {
            if (_isAnalyzing)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || !path.EndsWith(".uplugin", StringComparison.OrdinalIgnoreCase))
            {
                SetStatusMessage("请先选择有效的 .uplugin 文件。", isError: true);
                return;
            }

            _isAnalyzing = true;
            AnalyzeButton.IsEnabled = false;
            ExportButton.IsEnabled = false;
            AnalyzingRing.IsActive = true;
            SetStatusMessage("正在检查...", isError: false);

            try
            {
                var report = await _service.AnalyzeAsync(path);
                _report = report;
                _isUnderGit = await _service.IsUnderGitAsync(report.PluginFolder);
                RenderReport(report);
                ExportButton.IsEnabled = true;
                SetStatusMessage($"检查完成 · {DateTime.Now:HH:mm:ss}", isError: false);
            }
            catch (Exception ex)
            {
                SetStatusMessage($"检查失败: {ex.Message}", isError: true);
            }
            finally
            {
                AnalyzingRing.IsActive = false;
                AnalyzeButton.IsEnabled = true;
                _isAnalyzing = false;
            }
        }

        private void SetStatusMessage(string message, bool isError)
        {
            AnalyzingText.Text = message;
            AnalyzingText.Foreground = isError
                ? GetBrush("SystemFillColorCriticalBrush", Colors.IndianRed)
                : GetBrush("TextFillColorSecondaryBrush", Colors.Gray);
        }

        #region 渲染

        private void RenderReport(ComplianceReport report)
        {
            // 插件信息
            var infoParts = new List<string> { $"插件: {report.PluginName}" };
            if (!string.IsNullOrWhiteSpace(report.PluginVersion))
            {
                infoParts.Add($"版本: {report.PluginVersion}");
            }

            if (!string.IsNullOrWhiteSpace(report.EngineVersion))
            {
                infoParts.Add($"引擎: {report.EngineVersion}");
            }

            infoParts.Add($"目录: {report.PluginFolder}");
            PluginInfoText.Text = string.Join("    ", infoParts);
            PluginInfoText.Visibility = Visibility.Visible;

            // 概要
            SummaryCard.Visibility = Visibility.Visible;
            VerdictText.Text = report.GetVerdict();

            if (report.FailCount > 0)
            {
                VerdictIcon.Glyph = GlyphFail;
                VerdictIcon.Foreground = GetBrush("SystemFillColorCriticalBrush", Colors.IndianRed);
            }
            else if (report.WarningCount > 0)
            {
                VerdictIcon.Glyph = GlyphWarning;
                VerdictIcon.Foreground = GetBrush("SystemFillColorCautionBrush", Colors.Orange);
            }
            else
            {
                VerdictIcon.Glyph = GlyphPass;
                VerdictIcon.Foreground = GetBrush("SystemFillColorSuccessBrush", Colors.SeaGreen);
            }

            PassCountText.Text = $"通过 {report.PassCount}";
            FailCountText.Text = $"失败 {report.FailCount}";
            WarningCountText.Text = $"警告 {report.WarningCount}";
            RecommendedCountText.Text = $"推荐 {report.RecommendedCount}";
            ManualCountText.Text = $"待确认 {report.ManualCount}";
            UpdateManualProgress();
            UpdateAutoFixUi();

            // 结果分组
            ResultsPanel.Children.Clear();
            foreach (var (category, title) in CategoryOrder)
            {
                var items = report.Items.Where(i => i.Category == category).ToList();
                if (items.Count == 0)
                {
                    continue;
                }

                ResultsPanel.Children.Add(BuildCategoryCard(title, items));
            }
        }

        private void UpdateManualProgress()
        {
            if (_report == null)
            {
                return;
            }

            var manualItems = _report.Items.Where(i => i.Status == ComplianceStatus.Manual).ToList();
            var done = manualItems.Count(i => i.ManualChecked);
            ManualProgressText.Text = manualItems.Count > 0
                ? $"人工确认进度: {done}/{manualItems.Count}（这些项无法自动判断，请逐项核对后勾选）"
                : "无需人工确认项。";
        }

        private void UpdateAutoFixUi()
        {
            var fixable = _report?.Items.Count(i => i.AutoFix != AutoFixKind.None) ?? 0;

            if (_isUnderGit)
            {
                GitStatusIcon.Glyph = GlyphPass;
                GitStatusIcon.Foreground = GetBrush("SystemFillColorSuccessBrush", Colors.SeaGreen);
                GitStatusText.Text = fixable > 0
                    ? $"该插件目录在 Git 管理下，可自动修复 {fixable} 项。修复前建议提交/暂存当前改动以便回滚。"
                    : "该插件目录在 Git 管理下。当前没有可自动修复的项。";
            }
            else
            {
                GitStatusIcon.Glyph = GlyphWarning;
                GitStatusIcon.Foreground = GetBrush("SystemFillColorCautionBrush", Colors.Orange);
                GitStatusText.Text = "未检测到 Git 管理，自动修复已禁用。请将插件置于 Git 仓库中，以便修复后可回滚。";
            }

            AutoFixAllButton.IsEnabled = _isUnderGit && fixable > 0;
        }

        private FrameworkElement BuildCategoryCard(string title, List<ComplianceCheckItem> items)
        {
            var panel = new StackPanel { Spacing = 12 };

            var failed = items.Count(i => i.Status == ComplianceStatus.Fail);
            var warned = items.Count(i => i.Status == ComplianceStatus.Warning);
            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            header.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });

            var summary = failed > 0 ? $"{failed} 失败" : warned > 0 ? $"{warned} 警告" : "OK";
            header.Children.Add(new TextBlock
            {
                Text = summary,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = failed > 0
                    ? GetBrush("SystemFillColorCriticalBrush", Colors.IndianRed)
                    : warned > 0
                        ? GetBrush("SystemFillColorCautionBrush", Colors.Orange)
                        : GetBrush("TextFillColorSecondaryBrush", Colors.Gray)
            });
            panel.Children.Add(header);

            foreach (var item in items)
            {
                panel.Children.Add(BuildItemRow(item));
            }

            return new Border
            {
                Background = GetBrush("CardBackgroundFillColorDefaultBrush", Color.FromArgb(12, 255, 255, 255)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(20),
                Child = panel
            };
        }

        private FrameworkElement BuildItemRow(ComplianceCheckItem item)
        {
            var (glyph, brush, _) = GetStatusVisual(item.Status);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new FontIcon
            {
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                Glyph = glyph,
                FontSize = 18,
                Foreground = brush,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 12, 0)
            };
            Grid.SetColumn(icon, 0);
            grid.Children.Add(icon);

            var content = new StackPanel { Spacing = 4 };

            content.Children.Add(new TextBlock
            {
                Text = item.Title,
                FontSize = 15,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });

            if (!string.IsNullOrWhiteSpace(item.Requirement))
            {
                content.Children.Add(new TextBlock
                {
                    Text = item.Requirement,
                    FontSize = 11,
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = GetBrush("TextFillColorTertiaryBrush", Colors.Gray)
                });
            }

            if (!string.IsNullOrWhiteSpace(item.Detail))
            {
                content.Children.Add(new TextBlock
                {
                    Text = item.Detail,
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap
                });
            }

            if (item.Offenders.Count > 0)
            {
                content.Children.Add(BuildOffendersExpander(item.Offenders));
            }

            if (!string.IsNullOrWhiteSpace(item.Recommendation))
            {
                content.Children.Add(new TextBlock
                {
                    Text = $"建议: {item.Recommendation}",
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = GetBrush("AccentTextFillColorPrimaryBrush", Colors.CornflowerBlue)
                });
            }

            Grid.SetColumn(content, 1);
            grid.Children.Add(content);

            // 右侧控件：人工项 → 复选框；可清理项 → 清理按钮。
            if (item.Status == ComplianceStatus.Manual)
            {
                var check = new CheckBox
                {
                    Content = "已确认",
                    IsChecked = item.ManualChecked,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 0, 0)
                };
                check.Checked += (_, _) =>
                {
                    item.ManualChecked = true;
                    icon.Glyph = GlyphPass;
                    icon.Foreground = GetBrush("SystemFillColorSuccessBrush", Colors.SeaGreen);
                    UpdateManualProgress();
                };
                check.Unchecked += (_, _) =>
                {
                    item.ManualChecked = false;
                    icon.Glyph = glyph;
                    icon.Foreground = brush;
                    UpdateManualProgress();
                };
                Grid.SetColumn(check, 2);
                grid.Children.Add(check);
            }
            else if (item.AutoFix != AutoFixKind.None)
            {
                var fixButton = new Button
                {
                    Content = "自动修复",
                    Tag = item,
                    IsEnabled = _isUnderGit,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 0, 0)
                };
                ToolTipService.SetToolTip(fixButton, _isUnderGit ? "自动修复此项" : "需在 Git 管理下才能使用自动修复");
                fixButton.Click += AutoFix_Click;
                Grid.SetColumn(fixButton, 2);
                grid.Children.Add(fixButton);
            }

            return new Border
            {
                BorderBrush = GetBrush("DividerStrokeColorDefaultBrush", Color.FromArgb(20, 128, 128, 128)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 8, 0, 8),
                Child = grid
            };
        }

        private FrameworkElement BuildOffendersExpander(List<string> offenders)
        {
            const int cap = 60;
            var shown = offenders.Take(cap).ToList();
            var text = string.Join(Environment.NewLine, shown);
            if (offenders.Count > cap)
            {
                text += $"{Environment.NewLine}... 其余 {offenders.Count - cap} 项已省略";
            }

            var body = new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                Foreground = GetBrush("TextFillColorSecondaryBrush", Colors.Gray)
            };

            var scroll = new ScrollViewer
            {
                MaxHeight = 180,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = body
            };

            return new Expander
            {
                Header = $"查看 {offenders.Count} 项",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 2, 0, 2),
                Content = scroll
            };
        }

        #endregion

        #region 自动修复

        private async void AutoFix_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: ComplianceCheckItem item })
            {
                await ApplyAutoFixAsync(new[] { item.AutoFix });
            }
        }

        private async void AutoFixAll_Click(object sender, RoutedEventArgs e)
        {
            if (_report == null)
            {
                return;
            }

            var kinds = _report.Items
                .Select(i => i.AutoFix)
                .Where(k => k != AutoFixKind.None)
                .Distinct()
                .ToArray();

            await ApplyAutoFixAsync(kinds);
        }

        private async Task ApplyAutoFixAsync(IReadOnlyList<AutoFixKind> kinds)
        {
            // 防重入：修复或分析进行中时忽略后续点击（也避免重新检查期间的旧按钮触发第二个对话框）。
            if (_report == null || kinds.Count == 0 || _isFixing || _isAnalyzing)
            {
                return;
            }

            // 自动修复只在 Git 管理下可用，确保用户可回滚。
            if (!_isUnderGit)
            {
                SetStatusMessage("未检测到 Git 管理，已禁用自动修复。", isError: true);
                return;
            }

            _isFixing = true;
            AnalyzeButton.IsEnabled = false;
            try
            {
                var folder = _report.PluginFolder;
                var uplugin = _report.PluginPath;

                var confirm = new ContentDialog
                {
                    XamlRoot = this.XamlRoot,
                    Title = "自动修复",
                    Content = "将执行以下操作：\n\n"
                              + string.Join("\n", kinds.Select(k => "· " + DescribeFix(k)))
                              + "\n\n已跟踪文件的改动可用 Git 回滚，生成目录删除后可重新构建。建议先提交/暂存当前改动。是否继续？",
                    PrimaryButtonText = "修复",
                    CloseButtonText = "取消",
                    DefaultButton = ContentDialogButton.Close
                };

                if (await confirm.ShowAsync() != ContentDialogResult.Primary)
                {
                    return;
                }

                var summaries = new List<string>();
                foreach (var kind in kinds)
                {
                    summaries.Add(await ApplyOneAsync(kind, folder, uplugin));
                }

                SetStatusMessage(string.Join("；", summaries) + "，正在重新检查...", isError: false);

                if (!string.IsNullOrWhiteSpace(UPluginPathTextBox.Text))
                {
                    await RunAnalysisAsync(UPluginPathTextBox.Text.Trim());
                }
            }
            catch (Exception ex)
            {
                SetStatusMessage($"自动修复失败: {ex.Message}", isError: true);
            }
            finally
            {
                _isFixing = false;
                AnalyzeButton.IsEnabled = true;
            }
        }

        private async Task<string> ApplyOneAsync(AutoFixKind kind, string folder, string upluginPath)
        {
            switch (kind)
            {
                case AutoFixKind.CleanGeneratedFolders:
                {
                    var removed = await Task.Run(() => _service.CleanGeneratedFolders(folder));
                    return $"清理生成目录 {removed.Count} 个";
                }
                case AutoFixKind.RemoveEmptyFolders:
                {
                    var removed = await Task.Run(() => _service.RemoveEmptyFolders(folder));
                    return $"删除空文件夹 {removed.Count} 个";
                }
                case AutoFixKind.AddCopyrightNotice:
                {
                    var publisher = _report?.Publisher ?? string.Empty;
                    var year = DateTime.Now.Year;
                    var n = await Task.Run(() => _service.AddCopyrightNotices(folder, publisher, year));
                    return $"添加版权声明 {n} 个文件";
                }
                case AutoFixKind.DisableModelingTools:
                {
                    var ok = await Task.Run(() => _service.DisableModelingToolsEditorMode(upluginPath));
                    return ok ? "已禁用 ModelingToolsEditorMode" : "未找到 ModelingToolsEditorMode";
                }
                default:
                    return string.Empty;
            }
        }

        private string DescribeFix(AutoFixKind kind)
        {
            return kind switch
            {
                AutoFixKind.CleanGeneratedFolders => "删除 Binaries/Build/Intermediate/Saved/DerivedDataCache 生成目录",
                AutoFixKind.RemoveEmptyFolders => "删除空文件夹",
                AutoFixKind.AddCopyrightNotice =>
                    $"为缺少版权声明的源码文件顶部添加：// Copyright (C) {DateTime.Now.Year} {(string.IsNullOrWhiteSpace(_report?.Publisher) ? "<发布者>" : _report!.Publisher)}. All Rights Reserved.",
                AutoFixKind.DisableModelingTools => "在 .uplugin 中禁用 ModelingToolsEditorMode",
                _ => string.Empty
            };
        }

        #endregion

        #region 导出

        private async void Export_Click(object sender, RoutedEventArgs e)
        {
            if (_report == null)
            {
                return;
            }

            try
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                var suggested = $"FabCompliance_{SanitizeFileName(_report.PluginName)}_{DateTime.Now:yyyyMMdd_HHmmss}.md";
                var savePath = Win32DialogHelper.SaveFile(
                    hwnd,
                    "导出合规检查报告",
                    "Markdown (*.md)|*.md|文本文件 (*.txt)|*.txt",
                    suggested);
                if (string.IsNullOrWhiteSpace(savePath))
                {
                    return;
                }

                await File.WriteAllTextAsync(savePath, BuildReportText(_report), Encoding.UTF8);
                SetStatusMessage($"报告已导出到: {savePath}", isError: false);
            }
            catch (Exception ex)
            {
                SetStatusMessage($"导出失败: {ex.Message}", isError: true);
            }
        }

        private static string BuildReportText(ComplianceReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Fab 上架合规检查报告");
            sb.AppendLine();
            sb.AppendLine($"- 插件: {report.PluginName}");
            if (!string.IsNullOrWhiteSpace(report.PluginVersion))
            {
                sb.AppendLine($"- 版本: {report.PluginVersion}");
            }

            if (!string.IsNullOrWhiteSpace(report.EngineVersion))
            {
                sb.AppendLine($"- 引擎版本: {report.EngineVersion}");
            }

            sb.AppendLine($"- 插件目录: {report.PluginFolder}");
            sb.AppendLine($"- 生成时间: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"- 综合结论: {report.GetVerdict()}");
            sb.AppendLine($"- 统计: 通过 {report.PassCount} / 失败 {report.FailCount} / 警告 {report.WarningCount} / 推荐 {report.RecommendedCount} / 待确认 {report.ManualCount}");
            sb.AppendLine();

            foreach (var (category, title) in CategoryOrder)
            {
                var items = report.Items.Where(i => i.Category == category).ToList();
                if (items.Count == 0)
                {
                    continue;
                }

                sb.AppendLine($"## {title}");
                sb.AppendLine();
                foreach (var item in items)
                {
                    sb.AppendLine($"- [{StatusToText(item)}] {item.Title}");
                    if (!string.IsNullOrWhiteSpace(item.Requirement))
                    {
                        sb.AppendLine($"    - 要求: {item.Requirement}");
                    }

                    if (!string.IsNullOrWhiteSpace(item.Detail))
                    {
                        sb.AppendLine($"    - 说明: {item.Detail}");
                    }

                    if (!string.IsNullOrWhiteSpace(item.Recommendation))
                    {
                        sb.AppendLine($"    - 建议: {item.Recommendation}");
                    }

                    if (item.Offenders.Count > 0)
                    {
                        sb.AppendLine("    - 违规项:");
                        foreach (var offender in item.Offenders)
                        {
                            sb.AppendLine($"        - {offender}");
                        }
                    }
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static string StatusToText(ComplianceCheckItem item)
        {
            if (item.Status == ComplianceStatus.Manual)
            {
                return item.ManualChecked ? "已确认" : "待确认";
            }

            return item.Status switch
            {
                ComplianceStatus.Pass => "通过",
                ComplianceStatus.Fail => "失败",
                ComplianceStatus.Warning => "警告",
                ComplianceStatus.Recommended => "推荐",
                ComplianceStatus.Info => "信息",
                _ => item.Status.ToString()
            };
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "Plugin";
            }

            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(name.Where(c => !invalid.Contains(c)).ToArray());
            return string.IsNullOrWhiteSpace(cleaned) ? "Plugin" : cleaned;
        }

        #endregion

        #region 视觉辅助

        private (string Glyph, Brush Brush, string Label) GetStatusVisual(ComplianceStatus status)
        {
            return status switch
            {
                ComplianceStatus.Pass => (GlyphPass, GetBrush("SystemFillColorSuccessBrush", Colors.SeaGreen), "通过"),
                ComplianceStatus.Fail => (GlyphFail, GetBrush("SystemFillColorCriticalBrush", Colors.IndianRed), "失败"),
                ComplianceStatus.Warning => (GlyphWarning, GetBrush("SystemFillColorCautionBrush", Colors.Orange), "警告"),
                ComplianceStatus.Recommended => (GlyphInfo, GetBrush("SystemFillColorAttentionBrush", Colors.CornflowerBlue), "推荐"),
                ComplianceStatus.Manual => (GlyphManual, GetBrush("TextFillColorSecondaryBrush", Colors.Gray), "待确认"),
                _ => (GlyphInfo, GetBrush("TextFillColorSecondaryBrush", Colors.Gray), "信息")
            };
        }

        private static Brush GetBrush(string key, Color fallback)
        {
            try
            {
                if (Application.Current.Resources.TryGetValue(key, out var res) && res is Brush brush)
                {
                    return brush;
                }
            }
            catch
            {
                // ignore
            }

            return new SolidColorBrush(fallback);
        }

        #endregion
    }
}
