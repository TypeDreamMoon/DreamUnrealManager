using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace DreamUnrealManager.Services
{
    /// <summary>
    /// 把本程序注册进 .uproject 的「打开方式」列表。
    ///
    /// 刻意不抢占默认关联：UE 依赖 UnrealVersionSelector 作为 .uproject 的默认处理程序
    /// 来做引擎切换和右键菜单，把它顶掉会破坏用户既有的工作流。
    /// 这里只写 HKCU\Software\Classes\Applications 下的条目——它让程序出现在
    /// 「打开方式」菜单里，不改变任何现有默认值，也不需要管理员权限。
    /// </summary>
    public static class FileAssociationService
    {
        private const string ApplicationExeName = "DreamUnrealManager.exe";
        private const string ApplicationsKey = @"Software\Classes\Applications\" + ApplicationExeName;
        private const string ProgIdDescription = "Dream Unreal Manager";

        public static bool IsRegistered()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(ApplicationsKey + @"\shell\open\command");
                var value = key?.GetValue(null) as string;
                if (string.IsNullOrWhiteSpace(value))
                {
                    return false;
                }

                // 程序换过安装位置时旧注册项会指向失效路径，这种情况按未注册处理，
                // 好让用户重新点一次注册把路径刷新掉。
                return value.Contains(GetExecutablePath(), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                AppLogService.Warn($"读取文件关联状态失败: {ex.Message}", "FileAssoc");
                return false;
            }
        }

        public static bool Register(out string? error)
        {
            error = null;

            try
            {
                var exe = GetExecutablePath();
                if (!File.Exists(exe))
                {
                    error = $"找不到可执行文件: {exe}";
                    return false;
                }

                using (var appKey = Registry.CurrentUser.CreateSubKey(ApplicationsKey))
                {
                    if (appKey == null)
                    {
                        error = "无法创建注册表项";
                        return false;
                    }

                    appKey.SetValue("FriendlyAppName", ProgIdDescription);
                }

                using (var commandKey = Registry.CurrentUser.CreateSubKey(ApplicationsKey + @"\shell\open\command"))
                {
                    commandKey?.SetValue(null, $"\"{exe}\" \"%1\"");
                }

                // SupportedTypes 决定这个程序会出现在哪些扩展名的「打开方式」里。
                using (var typesKey = Registry.CurrentUser.CreateSubKey(ApplicationsKey + @"\SupportedTypes"))
                {
                    typesKey?.SetValue(".uproject", string.Empty);
                }

                AppLogService.Info($"已注册 .uproject 打开方式: {exe}", "FileAssoc");
                NotifyShell();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                AppLogService.Error("注册文件关联失败", ex, "FileAssoc");
                return false;
            }
        }

        public static bool Unregister(out string? error)
        {
            error = null;

            try
            {
                // 只删自己建的这棵子树，不碰任何现有的默认关联。
                Registry.CurrentUser.DeleteSubKeyTree(ApplicationsKey, throwOnMissingSubKey: false);
                AppLogService.Info("已移除 .uproject 打开方式注册", "FileAssoc");
                NotifyShell();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                AppLogService.Error("移除文件关联失败", ex, "FileAssoc");
                return false;
            }
        }

        private static string GetExecutablePath()
        {
            // Environment.ProcessPath 在单文件/自包含发布下也是对的，
            // 比 Assembly.Location（单文件下为空）可靠。
            return Environment.ProcessPath
                   ?? Path.Combine(AppContext.BaseDirectory, ApplicationExeName);
        }

        /// <summary>通知资源管理器刷新关联缓存，否则「打开方式」要等到下次登录才更新。</summary>
        private static void NotifyShell()
        {
            try
            {
                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                AppLogService.Warn($"通知资源管理器刷新关联失败: {ex.Message}", "FileAssoc");
            }
        }

        private const int SHCNE_ASSOCCHANGED = 0x08000000;
        private const uint SHCNF_IDLIST = 0x0000;

        [System.Runtime.InteropServices.DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
    }
}
