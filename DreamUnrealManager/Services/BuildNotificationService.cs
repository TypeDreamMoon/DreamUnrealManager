using System;
using System.Net;
using DreamUnrealManager.Contracts.Services;

namespace DreamUnrealManager.Services
{
    /// <summary>
    /// 构建结束时发系统通知。
    ///
    /// 插件构建动辄十几分钟，用户不会一直盯着窗口。通知基础设施
    /// （AppNotificationService）项目里早就引入了，但除了一行被注释掉的
    /// 示例外从未被调用过，等于白装。这里把它接上第一个真实用途。
    ///
    /// 只在窗口不在前台时发：正盯着构建日志看的时候再弹通知是纯粹的打扰。
    /// </summary>
    public static class BuildNotificationService
    {
        /// <summary>设置键：是否在构建结束时发系统通知。</summary>
        public const string EnabledKey = "Build.NotifyOnComplete";

        public static bool IsEnabled => SettingsService.Get(EnabledKey, true);

        public static void NotifyBuildFinished(string pluginName, bool success, string? detail = null)
        {
            if (!IsEnabled)
            {
                return;
            }

            try
            {
                if (IsWindowInForeground())
                {
                    // 用户就在看着，终端里已经有结论了，不必再弹。
                    return;
                }

                var title = success ? "构建完成" : "构建失败";
                var body = string.IsNullOrWhiteSpace(detail)
                    ? pluginName
                    : $"{pluginName} — {detail}";

                // AppNotification 的载荷是 XML，文本必须转义，
                // 否则插件名里带 & 或 < 会让整条通知静默失败。
                var payload =
                    "<toast>" +
                    "<visual><binding template='ToastGeneric'>" +
                    $"<text>{Escape(title)}</text>" +
                    $"<text>{Escape(body)}</text>" +
                    "</binding></visual>" +
                    "</toast>";

                App.GetService<IAppNotificationService>().Show(payload);
                AppLogService.Info($"已发送构建通知: {title} - {body}", "Notify");
            }
            catch (Exception ex)
            {
                // 通知失败绝不能影响构建流程本身。
                AppLogService.Warn($"发送构建通知失败: {ex.Message}", "Notify");
            }
        }

        private static string Escape(string value) => WebUtility.HtmlEncode(value ?? string.Empty);

        private static bool IsWindowInForeground()
        {
            try
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                return GetForegroundWindow() == hwnd;
            }
            catch
            {
                // 判断不出来时按“不在前台”处理：宁可多发一条，也别漏掉用户真正需要的提醒。
                return false;
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
    }
}
