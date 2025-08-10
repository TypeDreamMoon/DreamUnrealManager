using DreamUnrealManager.Contracts.Services;
using DreamUnrealManager.Helpers;
using DreamUnrealManager.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using Windows.System;

namespace DreamUnrealManager.Views;

public sealed partial class ShellPage : Page
{
    public ShellViewModel ViewModel
    {
        get;
    }

    public ShellPage(ShellViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        ViewModel.NavigationService.Frame = NavigationFrame;
        ViewModel.NavigationViewService.Initialize(NavigationViewControl);

        // TODO: Set the title bar icon by updating /Assets/WindowIcon.ico.
        // A custom title bar is required for full window theme and Mica support.
        // https://docs.microsoft.com/windows/apps/develop/title-bar?tabs=winui3#full-customization
        App.MainWindow.ExtendsContentIntoTitleBar = true;
        App.MainWindow.SetTitleBar(AppTitleBar);
        App.MainWindow.Activated += MainWindow_Activated;

        // 初始化插件构建状态监听
        InitializePluginsBuildStatus();
    }

    private void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        TitleBarHelper.UpdateTitleBar(RequestedTheme);

        KeyboardAccelerators.Add(BuildKeyboardAccelerator(VirtualKey.Left, VirtualKeyModifiers.Menu));
        KeyboardAccelerators.Add(BuildKeyboardAccelerator(VirtualKey.GoBack));
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        App.AppTitlebar = AppTitleBarText as UIElement;
    }

    private void NavigationViewControl_DisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
    {
        AppTitleBar.Margin = new Thickness()
        {
            Left = sender.CompactPaneLength * (sender.DisplayMode == NavigationViewDisplayMode.Minimal ? 2 : 1),
            Top = 0,
            Right = 0,
            Bottom = 0
        };
    }

    private static KeyboardAccelerator BuildKeyboardAccelerator(VirtualKey key, VirtualKeyModifiers? modifiers = null)
    {
        var keyboardAccelerator = new KeyboardAccelerator() { Key = key };

        if (modifiers.HasValue)
        {
            keyboardAccelerator.Modifiers = modifiers.Value;
        }

        keyboardAccelerator.Invoked += OnKeyboardAcceleratorInvoked;

        return keyboardAccelerator;
    }

    private static void OnKeyboardAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        var navigationService = App.GetService<INavigationService>();

        var result = navigationService.GoBack();

        args.Handled = result;
    }

    #region 插件构建状态管理

    private void InitializePluginsBuildStatus()
    {
        // 初始状态设置
        UpdatePluginsBuildBadge(false, 0, "就绪");

        // 订阅全局状态变化事件
        Services.PluginsBuildStatusService.Instance.StatusChanged += OnPluginsBuildStatusChanged;
    }

    private void OnPluginsBuildStatusChanged(object? sender, Services.PluginsBuildStatusEventArgs e)
    {
        UpdatePluginsBuildBadge(e.IsActive, e.TaskCount, e.StatusText);
    }

    // 在页面卸载时取消订阅
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Services.PluginsBuildStatusService.Instance.StatusChanged -= OnPluginsBuildStatusChanged;
    }

    /// <summary>
    /// 更新插件构建状态徽章
    /// </summary>
    /// <param name="isActive">是否有活动任务</param>
    /// <param name="taskCount">活动任务数</param>
    /// <param name="statusText">状态文本</param>
    public void UpdatePluginsBuildBadge(bool isActive, int taskCount, string statusText)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (PluginsBuildStatusBadge != null)
            {
                PluginsBuildStatusBadge.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
                PluginsBuildStatusBadge.Value = taskCount;

                // 根据状态设置不同的图标和颜色
                if (PluginsBuildStatusIcon != null)
                {
                    if (statusText.Contains("构建"))
                    {
                        PluginsBuildStatusIcon.Glyph = "\uE9F3"; // 构建图标
                        PluginsBuildStatusBadge.Foreground = new SolidColorBrush(Colors.Orange);
                    }
                    else if (statusText.Contains("运行"))
                    {
                        PluginsBuildStatusIcon.Glyph = "\uE768"; // 播放图标
                        PluginsBuildStatusBadge.Foreground = new SolidColorBrush(Colors.Green);
                    }
                    else if (statusText.Contains("错误") || statusText.Contains("失败"))
                    {
                        PluginsBuildStatusIcon.Glyph = "\uE783"; // 错误图标
                        PluginsBuildStatusBadge.Foreground = new SolidColorBrush(Colors.Red);
                    }
                    else
                    {
                        PluginsBuildStatusIcon.Glyph = "\uE9F3"; // 默认图标
                        PluginsBuildStatusBadge.Foreground = new SolidColorBrush(Colors.Gray);
                    }
                }

                if (PluginsBuildStatusTooltip != null)
                {
                    PluginsBuildStatusTooltip.Content = statusText;
                }
            }
        });
    }

    /// <summary>
    /// 开始构建状态
    /// </summary>
    public void StartBuildingStatus()
    {
        UpdatePluginsBuildBadge(true, 1, "插件构建中...");
    }

    /// <summary>
    /// 开始运行状态
    /// </summary>
    public void StartRunningStatus()
    {
        UpdatePluginsBuildBadge(true, 1, "插件运行中...");
    }

    /// <summary>
    /// 构建并运行状态
    /// </summary>
    public void StartBuildAndRunStatus()
    {
        UpdatePluginsBuildBadge(true, 2, "构建并运行中...");
    }

    /// <summary>
    /// 停止所有状态
    /// </summary>
    public void StopAllStatus()
    {
        UpdatePluginsBuildBadge(false, 0, "就绪");
    }

    /// <summary>
    /// 设置错误状态
    /// </summary>
    public void SetErrorStatus(string errorMessage)
    {
        UpdatePluginsBuildBadge(true, 1, $"错误: {errorMessage}");
    }

    #endregion
}