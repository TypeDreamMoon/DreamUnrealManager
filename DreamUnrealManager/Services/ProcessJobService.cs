using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DreamUnrealManager.Services
{
    /// <summary>
    /// 把外部构建进程挂到一个 Job Object 上，并设置 KILL_ON_JOB_CLOSE。
    ///
    /// 存在的理由：RunUAT / UnrealBuildTool 会再派生一大串子进程（cl.exe、link.exe…）。
    /// 光在取消时 Kill 掉直接子进程并不够——应用崩溃、被任务管理器强杀、
    /// 或者用户直接关窗口时，这些孙子进程会变成孤儿，继续吃满 CPU 并锁住 Binaries 目录。
    /// Job Object 由内核保证：本进程句柄一关，整棵树一起终止，无论我们是怎么退出的。
    /// </summary>
    public static class ProcessJobService
    {
        private const int JobObjectExtendedLimitInformation = 9;
        private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

        private static readonly object Gate = new();
        private static IntPtr _jobHandle = IntPtr.Zero;
        private static bool _initialized;

        /// <summary>把进程加入 Job；失败只记日志，不影响构建本身。</summary>
        public static bool Track(Process process)
        {
            if (process == null)
            {
                return false;
            }

            lock (Gate)
            {
                if (!EnsureJob())
                {
                    return false;
                }

                try
                {
                    // 进程可能已经退出，此时 Handle 访问会抛。
                    if (process.HasExited)
                    {
                        return false;
                    }

                    if (AssignProcessToJobObject(_jobHandle, process.Handle))
                    {
                        return true;
                    }

                    AppLogService.Warn(
                        $"无法将进程 {process.Id} 加入 Job Object，错误码 {Marshal.GetLastWin32Error()}",
                        "ProcessJob");
                }
                catch (Exception ex)
                {
                    AppLogService.Error("加入 Job Object 异常", ex, "ProcessJob");
                }

                return false;
            }
        }

        private static bool EnsureJob()
        {
            if (_initialized)
            {
                return _jobHandle != IntPtr.Zero;
            }

            _initialized = true;

            try
            {
                _jobHandle = CreateJobObject(IntPtr.Zero, null);
                if (_jobHandle == IntPtr.Zero)
                {
                    AppLogService.Warn($"CreateJobObject 失败，错误码 {Marshal.GetLastWin32Error()}", "ProcessJob");
                    return false;
                }

                var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
                {
                    BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                    {
                        LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                    }
                };

                var length = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
                var pointer = Marshal.AllocHGlobal(length);
                try
                {
                    Marshal.StructureToPtr(info, pointer, false);
                    if (!SetInformationJobObject(_jobHandle, JobObjectExtendedLimitInformation, pointer, (uint)length))
                    {
                        AppLogService.Warn(
                            $"SetInformationJobObject 失败，错误码 {Marshal.GetLastWin32Error()}", "ProcessJob");
                        CloseHandle(_jobHandle);
                        _jobHandle = IntPtr.Zero;
                        return false;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(pointer);
                }

                AppLogService.Info("构建进程 Job Object 已就绪", "ProcessJob");
                return true;
            }
            catch (Exception ex)
            {
                AppLogService.Error("初始化 Job Object 异常", ex, "ProcessJob");
                _jobHandle = IntPtr.Zero;
                return false;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public IntPtr MinimumWorkingSetSize;
            public IntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public IntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public IntPtr ProcessMemoryLimit;
            public IntPtr JobMemoryLimit;
            public IntPtr PeakProcessMemoryUsed;
            public IntPtr PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(IntPtr hJob, int infoClass,
            IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);
    }
}
