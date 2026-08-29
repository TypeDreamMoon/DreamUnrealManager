using DreamUnrealManager.Helpers;
using DreamUnrealManager.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Windows.UI.ViewManagement;

namespace DreamUnrealManager;

public sealed partial class MainWindow : WindowEx
{
    /// <summary>“关闭到后台”开关的设置键；Settings 页切换后需要同步托盘图标状态。</summary>
    public const string CloseToBackgroundKey = "App.CloseToBackground";

    private readonly DispatcherQueue _dispatcher;

    /// <summary>走真正退出流程时置位，让 Closing 不再拦截。</summary>
    private bool _forceClose;

    public MainWindow()
    {
        InitializeComponent();

        _dispatcher = DispatcherQueue.GetForCurrentThread();

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets/WindowIcon.ico"));
        AppWindow.Closing += AppWindow_Closing;
        Content = null;
        Title = "Dream Unreal Manager";

        TrayIconService.ShowRequested += OnTrayShowRequested;
        TrayIconService.ExitRequested += OnTrayExitRequested;

        SyncTrayIcon();
    }

    /// <summary>按当前设置决定托盘图标的显示与否；设置页改动后调用。</summary>
    public void SyncTrayIcon()
    {
        if (SettingsService.Get(CloseToBackgroundKey, false))
        {
            TrayIconService.Show("Dream Unreal Manager");
        }
        else
        {
            TrayIconService.Hide();
        }
    }

    /// <summary>从托盘还原窗口。</summary>
    public void RestoreFromTray()
    {
        AppWindow.Show();
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        Activate();
        this.SetForegroundWindow();
    }

    /// <summary>真正退出：绕过“关闭到后台”拦截，清掉托盘图标后结束进程。</summary>
    public void ExitApplication()
    {
        _forceClose = true;
        TrayIconService.Hide();
        AppLogService.Info("用户从托盘菜单退出应用", "App");
        Close();
    }

    private void OnTrayShowRequested()
    {
        // 托盘窗口过程与 UI 线程同属一个线程，但显式 Enqueue 可避免在消息处理中重入 XAML。
        _dispatcher.TryEnqueue(RestoreFromTray);
    }

    private void OnTrayExitRequested()
    {
        _dispatcher.TryEnqueue(ExitApplication);
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_forceClose)
        {
            return;
        }

        try
        {
            if (!SettingsService.Get(CloseToBackgroundKey, false))
            {
                return;
            }

            args.Cancel = true;

            // 之前这里只是 Minimize()：窗口仍占着任务栏，而 Cancel 又拦掉了所有关闭路径，
            // 结果是既退不出也不像“后台”。改为真正隐藏窗口，退出入口交给托盘菜单。
            TrayIconService.Show("Dream Unreal Manager");
            AppWindow.Hide();
        }
        catch (Exception ex)
        {
            // 读设置或隐藏窗口失败时，宁可让窗口正常关闭，也不能把用户锁死在一个关不掉的进程里。
            AppLogService.Error("处理关闭到后台失败，回退为直接关闭", ex, "App");
            args.Cancel = false;
        }
    }
}
