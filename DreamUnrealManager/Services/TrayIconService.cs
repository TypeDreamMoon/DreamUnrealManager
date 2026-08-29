using System;
using System.IO;
using System.Runtime.InteropServices;

namespace DreamUnrealManager.Services
{
    /// <summary>
    /// 系统托盘图标（Shell_NotifyIcon 直接 P/Invoke，不引三方包）。
    ///
    /// 存在的理由：设置项“关闭到后台”会在 AppWindow.Closing 里 Cancel 掉关闭，
    /// 而此前既没有托盘图标也没有任何“退出”入口，一旦勾上这个设置就只能去任务管理器杀进程。
    /// 有了托盘图标，隐藏后仍能还原窗口，并且始终存在一个真正的退出入口。
    /// </summary>
    public static class TrayIconService
    {
        private const int WM_APP = 0x8000;
        private const int WM_TRAYCALLBACK = WM_APP + 1;
        private const int WM_COMMAND = 0x0111;
        private const int WM_DESTROY = 0x0002;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_NULL = 0x0000;

        private const int NIM_ADD = 0x0000;
        private const int NIM_MODIFY = 0x0001;
        private const int NIM_DELETE = 0x0002;

        private const int NIF_MESSAGE = 0x0001;
        private const int NIF_ICON = 0x0002;
        private const int NIF_TIP = 0x0004;

        private const int IMAGE_ICON = 1;
        private const int LR_LOADFROMFILE = 0x0010;
        private const int LR_DEFAULTSIZE = 0x0040;

        private const int MF_STRING = 0x0000;
        private const int MF_SEPARATOR = 0x0800;
        private const int TPM_RIGHTBUTTON = 0x0002;

        private const int CMD_SHOW = 1;
        private const int CMD_EXIT = 2;

        private const string WindowClassName = "DreamUnrealManager.TrayHost";

        private static readonly object Gate = new();

        // 委托必须由托管侧强引用持有：交给 Win32 之后 GC 无从得知它仍在被调用，
        // 一旦被回收，下一条托盘消息就是 CallbackOnCollectedDelegate 崩溃。
        private static WndProcDelegate? _wndProc;
        private static IntPtr _hwnd;
        private static IntPtr _hIcon;
        private static bool _iconAdded;
        private static uint _taskbarCreatedMessage;

        /// <summary>点击“显示主窗口”或双击托盘图标时触发。</summary>
        public static event Action? ShowRequested;

        /// <summary>点击“退出”时触发；宿主据此执行真正的退出流程。</summary>
        public static event Action? ExitRequested;

        public static bool IsVisible
        {
            get
            {
                lock (Gate)
                {
                    return _iconAdded;
                }
            }
        }

        /// <summary>显示托盘图标；重复调用是幂等的。</summary>
        public static void Show(string tooltip)
        {
            lock (Gate)
            {
                try
                {
                    EnsureWindow();
                    if (_hwnd == IntPtr.Zero)
                    {
                        return;
                    }

                    var data = CreateData(tooltip);
                    var message = _iconAdded ? NIM_MODIFY : NIM_ADD;
                    if (Shell_NotifyIcon(message, ref data))
                    {
                        _iconAdded = true;
                    }
                    else if (!_iconAdded)
                    {
                        AppLogService.Warn("添加托盘图标失败", "Tray");
                    }
                }
                catch (Exception ex)
                {
                    AppLogService.Error("显示托盘图标异常", ex, "Tray");
                }
            }
        }

        /// <summary>移除托盘图标；宿主窗口保留，便于后续再次显示。</summary>
        public static void Hide()
        {
            lock (Gate)
            {
                if (!_iconAdded || _hwnd == IntPtr.Zero)
                {
                    return;
                }

                try
                {
                    var data = CreateData(string.Empty);
                    Shell_NotifyIcon(NIM_DELETE, ref data);
                }
                catch (Exception ex)
                {
                    AppLogService.Error("移除托盘图标异常", ex, "Tray");
                }
                finally
                {
                    _iconAdded = false;
                }
            }
        }

        private static NOTIFYICONDATA CreateData(string tooltip)
        {
            return new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = 1,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = WM_TRAYCALLBACK,
                hIcon = _hIcon,
                // szTip 上限 128 个字符（含结尾 NUL），超长会让 Shell_NotifyIcon 整体失败。
                szTip = tooltip.Length > 127 ? tooltip[..127] : tooltip
            };
        }

        private static void EnsureWindow()
        {
            if (_hwnd != IntPtr.Zero)
            {
                return;
            }

            var moduleHandle = GetModuleHandle(null);
            _wndProc = TrayWndProc;

            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = moduleHandle,
                lpszClassName = WindowClassName
            };

            // 类名已注册（例如上一次实例未彻底清理）时 RegisterClassEx 返回 0，
            // 此时直接继续 CreateWindowEx 即可，不必当成失败。
            RegisterClassEx(ref wc);

            // 普通隐藏窗口而非 HWND_MESSAGE：消息窗口不能作为 TrackPopupMenu 的前台窗口，
            // 右键菜单会出现点击外部不消失的问题。
            _hwnd = CreateWindowEx(0, WindowClassName, WindowClassName, 0,
                0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, moduleHandle, IntPtr.Zero);

            if (_hwnd == IntPtr.Zero)
            {
                AppLogService.Warn($"创建托盘宿主窗口失败，错误码 {Marshal.GetLastWin32Error()}", "Tray");
                return;
            }

            // explorer.exe 重启后所有托盘图标都会丢失，系统靠这条广播消息通知各进程重新添加。
            _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");

            LoadIcon();
        }

        private static void LoadIcon()
        {
            if (_hIcon != IntPtr.Zero)
            {
                return;
            }

            try
            {
                var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "WindowIcon.ico");
                if (File.Exists(iconPath))
                {
                    _hIcon = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 0, 0,
                        LR_LOADFROMFILE | LR_DEFAULTSIZE);
                }
            }
            catch (Exception ex)
            {
                AppLogService.Error("加载托盘图标文件失败", ex, "Tray");
            }

            if (_hIcon == IntPtr.Zero)
            {
                // 兜底用系统默认应用图标，至少保证托盘里能看到、点得到。
                _hIcon = LoadIcon(IntPtr.Zero, new IntPtr(32512)); // IDI_APPLICATION
            }
        }

        private static IntPtr TrayWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (_taskbarCreatedMessage != 0 && msg == _taskbarCreatedMessage)
                {
                    lock (Gate)
                    {
                        if (_iconAdded)
                        {
                            // 图标已随 explorer 一起消失，重新 NIM_ADD 而不是 NIM_MODIFY。
                            _iconAdded = false;
                        }
                    }

                    Show(BuildTooltip());
                    return IntPtr.Zero;
                }

                switch (msg)
                {
                    case WM_TRAYCALLBACK:
                        HandleTrayCallback((int)(lParam.ToInt64() & 0xFFFF));
                        return IntPtr.Zero;

                    case WM_COMMAND:
                        HandleCommand((int)(wParam.ToInt64() & 0xFFFF));
                        return IntPtr.Zero;

                    case WM_DESTROY:
                        Hide();
                        return IntPtr.Zero;
                }
            }
            catch (Exception ex)
            {
                // 窗口过程里逃逸的异常会直接掀掉消息循环，必须在此兜住。
                AppLogService.Error("托盘窗口过程异常", ex, "Tray");
            }

            return DefWindowProc(hwnd, msg, wParam, lParam);
        }

        private static string BuildTooltip() => "Dream Unreal Manager";

        private static void HandleTrayCallback(int mouseMessage)
        {
            switch (mouseMessage)
            {
                case WM_LBUTTONUP:
                case WM_LBUTTONDBLCLK:
                    ShowRequested?.Invoke();
                    break;

                case WM_RBUTTONUP:
                    ShowContextMenu();
                    break;
            }
        }

        private static void HandleCommand(int commandId)
        {
            switch (commandId)
            {
                case CMD_SHOW:
                    ShowRequested?.Invoke();
                    break;

                case CMD_EXIT:
                    ExitRequested?.Invoke();
                    break;
            }
        }

        private static void ShowContextMenu()
        {
            var menu = CreatePopupMenu();
            if (menu == IntPtr.Zero)
            {
                return;
            }

            try
            {
                AppendMenu(menu, MF_STRING, CMD_SHOW, "显示主窗口(&O)");
                AppendMenu(menu, MF_SEPARATOR, 0, null);
                AppendMenu(menu, MF_STRING, CMD_EXIT, "退出(&X)");

                GetCursorPos(out var pt);

                // TrackPopupMenu 要求调用方处于前台，否则点击菜单外部不会关闭菜单；
                // 收尾的 WM_NULL 是配套的官方约定，用来让菜单正确失活。
                SetForegroundWindow(_hwnd);
                TrackPopupMenu(menu, TPM_RIGHTBUTTON, pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);
                PostMessage(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
            }
            finally
            {
                DestroyMenu(menu);
            }
        }

        private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public uint uVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(int dwExStyle, string lpClassName, string lpWindowName,
            int dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu,
            IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadImage(IntPtr hInst, string name, uint type,
            int cx, int cy, uint fuLoad);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

        [DllImport("user32.dll")]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool AppendMenu(IntPtr hMenu, int uFlags, int uIDNewItem, string? lpNewItem);

        [DllImport("user32.dll")]
        private static extern bool TrackPopupMenu(IntPtr hMenu, int uFlags, int x, int y,
            int nReserved, IntPtr hWnd, IntPtr prcRect);

        [DllImport("user32.dll")]
        private static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);
    }
}
