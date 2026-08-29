using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using DreamUnrealManager.Models;
using DreamUnrealManager.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DreamUnrealManager.Views
{
    /// <summary>项目日志 / 崩溃查看器：左边列文件，右边看尾部内容。</summary>
    public static class ProjectLogDialog
    {
        public static async Task ShowAsync(XamlRoot xamlRoot, ProjectInfo project)
        {
            if (project == null || string.IsNullOrWhiteSpace(project.ProjectDirectory))
            {
                return;
            }

            var entries = ProjectLogService.Enumerate(project.ProjectDirectory);

            if (entries.Count == 0)
            {
                await ShowMessageAsync(xamlRoot, "没有日志",
                    "该项目的 Saved/Logs 与 Saved/Crashes 下没有找到任何记录。" +
                    Environment.NewLine + "项目至少要用编辑器打开过一次才会生成日志。");
                return;
            }

            var listView = new ListView
            {
                Width = 300,
                SelectionMode = ListViewSelectionMode.Single,
                ItemsSource = entries.Select(e => new LogListItem(e)).ToList()
            };

            var contentBox = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                FontSize = 12,
                MinWidth = 560,
                Text = "从左侧选择一条记录查看内容。"
            };

            var scrollHost = new ScrollViewer
            {
                Content = contentBox,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Height = 420
            };

            var openFolderButton = new Button { Content = "打开所在目录", IsEnabled = false };
            ProjectLogEntry? current = null;

            listView.SelectionChanged += async (_, _) =>
            {
                if (listView.SelectedItem is not LogListItem item)
                {
                    return;
                }

                current = item.Entry;
                openFolderButton.IsEnabled = true;
                contentBox.Text = "正在读取...";

                try
                {
                    contentBox.Text = await ProjectLogService.ReadTailAsync(item.Entry);
                }
                catch (Exception ex)
                {
                    contentBox.Text = $"(读取失败: {ex.Message})";
                    AppLogService.Error("读取项目日志失败", ex, "ProjectLog");
                }
            };

            openFolderButton.Click += (_, _) =>
            {
                if (current == null)
                {
                    return;
                }

                try
                {
                    // 目录条目直接打开；文件条目用 /select 在资源管理器里选中它。
                    if (current.IsDirectory)
                    {
                        Process.Start("explorer.exe", $"\"{current.FullPath}\"");
                    }
                    else
                    {
                        Process.Start("explorer.exe", $"/select,\"{current.FullPath}\"");
                    }
                }
                catch (Exception ex)
                {
                    AppLogService.Warn($"打开日志目录失败: {ex.Message}", "ProjectLog");
                }
            };

            var body = new Grid { ColumnSpacing = 12, RowSpacing = 8 };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var listHost = new ScrollViewer { Content = listView, Height = 420 };
            Grid.SetColumn(listHost, 0);
            Grid.SetRow(listHost, 0);
            body.Children.Add(listHost);

            Grid.SetColumn(scrollHost, 1);
            Grid.SetRow(scrollHost, 0);
            body.Children.Add(scrollHost);

            Grid.SetColumn(openFolderButton, 0);
            Grid.SetRow(openFolderButton, 1);
            body.Children.Add(openFolderButton);

            var dialog = new ContentDialog
            {
                Title = $"{project.DisplayName} — 日志与崩溃记录",
                Content = body,
                CloseButtonText = "关闭",
                XamlRoot = xamlRoot
            };

            // 默认选中最新一条，省掉用户一次点击。
            listView.SelectedIndex = 0;

            await dialog.ShowAsync();
        }

        /// <summary>列表项：把大小/时间格式化好，ListView 直接显示 ToString()。</summary>
        private sealed class LogListItem
        {
            public LogListItem(ProjectLogEntry entry) => Entry = entry;

            public ProjectLogEntry Entry { get; }

            public override string ToString()
            {
                var prefix = Entry.Kind == ProjectLogKind.Crash ? "[崩溃] " : string.Empty;
                return $"{prefix}{Entry.DisplayName}{Environment.NewLine}" +
                       $"{Entry.LastWriteTime:yyyy-MM-dd HH:mm}  ·  {ByteSizeFormatter.Format(Entry.SizeBytes)}";
            }
        }

        private static async Task ShowMessageAsync(XamlRoot xamlRoot, string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                CloseButtonText = "确定",
                XamlRoot = xamlRoot
            };

            await dialog.ShowAsync();
        }
    }
}
