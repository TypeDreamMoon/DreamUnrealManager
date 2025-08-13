using System.Diagnostics;
using DreamUnrealManager.Contracts.Services;
using DreamUnrealManager.Models;

namespace DreamUnrealManager.Services
{
    public sealed class UnrealProjectService : IUnrealProjectService
    {
        private IDialogService _dlg;
        private IProjectRepositoryService _repo;

        public UnrealProjectService()
        {
            _dlg = new DialogService();
            _repo = new ProjectRepositoryService();
        }

        public async Task LaunchProject(ProjectInfo project)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = project.ProjectPath,
                    UseShellExecute = true
                };
                Process.Start(psi);
                project.LastUsed = DateTime.Now;
                await _repo.SaveAsync(await _repo.LoadAsync());
            }
            catch (Exception ex)
            {
                await _dlg.ShowMessageAsync("错误", $"启动项目失败：{ex.Message}");
            }
        }
    }
}