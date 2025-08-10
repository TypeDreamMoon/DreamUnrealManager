using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DreamUnrealManager.Services
{
    /// <summary>
    /// 单例控制台服务：启动 cmd.exe 并提供 ExecuteCommandAsync 与输出事件。
    /// </summary>
    public sealed class ConsoleService : IDisposable
    {
        private static readonly Lazy<ConsoleService> _instance = new(() => new ConsoleService());
        public static ConsoleService Instance => _instance.Value;

        private Process? _proc;
        private StreamWriter? _stdin;
        private readonly object _lock = new();
        private bool _started;

        /// <summary>
        /// 有新输出就回调一行
        /// </summary>
        public event Action<string>? OutputReceived;

        private ConsoleService() { }

        public bool IsRunning => _proc is { HasExited: false };

        public void StartIfNeeded()
        {
            if (_started && IsRunning) return;

            lock (_lock)
            {
                if (_started && IsRunning) return;

                _proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    },
                    EnableRaisingEvents = true
                };

                _proc.OutputDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        OutputReceived?.Invoke(e.Data);
                };
                _proc.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        OutputReceived?.Invoke(e.Data);
                };

                _proc.Start();
                _stdin = _proc.StandardInput;
                _proc.BeginOutputReadLine();
                _proc.BeginErrorReadLine();

                _started = true;
            }
        }

        /// <summary>
        /// 执行命令（不会等待命令结束，输出通过 OutputReceived 事件获取）。
        /// </summary>
        public Task ExecuteCommandAsync(string command, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(command))
                return Task.CompletedTask;

            StartIfNeeded();

            lock (_lock)
            {
                _stdin?.WriteLine(command);
                _stdin?.Flush();
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// 执行命令并收集结果，直到进程空闲为止（简单版）。
        /// </summary>
        public async Task<string> ExecuteCommandAndGetOutputAsync(string command, TimeSpan? drainTimeout = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(command))
                return string.Empty;

            StartIfNeeded();

            var tcs = new TaskCompletionSource<string>();
            var buf = new System.Text.StringBuilder();
            void Handler(string line) => buf.AppendLine(line);

            OutputReceived += Handler;
            try
            {
                await ExecuteCommandAsync(command, ct).ConfigureAwait(false);
                await Task.Delay(drainTimeout ?? TimeSpan.FromMilliseconds(200), ct).ConfigureAwait(false);
                return buf.ToString();
            }
            finally
            {
                OutputReceived -= Handler;
            }
        }

        public void Dispose()
        {
            try
            {
                _stdin?.WriteLine("exit");
                _stdin?.Flush();
            }
            catch { }

            try
            {
                if (_proc is { HasExited: false })
                    _proc.Kill(true);
            }
            catch { }
            finally
            {
                _stdin?.Dispose();
                _proc?.Dispose();
            }
        }
    }
}