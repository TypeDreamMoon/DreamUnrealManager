using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DreamUnrealManager.Models;
using Windows.Storage;

namespace DreamUnrealManager.Services
{
    public class ProjectDataService
    {
        private static readonly Lazy<ProjectDataService> _instance = new(() => new ProjectDataService());
        public static ProjectDataService Instance => _instance.Value;

        private const string ProjectsFileName = "projects.json";
        private const string BackupFileName = "projects_backup.json";

        private ProjectDataService() { }

        // 保存项目数据的数据传输对象
        public class ProjectDataDto
        {
            public string ProjectPath { get; set; } = "";
            public string DisplayName { get; set; } = "";
            public string ProjectDirectory { get; set; } = "";
            public string EngineAssociation { get; set; } = "";
            public string Description { get; set; } = "";
            public string Category { get; set; } = "";
            public DateTime LastModified { get; set; }
            public DateTime? LastUsed { get; set; }
            public long ProjectSize { get; set; }
            public Dictionary<string, object> AdditionalProperties { get; set; } = new();
        }

        public class ProjectsData
        {
            public List<ProjectDataDto> Projects { get; set; } = new();
            public DateTime LastSaved { get; set; }
            public int Version { get; set; } = 1;
        }

        /// <summary>
        /// 保存项目列表到本地存储
        /// </summary>
        public async Task<bool> SaveProjectsAsync(List<ProjectInfo> projects)
        {
            try
            {
                WriteDebug("开始保存项目数据");

                var projectsData = new ProjectsData
                {
                    Projects = projects.Select(p => new ProjectDataDto
                    {
                        ProjectPath = p.ProjectPath ?? "",
                        DisplayName = p.DisplayName ?? "",
                        ProjectDirectory = p.ProjectDirectory ?? "",
                        EngineAssociation = p.EngineAssociation ?? "",
                        Description = p.Description ?? "",
                        Category = p.Category ?? "",
                        LastModified = p.LastModified,
                        LastUsed = p.LastUsed,
                        ProjectSize = p.ProjectSize,
                        AdditionalProperties = new Dictionary<string, object>()
                    }).ToList(),
                    LastSaved = DateTime.Now,
                    Version = 1
                };

                var localFolder = ApplicationData.Current.LocalFolder;
                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                var jsonString = JsonSerializer.Serialize(projectsData, jsonOptions);

                // 先保存备份文件
                try
                {
                    var existingFile = await localFolder.TryGetItemAsync(ProjectsFileName);
                    if (existingFile != null)
                    {
                        var backupFile = await localFolder.CreateFileAsync(BackupFileName, CreationCollisionOption.ReplaceExisting);
                        var existingContent = await FileIO.ReadTextAsync((StorageFile)existingFile);
                        await FileIO.WriteTextAsync(backupFile, existingContent);
                        WriteDebug("备份文件已创建");
                    }
                }
                catch (Exception ex)
                {
                    WriteDebug($"创建备份文件失败: {ex.Message}");
                }

                // 保存新文件
                var file = await localFolder.CreateFileAsync(ProjectsFileName, CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file, jsonString);

                WriteDebug($"项目数据保存成功，共 {projects.Count} 个项目");
                return true;
            }
            catch (Exception ex)
            {
                WriteDebug($"保存项目数据失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 从本地存储加载项目列表
        /// </summary>
        public async Task<List<ProjectInfo>> LoadProjectsAsync()
        {
            try
            {
                WriteDebug("开始加载项目数据");

                var localFolder = ApplicationData.Current.LocalFolder;
                var file = await localFolder.TryGetItemAsync(ProjectsFileName) as StorageFile;

                if (file == null)
                {
                    WriteDebug("未找到项目数据文件，尝试迁移旧数据");
                    return await MigrateFromOldFormat();
                }

                var jsonString = await FileIO.ReadTextAsync(file);
                if (string.IsNullOrWhiteSpace(jsonString))
                {
                    WriteDebug("项目数据文件为空");
                    return new List<ProjectInfo>();
                }

                var projectsData = JsonSerializer.Deserialize<ProjectsData>(jsonString);
                if (projectsData?.Projects == null)
                {
                    WriteDebug("项目数据格式错误");
                    return new List<ProjectInfo>();
                }

                var projects = new List<ProjectInfo>();
                foreach (var dto in projectsData.Projects)
                {
                    if (File.Exists(dto.ProjectPath))
                    {
                        var projectInfo = new ProjectInfo
                        {
                            ProjectPath = dto.ProjectPath,
                            DisplayName = dto.DisplayName,
                            ProjectDirectory = dto.ProjectDirectory,
                            EngineAssociation = dto.EngineAssociation,
                            Description = dto.Description,
                            Category = dto.Category,
                            LastModified = dto.LastModified,
                            LastUsed = dto.LastUsed,
                            ProjectSize = dto.ProjectSize
                        };

                        // 更新文件修改时间（如果文件已更改）
                        var actualModified = File.GetLastWriteTime(dto.ProjectPath);
                        if (actualModified != dto.LastModified)
                        {
                            projectInfo.LastModified = actualModified;
                        }

                        projects.Add(projectInfo);
                        WriteDebug($"加载项目: {projectInfo.DisplayName}");
                    }
                    else
                    {
                        WriteDebug($"项目文件不存在，跳过: {dto.ProjectPath}");
                    }
                }

                WriteDebug($"项目数据加载完成，共 {projects.Count} 个有效项目");
                return projects;
            }
            catch (Exception ex)
            {
                WriteDebug($"加载项目数据失败: {ex.Message}，尝试使用备份");
                return await LoadFromBackup();
            }
        }

        /// <summary>
        /// 从备份文件恢复
        /// </summary>
        private async Task<List<ProjectInfo>> LoadFromBackup()
        {
            try
            {
                var localFolder = ApplicationData.Current.LocalFolder;
                var backupFile = await localFolder.TryGetItemAsync(BackupFileName) as StorageFile;

                if (backupFile == null)
                {
                    WriteDebug("备份文件不存在");
                    return await MigrateFromOldFormat();
                }

                var jsonString = await FileIO.ReadTextAsync(backupFile);
                var projectsData = JsonSerializer.Deserialize<ProjectsData>(jsonString);

                if (projectsData?.Projects == null)
                {
                    return await MigrateFromOldFormat();
                }

                var projects = projectsData.Projects
                    .Where(dto => File.Exists(dto.ProjectPath))
                    .Select(dto => new ProjectInfo
                    {
                        ProjectPath = dto.ProjectPath,
                        DisplayName = dto.DisplayName,
                        ProjectDirectory = dto.ProjectDirectory,
                        EngineAssociation = dto.EngineAssociation,
                        Description = dto.Description,
                        Category = dto.Category,
                        LastModified = dto.LastModified,
                        LastUsed = dto.LastUsed,
                        ProjectSize = dto.ProjectSize
                    }).ToList();

                WriteDebug($"从备份恢复了 {projects.Count} 个项目");
                return projects;
            }
            catch (Exception ex)
            {
                WriteDebug($"从备份恢复失败: {ex.Message}");
                return await MigrateFromOldFormat();
            }
        }

        /// <summary>
        /// 从旧格式迁移数据
        /// </summary>
        private async Task<List<ProjectInfo>> MigrateFromOldFormat()
        {
            try
            {
                WriteDebug("尝试从旧格式迁移项目数据");

                var localSettings = ApplicationData.Current.LocalSettings;
                if (!localSettings.Values.ContainsKey("ProjectsData"))
                {
                    WriteDebug("未找到旧格式数据");
                    return new List<ProjectInfo>();
                }

                var savedData = localSettings.Values["ProjectsData"] as string;
                if (string.IsNullOrEmpty(savedData))
                {
                    return new List<ProjectInfo>();
                }

                var projectPaths = savedData.Split('|');
                var projects = new List<ProjectInfo>();

                foreach (var path in projectPaths)
                {
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        try
                        {
                            var projectInfo = await CreateProjectInfoFromPath(path);
                            if (projectInfo != null)
                            {
                                projects.Add(projectInfo);
                            }
                        }
                        catch (Exception ex)
                        {
                            WriteDebug($"迁移项目失败 {path}: {ex.Message}");
                        }
                    }
                }

                // 保存到新格式
                if (projects.Count > 0)
                {
                    await SaveProjectsAsync(projects);
                    WriteDebug($"成功迁移 {projects.Count} 个项目到新格式");
                }

                return projects;
            }
            catch (Exception ex)
            {
                WriteDebug($"迁移旧数据失败: {ex.Message}");
                return new List<ProjectInfo>();
            }
        }

        /// <summary>
        /// 从项目路径创建项目信息
        /// </summary>
        private async Task<ProjectInfo> CreateProjectInfoFromPath(string projectPath)
        {
            try
            {
                var projectInfo = new ProjectInfo
                {
                    ProjectPath = projectPath,
                    DisplayName = Path.GetFileNameWithoutExtension(projectPath),
                    ProjectDirectory = Path.GetDirectoryName(projectPath),
                    LastModified = File.GetLastWriteTime(projectPath)
                };

                // 尝试解析项目文件
                try
                {
                    var content = await File.ReadAllTextAsync(projectPath);
                    using var jsonDoc = JsonDocument.Parse(content);
                    var root = jsonDoc.RootElement;

                    if (root.TryGetProperty("EngineAssociation", out var engineAssoc))
                    {
                        projectInfo.EngineAssociation = engineAssoc.GetString() ?? "";
                    }

                    if (root.TryGetProperty("Description", out var desc))
                    {
                        projectInfo.Description = desc.GetString() ?? "";
                    }

                    if (root.TryGetProperty("Category", out var cat))
                    {
                        projectInfo.Category = cat.GetString() ?? "";
                    }
                }
                catch (Exception ex)
                {
                    WriteDebug($"解析项目文件失败，使用默认值: {ex.Message}");
                    projectInfo.EngineAssociation = "未知版本";
                    projectInfo.Description = "无法读取项目描述";
                }

                // 计算项目大小（异步）
                _ = Task.Run(async () =>
                {
                    try
                    {
                        projectInfo.ProjectSize = await CalculateProjectSize(projectInfo.ProjectDirectory);
                    }
                    catch
                    {
                        projectInfo.ProjectSize = 0;
                    }
                });

                return projectInfo;
            }
            catch (Exception ex)
            {
                WriteDebug($"CreateProjectInfoFromPath失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 计算项目大小
        /// </summary>
        private async Task<long> CalculateProjectSize(string projectDirectory)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (string.IsNullOrEmpty(projectDirectory) || !Directory.Exists(projectDirectory))
                        return 0;

                    var dirInfo = new DirectoryInfo(projectDirectory);
                    return GetDirectorySize(dirInfo);
                }
                catch
                {
                    return 0;
                }
            });
        }

        private long GetDirectorySize(DirectoryInfo dir)
        {
            long size = 0;
            try
            {
                // 跳过一些不必要的文件夹以提高性能
                if (dir.Name.Equals("Intermediate", StringComparison.OrdinalIgnoreCase) ||
                    dir.Name.Equals("Binaries", StringComparison.OrdinalIgnoreCase) ||
                    dir.Name.Equals(".vs", StringComparison.OrdinalIgnoreCase))
                {
                    return 0;
                }

                FileInfo[] files = dir.GetFiles();
                foreach (FileInfo file in files)
                {
                    size += file.Length;
                }

                DirectoryInfo[] dirs = dir.GetDirectories();
                foreach (DirectoryInfo subDir in dirs)
                {
                    size += GetDirectorySize(subDir);
                }
            }
            catch
            {
                // 忽略访问权限问题
            }

            return size;
        }

        /// <summary>
        /// 清理过期的项目数据
        /// </summary>
        public async Task<int> CleanupInvalidProjectsAsync(List<ProjectInfo> projects)
        {
            var validProjects = projects.Where(p => File.Exists(p.ProjectPath)).ToList();
            var removedCount = projects.Count - validProjects.Count;

            if (removedCount > 0)
            {
                await SaveProjectsAsync(validProjects);
                WriteDebug($"清理了 {removedCount} 个无效项目");
            }

            return removedCount;
        }

        /// <summary>
        /// 导出项目列表
        /// </summary>
        public async Task<bool> ExportProjectsAsync(List<ProjectInfo> projects, StorageFile file)
        {
            try
            {
                var projectsData = new ProjectsData
                {
                    Projects = projects.Select(p => new ProjectDataDto
                    {
                        ProjectPath = p.ProjectPath ?? "",
                        DisplayName = p.DisplayName ?? "",
                        ProjectDirectory = p.ProjectDirectory ?? "",
                        EngineAssociation = p.EngineAssociation ?? "",
                        Description = p.Description ?? "",
                        Category = p.Category ?? "",
                        LastModified = p.LastModified,
                        LastUsed = p.LastUsed,
                        ProjectSize = p.ProjectSize
                    }).ToList(),
                    LastSaved = DateTime.Now,
                    Version = 1
                };

                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                var jsonString = JsonSerializer.Serialize(projectsData, jsonOptions);
                await FileIO.WriteTextAsync(file, jsonString);

                WriteDebug($"导出了 {projects.Count} 个项目");
                return true;
            }
            catch (Exception ex)
            {
                WriteDebug($"导出项目失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 导入项目列表
        /// </summary>
        public async Task<List<ProjectInfo>> ImportProjectsAsync(StorageFile file)
        {
            try
            {
                var jsonString = await FileIO.ReadTextAsync(file);
                var projectsData = JsonSerializer.Deserialize<ProjectsData>(jsonString);

                if (projectsData?.Projects == null)
                {
                    throw new InvalidDataException("导入的文件格式不正确");
                }

                var projects = projectsData.Projects
                    .Where(dto => File.Exists(dto.ProjectPath))
                    .Select(dto => new ProjectInfo
                    {
                        ProjectPath = dto.ProjectPath,
                        DisplayName = dto.DisplayName,
                        ProjectDirectory = dto.ProjectDirectory,
                        EngineAssociation = dto.EngineAssociation,
                        Description = dto.Description,
                        Category = dto.Category,
                        LastModified = dto.LastModified,
                        LastUsed = dto.LastUsed,
                        ProjectSize = dto.ProjectSize
                    }).ToList();

                WriteDebug($"导入了 {projects.Count} 个项目");
                return projects;
            }
            catch (Exception ex)
            {
                WriteDebug($"导入项目失败: {ex.Message}");
                throw;
            }
        }

        private void WriteDebug(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[ProjectDataService] {message}");
        }
    }
}
