using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DreamUnrealManager.Contracts.Services;
using DreamUnrealManager.Models;
using DreamUnrealManager.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DreamUnrealManager.Views
{
    /// <summary>
    /// 项目清理对话框：先扫描列出各类生成目录的占用，用户勾选后再删除。
    ///
    /// 界面用代码构建而非独立 XAML 页——它只是一个临时对话框，
    /// 与 LauncherPage 现有的详情/切换引擎弹窗保持同一种写法。
    /// </summary>
    public static class ProjectCleanupDialog
    {
        public static async Task ShowAsync(XamlRoot xamlRoot, ProjectInfo project)
        {
            if (project == null || string.IsNullOrWhiteSpace(project.ProjectDirectory))
            {
                return;
            }

            var service = App.GetService<IProjectCleanupService>();

            // ---- 第一步：扫描（带进度环，别让用户对着空窗口等） ----
            var scanningRing = new ProgressRing { IsActive = true, Width = 32, Height = 32 };
            var scanningDialog = new ContentDialog
            {
                Title = "正在扫描",
                Content = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        scanningRing,
                        new TextBlock
                        {
                            Text = $"正在统计 {project.DisplayName} 的生成文件占用...",
                            TextWrapping = TextWrapping.Wrap
                        }
                    }
                },
                CloseButtonText = "取消",
                XamlRoot = xamlRoot
            };

            using var scanCts = new CancellationTokenSource();
            scanningDialog.CloseButtonClick += (_, _) => scanCts.Cancel();

            CleanupScanResult? scan = null;
            Exception? scanError = null;

            // 在 Opened 里跑扫描，完成后自己关掉对话框：
            // WinUI 同一时刻只允许一个 ContentDialog，先 ShowAsync 再 await 扫描会打架。
            scanningDialog.Opened += async (dialog, _) =>
            {
                try
                {
                    scan = await service.ScanAsync(project.ProjectDirectory, scanCts.Token);
                }
                catch (OperationCanceledException)
                {
                    // 用户取消，scan 保持 null。
                }
                catch (Exception ex)
                {
                    scanError = ex;
                }
                finally
                {
                    dialog.Hide();
                }
            };

            await scanningDialog.ShowAsync();

            if (scanError != null)
            {
                AppLogService.Error("扫描可清理文件失败", scanError, "Cleanup");
                await ShowMessageAsync(xamlRoot, "扫描失败", scanError.Message);
                return;
            }

            if (scan == null)
            {
                // 被取消。
                return;
            }

            if (scan.TotalBytes == 0)
            {
                await ShowMessageAsync(xamlRoot, "无需清理",
                    scan.Warnings.Count > 0
                        ? string.Join(Environment.NewLine, scan.Warnings)
                        : "没有找到可清理的生成文件。");
                return;
            }

            // ---- 第二步：勾选类别 ----
            var checkBoxes = new List<(CheckBox Box, CleanupCategorySummary Summary)>();
            var panel = new StackPanel { Spacing = 4 };

            panel.Children.Add(new TextBlock
            {
                Text = $"共可释放 {ByteSizeFormatter.Format(scan.TotalBytes)}",
                Style = Application.Current.Resources["SubtitleTextBlockStyle"] as Style,
                Margin = new Thickness(0, 0, 0, 8)
            });

            foreach (var category in scan.Categories)
            {
                var box = new CheckBox
                {
                    // 默认只勾中间产物和 IDE 缓存：这两类删掉几乎没有代价。
                    // Binaries 要重新编译、Saved 含自动保存备份、DDC 要重烘焙着色器，
                    // 都交给用户自己决定。
                    IsChecked = category.Category is CleanupCategory.Intermediate or CleanupCategory.IdeCache,
                    Content = new StackPanel
                    {
                        Children =
                        {
                            new TextBlock
                            {
                                Text = $"{category.DisplayName}  —  {ByteSizeFormatter.Format(category.TotalBytes)}" +
                                       $"（{category.TotalFiles} 个文件，{category.Entries.Count} 个目录）",
                                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                            },
                            new TextBlock
                            {
                                Text = category.Description,
                                TextWrapping = TextWrapping.Wrap,
                                Opacity = 0.7,
                                Style = Application.Current.Resources["CaptionTextBlockStyle"] as Style
                            }
                        }
                    }
                };

                checkBoxes.Add((box, category));
                panel.Children.Add(box);
            }

            if (scan.Warnings.Count > 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "扫描提示：" + string.Join("；", scan.Warnings.Take(3)),
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.7,
                    Margin = new Thickness(0, 8, 0, 0)
                });
            }

            panel.Children.Add(new TextBlock
            {
                Text = "请先关闭 Unreal 编辑器和 IDE，否则被占用的文件会删除失败。",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 12, 0, 0),
                Foreground = Application.Current.Resources["SystemFillColorCautionBrush"] as Microsoft.UI.Xaml.Media.Brush
            });

            var selectDialog = new ContentDialog
            {
                Title = $"清理 {project.DisplayName}",
                Content = new ScrollViewer { Content = panel, MaxHeight = 420 },
                PrimaryButtonText = "开始清理",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = xamlRoot
            };

            if (await selectDialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            var selected = checkBoxes
                .Where(c => c.Box.IsChecked == true)
                .Select(c => c.Summary.Category)
                .ToList();

            if (selected.Count == 0)
            {
                return;
            }

            var selectedBytes = scan.Categories
                .Where(c => selected.Contains(c.Category))
                .Sum(c => c.TotalBytes);

            // ---- 第三步：二次确认（这是不可撤销的删除） ----
            var names = string.Join("、", scan.Categories
                .Where(c => selected.Contains(c.Category))
                .Select(c => c.DisplayName));

            var confirm = new ContentDialog
            {
                Title = "确认删除",
                Content = new TextBlock
                {
                    Text = $"将永久删除 {names}，释放约 {ByteSizeFormatter.Format(selectedBytes)}。" +
                           Environment.NewLine + Environment.NewLine +
                           "此操作不可撤销，删除的内容不会进入回收站。",
                    TextWrapping = TextWrapping.Wrap
                },
                PrimaryButtonText = "确认删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = xamlRoot
            };

            if (await confirm.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            // ---- 第四步：执行 ----
            var result = await service.CleanAsync(scan, selected);

            var summaryText = $"已释放 {ByteSizeFormatter.Format(result.FreedBytes)}，" +
                              $"删除 {result.DeletedDirectories} 个目录。";

            if (result.Failures.Count > 0)
            {
                summaryText += Environment.NewLine + Environment.NewLine +
                               "以下项未能删除（通常是被编辑器或 IDE 占用）：" + Environment.NewLine +
                               string.Join(Environment.NewLine, result.Failures.Take(10));
            }

            await ShowMessageAsync(xamlRoot, result.Success ? "清理完成" : "清理部分完成", summaryText);
        }

        private static async Task ShowMessageAsync(XamlRoot xamlRoot, string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = new ScrollViewer
                {
                    MaxHeight = 360,
                    Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }
                },
                CloseButtonText = "确定",
                XamlRoot = xamlRoot
            };

            await dialog.ShowAsync();
        }
    }
}
