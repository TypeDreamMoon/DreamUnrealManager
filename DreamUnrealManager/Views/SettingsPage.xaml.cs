using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Linq;
using Windows.Storage.Pickers;
using DreamUnrealManager.Models;
using DreamUnrealManager.Services;
using Windows.UI;

namespace DreamUnrealManager.Views
{
    public sealed partial class SettingsPage : Page
    {
        private readonly EngineManagerService _engineManager;

        public SettingsPage()
        {
            this.InitializeComponent();
            _engineManager = EngineManagerService.Instance;
            
            Loaded += SettingsPage_Loaded;
        }

        private async void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadEngines();
        }

        private void CreateEngineItem(UnrealEngineInfo engine)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)), // Transparent
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 211, 211, 211)), // LightGray
                BorderThickness = new Thickness(1.0),
                CornerRadius = new CornerRadius(4.0),
                Margin = new Thickness(2.0),
                Padding = new Thickness(15.0, 10.0, 15.0, 10.0)
            };

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

            var statusColor = engine.IsValid ? 
                Color.FromArgb(255, 0, 128, 0) : // Green
                Color.FromArgb(255, 255, 0, 0);  // Red
            
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