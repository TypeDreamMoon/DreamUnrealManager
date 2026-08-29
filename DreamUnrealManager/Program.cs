using System;
using System.Runtime.InteropServices;
using System.Threading;
using DreamUnrealManager.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace DreamUnrealManager;

/// <summary>
/// 自定义入口点（csproj 里定义了 DISABLE_XAML_GENERATED_MAIN 关掉自动生成的 Main）。
///
/// 存在的理由：此前没有任何单实例保护。开两个窗口时两个进程各自持有一份项目缓存，
/// 谁后保存谁把对方的改动整个覆盖掉——用户表现为“加的项目莫名其妙消失了”。
/// 这里用 AppInstance 注册单实例键，后启动的进程把激活请求重定向给已有实例并退出。
/// </summary>
public static class Program
{
    private const string InstanceKey = "DreamUnrealManager.MainInstance";

    /// <summary>后续实例传过来的命令行；主实例据此处理“双击 .uproject”等场景。</summary>
    public static event Action<AppActivationArguments>? Redirected;

    [STAThread]
    private static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        if (TryRedirectToExistingInstance())
        {
            // 已有实例接管，本进程直接退出。
            return 0;
        }

        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });

        return 0;
    }

    private static bool TryRedirectToExistingInstance()
    {
        try
        {
            var activationArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
            var keyInstance = AppInstance.FindOrRegisterForKey(InstanceKey);

            if (keyInstance.IsCurrent)
            {
                // 本进程是主实例：接住后续实例转发过来的激活请求。
                keyInstance.Activated += OnInstanceActivated;
                return false;
            }

            RedirectActivationTo(activationArgs, keyInstance);
            return true;
        }
        catch (Exception ex)
        {
            // 单实例机制本身出问题时，宁可多开一个窗口，也不能让程序起不来。
            AppLogService.Error("单实例检查失败，按独立实例继续启动", ex, "App");
            return false;
        }
    }

    private static void OnInstanceActivated(object? sender, AppActivationArguments e)
    {
        Redirected?.Invoke(e);
    }

    /// <summary>
    /// RedirectActivationToAsync 是异步的，而这里还没有消息循环可用。
    /// 官方做法：丢到线程池执行，主线程用 CoWaitForMultipleObjects 等待——
    /// 它在等待期间仍会抽取 COM 消息，普通的 WaitHandle.WaitOne 会导致死锁。
    /// </summary>
    private static void RedirectActivationTo(AppActivationArguments args, AppInstance keyInstance)
    {
        var redirectEvent = CreateEvent(IntPtr.Zero, true, false, null);

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                keyInstance.RedirectActivationToAsync(args).AsTask().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                AppLogService.Error("重定向到已有实例失败", ex, "App");
            }
            finally
            {
                SetEvent(redirectEvent);
            }
        });

        _ = CoWaitForMultipleObjects(
            CWMO_DEFAULT,
            INFINITE,
            1,
            new[] { redirectEvent },
            out _);

        CloseHandle(redirectEvent);
    }

    private const uint CWMO_DEFAULT = 0;
    private const uint INFINITE = 0xFFFFFFFF;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset,
        bool bInitialState, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetEvent(IntPtr hEvent);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("ole32.dll")]
    private static extern uint CoWaitForMultipleObjects(uint dwFlags, uint dwMilliseconds,
        ulong nHandles, IntPtr[] pHandles, out uint dwIndex);
}
