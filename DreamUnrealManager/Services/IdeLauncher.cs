using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using DreamUnrealManager.Models;

namespace DreamUnrealManager.Services
{
    public sealed class IdeLauncher : IIdeLauncher
    {
        public Task LaunchAsync(ProjectInfo project)
        {
            if (project == null || string.IsNullOrEmpty(project.ProjectDirectory))
                return Task.CompletedTask;

            var slnPath = Path.Combine(project.ProjectDirectory, $"{project.DisplayName}.sln");
            if (!File.Exists(slnPath)) return Task.CompletedTask;

            // 假设使用默认VS打开
            Process.Start(new ProcessStartInfo
            {
                FileName = slnPath,
                UseShellExecute = true
            });

            return Task.CompletedTask;
        }
    }
}