using System.Diagnostics;
using DreamUnrealManager.Contracts.Services;
using DreamUnrealManager.Models;

namespace DreamUnrealManager.Services
{
    public sealed class UnrealProjectService : IUnrealProjectService
    {
        private IDialogService _dlg;

        public UnrealProjectService()
        {
            _dlg = new DialogService();
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
                await App.RepositoryService.SaveAsync(await App.RepositoryService.LoadAsync());
            }
            catch (Exception ex)
            {
                await _dlg.ShowMessageAsync("错误", $"启动项目失败：{ex.Message}");
            }
        }
    }
}