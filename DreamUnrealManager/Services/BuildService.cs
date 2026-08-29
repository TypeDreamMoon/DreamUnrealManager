using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DreamUnrealManager.Contracts.Services;
using DreamUnrealManager.Helpers;

namespace DreamUnrealManager.Services
{
    public sealed class BuildService : IBuildService
    {
        // 解析器无状态，复用一份即可；原先每解析一个项目就 new 一次。
        private static readonly EngineResolverService Resolver = new();

        public async Task<bool> GenerateProjectFilesAsync(string uprojectPath, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(uprojectPath) || !File.Exists(uprojectPath))
                return false;

            var engine = await ResolveEngineForProjectAsync(uprojectPath, null, ct);
            if (engine == null || !Directory.Exists(engine.EnginePath))
                return false;

            // 优先走 UnrealBuildTool 生成（5.x 默认路径）
            var engineRoot = engine.EnginePath;
            var ubtExe = Path.Combine(engineRoot, "Engine", "Binaries", "DotNET", "UnrealBuildTool", "UnrealBuildTool.exe");
            if (!File.Exists(ubtExe))
            {
                // 兼容旧结构
                ubtExe = Path.Combine(engineRoot, "Engine", "Binaries", "DotNET", "UnrealBuildTool.exe");
            }

            if (File.Exists(ubtExe))
            {
                var args = BuildProjectFilesArguments(uprojectPath, engine);
                var psi = new ProcessStartInfo
                {
                    FileName = ubtExe,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = engineRoot
                };

                return await RunWithLogsAsync(psi, null, null, ct);
            }

            // 回退方案：调用 GenerateProjectFiles.bat（某些版本可用）
            var genBat = Path.Combine(engineRoot, "Engine", "Build", "BatchFiles", "GenerateProjectFiles.bat");
            if (File.Exists(genBat))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = genBat,
                    Arguments = $"\"{uprojectPath}\"",
                    WorkingDirectory = Path.GetDirectoryName(genBat),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                return await RunWithLogsAsync(psi, null, null, ct);
            }

            return false;
        }

        public async Task<bool> GenerateProjectFilesAsync(
            string uprojectPath,
            IProgress<string>? log,
            IProgress<int>? percent,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(uprojectPath) || !File.Exists(uprojectPath))
                return false;

            var engine = await ResolveEngineForProjectAsync(uprojectPath, log, ct);
            if (engine == null || !Directory.Exists(engine.EnginePath))
                return false;

            // 先尝试 UBT.exe（5.x）
            var engineRoot = engine.EnginePath;
            var ubtExe = Path.Combine(engineRoot, "Engine", "Binaries", "DotNET", "UnrealBuildTool", "UnrealBuildTool.exe");
            if (!File.Exists(ubtExe))
            {
                // 兼容旧结构
                ubtExe = Path.Combine(engineRoot, "Engine", "Binaries", "DotNET", "UnrealBuildTool.exe");
            }

            if (File.Exists(ubtExe))
            {
                var args = BuildProjectFilesArguments(uprojectPath, engine);
                var psi = new ProcessStartInfo
                {
                    FileName = ubtExe,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = engineRoot
                };
                return await RunWithLogsAsync(psi, log, percent, ct);
            }

            // 回退：GenerateProjectFiles.bat
            var genBat = Path.Combine(engineRoot, "Engine", "Build", "BatchFiles", "GenerateProjectFiles.bat");
            if (File.Exists(genBat))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = genBat,
                    Arguments = $"\"{uprojectPath}\"",
                    WorkingDirectory = Path.GetDirectoryName(genBat),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                return await RunWithLogsAsync(psi, log, percent, ct);
            }

            return false;
        }

        private static async Task<bool> RunWithLogsAsync(
            ProcessStartInfo psi,
            IProgress<string>? log,
            IProgress<int>? percent,
            CancellationToken ct)
        {
            // UBT / GenerateProjectFiles.bat 的输出按 UTF-8 解码，避免中文系统上日志乱码。
            psi.UseUtf8Output();

            using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var percentRegex = new Regex(@"(?<!\d)(\d{1,3})\s*%", RegexOptions.Compiled);

            proc.OutputDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                log?.Report(e.Data);

                var m = percentRegex.Match(e.Data);
                if (m.Success && int.TryParse(m.Groups[1].Value, out var p))
                {
                    p = Math.Clamp(p, 0, 100);
                    percent?.Report(p);
                }
            };
            proc.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                log?.Report(e.Data);
                // 错误流里也可能带进度
                var m = percentRegex.Match(e.Data);
                if (m.Success && int.TryParse(m.Groups[1].Value, out var p))
                {
                    p = Math.Clamp(p, 0, 100);
                    percent?.Report(p);
                }
            };
            proc.Exited += (_, __) => tcs.TrySetResult(proc.ExitCode);

            if (!proc.Start()) return false;
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            await using var _ = ct.Register(() =>
            {
                try
                {
                    if (!proc.HasExited) proc.Kill(true);
                }
                catch (Exception ex)
                {
                    // HasExited 与 Kill 之间进程可能刚好退出，这类竞态是预期内的；
                    // 但仍要记下来，否则真正的"杀不掉"会完全无迹可寻。
                    AppLogService.Debug($"终止构建进程时异常（可能已自行退出）: {ex.Message}", "Build");
                }

                tcs.TrySetCanceled(ct);
            });

            var code = await tcs.Task.ConfigureAwait(false);

            // 有界等待异步输出读取排空，避免丢失最后几行日志；用超时防止子进程继承管道句柄导致无限阻塞。
            try
            {
                using var drainCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await proc.WaitForExitAsync(drainCts.Token).ConfigureAwait(false);
            }
            catch
            {
                // 超时/已退出/已取消等情况忽略
            }

            percent?.Report(100);
            return code == 0;
        }


        // 读取 .uproject 的 EngineAssociation，匹配到本机引擎。
        private static async Task<Models.UnrealEngineInfo?> ResolveEngineForProjectAsync(
            string uprojectPath,
            IProgress<string>? log,
            CancellationToken ct)
        {
            string? engineAssociation = null;

            try
            {
                var json = await File.ReadAllTextAsync(uprojectPath, ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("EngineAssociation", out var eng))
                    engineAssociation = eng.GetString();
            }
            catch (Exception ex)
            {
                Report(log, $"读取 {Path.GetFileName(uprojectPath)} 的 EngineAssociation 失败: {ex.Message}");
                AppLogService.Warn($"读取 EngineAssociation 失败: {uprojectPath}", "Build");
            }

            var mgr = EngineManagerService.Instance;
            await mgr.LoadEngines(); // 直接 await（不需要 AsTask）
            var engines = mgr.GetValidEngines();
            if (engines == null || !engines.Any())
            {
                Report(log, "未找到任何有效的 Unreal Engine 安装，请先在“引擎管理”中添加。");
                AppLogService.Warn("没有可用引擎", "Build");
                return null;
            }

            if (!string.IsNullOrWhiteSpace(engineAssociation))
            {
                var resolved = await Resolver.ResolveAsync(engineAssociation, ct).ConfigureAwait(false);
                if (resolved != null && Directory.Exists(resolved.EnginePath))
                {
                    return resolved;
                }

                // 关键：项目明确写了 EngineAssociation 却匹配不上时必须失败退出。
                // 原先这里会静默回退到“列表里第一个引擎”，等于拿一个用户根本没指定的
                // 引擎版本去生成工程文件，产物是错的而用户毫不知情。
                Report(log,
                    $"项目指定的引擎版本 \"{engineAssociation}\" 未在本机找到，已中止。" +
                    "请在“引擎管理”中添加该版本，或修改项目的引擎关联。");
                AppLogService.Warn(
                    $"EngineAssociation \"{engineAssociation}\" 无法解析，已中止: {uprojectPath}", "Build");
                return null;
            }

            // EngineAssociation 为空是合法情况（例如未关联的纯蓝图项目），
            // 此时回退到第一个有效引擎，但必须让用户知道用的是哪一个。
            var first = engines.FirstOrDefault(e => e.IsValid && Directory.Exists(e.EnginePath));
            if (first != null)
            {
                Report(log, $"项目未指定引擎版本，使用 {first.DisplayName} ({first.EnginePath})。");
                AppLogService.Info($"项目未指定引擎版本，回退到 {first.EnginePath}", "Build");
            }

            return first;
        }

        private static void Report(IProgress<string>? log, string message)
        {
            log?.Report(message);
        }

        private static string BuildProjectFilesArguments(string uprojectPath, Models.UnrealEngineInfo engine)
        {
            var args = $"-projectfiles -project=\"{uprojectPath}\" -game -progress";
            if (!engine.IsSourceBuild)
            {
                args += " -rocket";
            }

            return args;
        }
    }
}
