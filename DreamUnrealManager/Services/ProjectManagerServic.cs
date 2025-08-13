using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DreamUnrealManager.Models;

namespace DreamUnrealManager.Services
{
    public class ProjectManagerServic
    {
        private static ProjectManagerServic _instance;
        public static ProjectManagerServic Instance => _instance ??= new ProjectManagerServic();

        private readonly string _configFilePath;

        public ObservableCollection<ProjectInfo> Projects
        {
            get;
            private set;
        }

        private ProjectManagerServic()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appFolder = Path.Combine(appDataPath, "DreamUnrealManager");
            Directory.CreateDirectory(appFolder);
            _configFilePath = Path.Combine(appFolder, "projects.json");

            Projects = new ObservableCollection<ProjectInfo>();
        }

        public async Task LoadProjects()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    var json = await File.ReadAllTextAsync(_configFilePath);
                    var projects = JsonSerializer.Deserialize<List<ProjectInfo>>(json);

                    Projects.Clear();
                    foreach (var project in projects ?? new List<ProjectInfo>())
                    {
                        // 验证项目文件是否存在
                        if (File.Exists(project.ProjectPath))
                        {
                            await RefreshProjectInfo(project);
                            Projects.Add(project);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load projects: {ex.Message}");
            }
        }

        public async Task SaveProjects()
        {
            try
            {
                var json = JsonSerializer.Serialize(Projects.ToList(), new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                await File.WriteAllTextAsync(_configFilePath, json);
            }
            catch (Exception ex)
            {
                throw new Exception($"保存项目配置失败: {ex.Message}");
            }
        }

        public async Task<ProjectInfo> AddProject(string projectPath)
        {
            if (!File.Exists(projectPath))
            {
                throw new FileNotFoundException($"项目文件不存在: {projectPath}");
            }

            // 检查是否已存在
            if (Projects.Any(p => string.Equals(p.ProjectPath, projectPath, StringComparison.OrdinalIgnoreCase)))
            {
                throw new Exception("该项目已在列表中");
            }

            var projectInfo = await ParseProjectFile(projectPath);
            Projects.Add(projectInfo);
            await SaveProjects();

            return projectInfo;
        }

        public async Task RemoveProject(ProjectInfo project)
        {
            Projects.Remove(project);
            await SaveProjects();
        }

        public async Task RefreshAllProjects()
        {
            var projectsToRemove = new List<ProjectInfo>();

            foreach (var project in Projects)
            {
                if (File.Exists(project.ProjectPath))
                {
                    await RefreshProjectInfo(project);
                }
                else
                {
                    projectsToRemove.Add(project);
                }
            }

            // 移除不存在的项目
            foreach (var project in projectsToRemove)
            {
                Projects.Remove(project);
            }

            await SaveProjects();
        }

        public void UpdateProjectLastUsed(ProjectInfo project)
        {
            project.LastUsed = DateTime.Now;
            _ = SaveProjects(); // 异步保存，不等待
        }

        private async Task<ProjectInfo> ParseProjectFile(string projectPath)
        {
            var projectInfo = new ProjectInfo
            {
                ProjectPath = projectPath,
                LastModified = File.GetLastWriteTime(projectPath)
            };

            try
            {
                var content = await File.ReadAllTextAsync(projectPath);
                var projectData = JsonSerializer.Deserialize<ProjectInfo>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (projectData != null)
                {
                    projectInfo.FileVersion = projectData.FileVersion;
                    projectInfo.EngineAssociation = projectData.EngineAssociation;
                    projectInfo.Category = projectData.Category;
                    projectInfo.Description = projectData.Description;
                    projectInfo.FriendlyName = projectData.FriendlyName;
                    projectInfo.Modules = projectData.Modules ?? new List<ProjectModule>();
                    projectInfo.Plugins = projectData.Plugins ?? new List<ProjectPlugin>();
                    projectInfo.TargetPlatforms = projectData.TargetPlatforms;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to parse project file: {ex.Message}");
            }

            await RefreshProjectInfo(projectInfo);
            await AssociateEngine(projectInfo);

            return projectInfo;
        }

        private async Task RefreshProjectInfo(ProjectInfo project)
        {
            try
            {
                // 更新文件修改时间
                if (File.Exists(project.ProjectPath))
                {
                    project.LastModified = File.GetLastWriteTime(project.ProjectPath);
                }

                // 计算项目大小（异步，不阻塞）
                if (!string.IsNullOrEmpty(project.ProjectDirectory))
                {
                    _ = Task.Run(async () =>
                    {
                        project.ProjectSize = await CalculateDirectorySize(project.ProjectDirectory);
                    });
                }

                // 刷新缩略图
                project.RefreshThumbnail();

                // 验证项目
                project.ValidateProject();

                // 检查Git状态
                project.CheckGitStatus();

                System.Diagnostics.Debug.WriteLine($"刷新项目信息成功: {project.ProjectName}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to refresh project info: {ex.Message}");
            }
        }

        private async Task AssociateEngine(ProjectInfo project)
        {
            try
            {
                if (!string.IsNullOrEmpty(project.EngineAssociation))
                {
                    var engineManager = EngineManagerService.Instance;
                    await engineManager.LoadEngines();

                    var engine = engineManager.GetEngineByVersion(project.EngineAssociation);
                    if (engine != null)
                    {
                        project.AssociatedEngine = engine;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to associate engine: {ex.Message}");
            }
        }

        private async Task<long> CalculateDirectorySize(string directoryPath)
        {
            try
            {
                if (!Directory.Exists(directoryPath))
                    return 0;

                return await Task.Run(() =>
                {
                    var dirInfo = new DirectoryInfo(directoryPath);
                    long size = 0;

                    try
                    {
                        // 计算文件大小
                        var files = dirInfo.GetFiles("*", SearchOption.AllDirectories);
                        size = files.Sum(file => file.Length);
                    }
                    catch
                    {
                        // 如果访问被拒绝，返回0
                        size = 0;
                    }

                    return size;
                });
            }
            catch
            {
                return 0;
            }
        }
    }
}