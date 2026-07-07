using System;
using System.IO;

namespace DreamUnrealManager.Helpers
{
    public static class PathUtils
    {
        /// <summary>
        /// 判断一个文件是否“确实已被删除”，而不是所在卷暂时不可用（离线/未挂载的移动硬盘、网络盘等）。
        /// 仅当路径所在的本地卷已就绪且文件不存在时才返回 true；卷离线或无法判断时返回 false，
        /// 以避免把临时不可达的项目/引擎误当成已删除而从列表中清除，造成数据丢失。
        /// </summary>
        public static bool IsGenuinelyMissing(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                if (File.Exists(path))
                {
                    return false;
                }

                var root = Path.GetPathRoot(path);

                // 形如 "C:\" 的本地盘符：可通过 DriveInfo 判断卷是否就绪。
                if (!string.IsNullOrEmpty(root) && root.Length <= 3 && root.Contains(':'))
                {
                    DriveInfo drive;
                    try
                    {
                        drive = new DriveInfo(root);
                    }
                    catch
                    {
                        return false; // 无法判断 → 保守保留
                    }

                    if (!drive.IsReady)
                    {
                        return false; // 卷离线/未挂载 → 不视为已删除
                    }

                    return true; // 卷已就绪且文件不存在 → 确实已删除
                }

                // UNC/网络路径等无法可靠判断卷状态 → 保守保留。
                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
