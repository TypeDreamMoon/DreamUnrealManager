using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DreamUnrealManager.Models;
using Microsoft.Win32;

namespace DreamUnrealManager.Services
{
    public sealed class EngineResolver : IEngineResolver
    {
        private const string HKCU_Builds = @"Software\Epic Games\Unreal Engine\Builds";
        private const string HKLM_Builds = @"SOFTWARE\Epic Games\Unreal Engine\Builds";

        private static readonly Regex GuidPattern =
            new(@"^\{?[0-9a-fA-F]{8}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{12}\}?$",
                RegexOptions.Compiled);

        public async Task<UnrealEngineInfo?> ResolveAsync(string engineAssociation, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(engineAssociation))
                return null;

            ct.ThrowIfCancellationRequested();

            // 1) 如果是 GUID：去注册表把引擎路径拿出来，再由 Build.version 获取版本号
            if (IsGuid(engineAssociation))
            {
                var enginePath = ResolveEnginePathFromGuid(engineAssociation);
                if (!string.IsNullOrWhiteSpace(enginePath))
                {
                    enginePath = NormalizeToEngineRoot(enginePath);

                    // 先看看 EngineManagerService 里是否已经有这个引擎，尽量复用对象
                    var mgr = EngineManagerService.Instance;
                    await mgr.LoadEngines();
                    var engines = mgr.GetValidEngines() ?? Enumerable.Empty<UnrealEngineInfo>();

                    var byPath = engines.FirstOrDefault(e =>
                        !string.IsNullOrWhiteSpace(e.EnginePath) &&
                        PathsEqual(e.EnginePath, enginePath));

                    if (byPath != null)
                        return byPath;

                    // 没找到就新建一个，让它自检并从 Build.version 解析版本
                    var info = new UnrealEngineInfo
                    {
                        EnginePath = enginePath // 赋值会触发内部 Validate + DetectVersion
                    };
                    // 如需再次确保，可手动刷新：
                    info.RefreshVersionInfo(); // UnrealEngineInfo 已提供该方法

                    return info.IsValid ? info : null;
                }

                // GUID 但注册表没有映射，返回 null（上层可用原逻辑兜底/提示）
                return null;
            }

            // 2) 否则：延用你现有的“在已加载引擎里按版本/完整版本/分支名匹配”的逻辑
            var mgr2 = EngineManagerService.Instance;
            await mgr2.LoadEngines();
            var list = mgr2.GetValidEngines() ?? Enumerable.Empty<UnrealEngineInfo>();

            return list.FirstOrDefault(e =>
                string.Equals(e.Version, engineAssociation, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(e.FullVersion, engineAssociation, StringComparison.OrdinalIgnoreCase) ||
                (e.BuildVersionInfo?.BranchName?.Contains(engineAssociation, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        private static bool IsGuid(string s) => GuidPattern.IsMatch(s?.Trim() ?? "");

        private static string ResolveEnginePathFromGuid(string guid)
        {
            if (string.IsNullOrWhiteSpace(guid)) return null;

            // 注册表里通常存成带花括号的大写 GUID；统一格式，以提高命中率
            var name = "{" + guid.Trim().Trim('{', '}') + "}";

            // 先 HKCU（你确认的路径）
            var fromHkcu = ReadRegValue(RegistryHive.CurrentUser, RegistryView.Default, HKCU_Builds, name);
            if (!string.IsNullOrWhiteSpace(fromHkcu))
                return fromHkcu;

            // 再 HKLM (64 / 32 视图兜底)
            var fromHklm64 = ReadRegValue(RegistryHive.LocalMachine, RegistryView.Registry64, HKLM_Builds, name);
            if (!string.IsNullOrWhiteSpace(fromHklm64))
                return fromHklm64;

            var fromHklm32 = ReadRegValue(RegistryHive.LocalMachine, RegistryView.Registry32, HKLM_Builds, name);
            return fromHklm32;
        }

        private static string ReadRegValue(RegistryHive hive, RegistryView view, string subkey, string valueName)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var key = baseKey.OpenSubKey(subkey, false);
                var v = key?.GetValue(valueName) as string;
                return string.IsNullOrWhiteSpace(v) ? null : v;
            }
            catch
            {
                return null;
            }
        }

        // 有些值可能是 UE 可执行文件路径或子目录；向上找包含 /Engine 的根
        private static string NormalizeToEngineRoot(string path)
        {
            try
            {
                var p = path;
                if (File.Exists(p)) p = Path.GetDirectoryName(p);
                var dir = new DirectoryInfo(p);
                while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Engine")))
                    dir = dir.Parent;
                return dir?.FullName ?? path;
            }
            catch
            {
                return path;
            }
        }

        private static bool PathsEqual(string a, string b)
            => string.Equals(Path.GetFullPath(a ?? ""), Path.GetFullPath(b ?? ""), StringComparison.OrdinalIgnoreCase);
    }
}