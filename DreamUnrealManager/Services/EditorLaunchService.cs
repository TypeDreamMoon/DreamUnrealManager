using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using DreamUnrealManager.Contracts.Services;

namespace DreamUnrealManager.Services
{
    public sealed class EditorLaunchService : IEditorLaunchService
    {
        private readonly IDialogService _dialog;

        public EditorLaunchService(IDialogService dialog)
        {
            _dialog = dialog;
        }

        public async Task LaunchEditorForEnginePathAsync(string engineRootPath)
        {
            if (string.IsNullOrWhiteSpace(engineRootPath))
            {
                await _dialog.ShowMessageAsync("无法启动", "引擎路径为空。");
                return;
            }

            // 目标： [引擎目录]/Engine/Binaries/Win64/UnrealEditor.exe
            var exePath = Path.Combine(engineRootPath, "Engine", "Binaries", "Win64", "UnrealEditor.exe");

            if (!File.Exists(exePath))
            {
                await _dialog.ShowMessageAsync(
                    "启动失败",
                    "搜索不到执行文件 可能是引擎未编译或者已损坏"
                ); // 复用你已有的 DialogService。:contentReference[oaicite:2]{index=2}
                return;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = Path.GetDirectoryName(exePath)!,
                    UseShellExecute = true // 允许从 UI 进程直接拉起
                };
                Process.Start(psi);
            }
            catch
            {
                await _dialog.ShowMessageAsync("启动失败", "无法启动 UnrealEditor.exe，请以管理员身份或检查文件权限后重试。");
            }
        }
    }
}