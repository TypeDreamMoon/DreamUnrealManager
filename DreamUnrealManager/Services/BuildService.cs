using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DreamUnrealManager.Contracts.Services;

namespace DreamUnrealManager.Services
{
    public sealed class BuildService : IBuildService
    {
        public async Task<bool> GenerateProjectFilesAsync(string uprojectPath, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(uprojectPath) || !File.Exists(uprojectPath))
                return false;

            var engine = await ResolveEngineForProjectAsync(uprojectPath, ct);
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

            var engine = await ResolveEngineForProjectAsync(uprojectPath, ct);
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
                catch
                {
                }

                tcs.TrySetCanceled(ct);
            });

            var code = await tcs.Task.ConfigureAwait(false);
            percent?.Report(100);
            return code == 0;
        }


        // 读取 .uproject 的 EngineAssociation，匹配到本机引擎。
        private static async Task<Models.UnrealEngineInfo?> ResolveEngineForProjectAsync(string uprojectPath, CancellationToken ct)
        {
            string? engineAssociation = null;

            try
            {
                var json = await File.ReadAllTextAsync(uprojectPath, ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("EngineAssociation", out var eng))
                    engineAssociation = eng.GetString();
            }
            catch
            {
                // ignore
            }

            var mgr = EngineManagerService.Instance;
            await mgr.LoadEngines(); // 直接 await（不需要 AsTask）
            var engines = mgr.GetValidEngines();
            if (engines == null || !engines.Any()) return null;

            if (!string.IsNullOrWhiteSpace(engineAssociation))
            {
                var resolver = new EngineResolverService();
                var resolved = await resolver.ResolveAsync(engineAssociation, ct).ConfigureAwait(false);
                if (resolved != null && Directory.Exists(resolved.EnginePath))
                {
                    return resolved;
                }
            }

            // 回退：取第一个有效引擎
            var first = engines.FirstOrDefault(e => e.IsValid && Directory.Exists(e.EnginePath));
            return first;
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
