using System.Diagnostics;
using DreamUnrealManager.Contracts.Services;
using DreamUnrealManager.Models;

namespace DreamUnrealManager.Services
{
    public sealed class UnrealProjectService : IUnrealProjectService
    {
        private readonly IDialogService _dlg;
        private readonly IProjectRepositoryService _repository;

        public UnrealProjectService(IDialogService dialogService, IProjectRepositoryService repository)
        {
            _dlg = dialogService;
            _repository = repository;
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
                await _repository.SaveAsync(await _repository.LoadAsync());
            }
            catch (Exception ex)
            {
                await _dlg.ShowMessageAsync("错误", $"启动项目失败：{ex.Message}");
            }
        }
    }
}
