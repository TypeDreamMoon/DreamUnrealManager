using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using DreamUnrealManager.Contracts.Services;
using DreamUnrealManager.Models;
using Microsoft.UI.Xaml.Controls;

namespace DreamUnrealManager.Services
{
    public sealed class IdeLauncherService : IIdeLauncherService
    {
        public async Task LaunchAsync(ProjectInfo project)
        {
            if (project == null || string.IsNullOrEmpty(project.ProjectDirectory))
            {
                await ShowErrorDialog("启动失败", "项目目录无效。");
                return;
            }

            var projectPath = Path.Combine(project.ProjectDirectory);

            string ide = SettingsService.Get("Default.IDE", "VS"); // 默认 IDE
            string idePath;

            switch (ide)
            {
                case "VS":
                {
                    idePath = SettingsService.Get("IDE.Path.VS", "");
                    projectPath = Path.Combine(projectPath, $"{project.ProjectName}.sln");
                }
                    break;
                case "RD":
                {
                    idePath = SettingsService.Get("IDE.Path.RD", "");
                    var launchMethod = SettingsService.Get("IDE.Rider.LaunchMethod", "SOLUTION");
                    switch (launchMethod)
                    {
                        case "SOLUTION":
                        {
                            projectPath = Path.Combine(projectPath, $"{project.ProjectName}.sln");
                            break;
                        }
                        case "UPROJECT":
                        {
                            projectPath = Path.Combine(projectPath, $"{project.ProjectName}.uproject");
                            break;
                        }

                        default:
                        {
                            projectPath = Path.Combine(projectPath, $"{project.ProjectName}.sln");
                            break;
                        }
                    }
                }
                    break;
                case "VSCode":
                {
                    idePath = SettingsService.Get("IDE.Path.VSCode", "");
                }
                    break;
                default:
                {
                    await ShowErrorDialog("启动失败", "未知的 IDE。");
                    return;
                }
            }

            if (string.IsNullOrEmpty(idePath))
            {
                // 如果没有设置 IDE 路径，弹出提示框引导用户设置
                await ShowErrorDialog("IDE 路径未设置", "请设置 IDE 路径以启动项目。");
                return;
            }

            try
            {
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = idePath,
                    Arguments = projectPath,
                    UseShellExecute = true
                };

                Process.Start(processStartInfo);
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("启动失败", $"启动 IDE 时发生错误: {ex.Message}");
            }
        }

        private async Task ShowErrorDialog(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "确定",
                XamlRoot = App.MainWindow.Content.XamlRoot
            };
            await dialog.ShowAsync();
        }
    }
}