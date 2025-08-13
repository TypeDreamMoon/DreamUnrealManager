using System.Threading.Tasks;

namespace DreamUnrealManager.Contracts.Services
{
    public interface IEditorLaunchService
    {
        /// <summary>
        /// 根据 UE 引擎根目录启动 UnrealEditor.exe。
        /// 找不到则弹窗提示。
        /// </summary>
        Task LaunchEditorForEnginePathAsync(string engineRootPath);
    }
}