using System.Text.Json;
using DreamUnrealManager.Models;
using Windows.Storage;
using DreamUnrealManager.Contracts.Services;

namespace DreamUnrealManager.Services
{
    /// <summary>
    /// 项目数据读写服务实现。
    /// 兼容：仍保留 Instance 单例；推荐：通过依赖注入注入 IProjectDataService 使用。
    /// </summary>
    public class ProjectDataService : IProjectDataService
    {
        // =========================
        // 兼容旧代码的单例入口
        // =========================
        private static readonly Lazy<ProjectDataService> _instance = new(() => new ProjectDataService());

        [Obsolete("建议通过依赖注入使用 IProjectDataService。临时兼容旧代码可用此属性。")]
        public static ProjectDataService Instance => _instance.Value;

        private const string ProjectsFileName = "projects.json";
        private const string BackupFileName = "projects_backup.json";

        // 为支持 DI，将构造函数设为 public；依旧允许通过 Instance 获取单例以兼容旧代码
        public ProjectDataService() { }

        // ====== 以下为内部使用的数据结构（实现细节，不暴露到接口） ======

        // 保存项目数据的数据传输对象
        internal class ProjectDataDto
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
            public bool IsFavorite { get; set; }
        }

        internal class ProjectsData
        {
            public List<ProjectDataDto> Projects { get; set; } = new();
            public DateTime LastSaved { get; set; }
            public int Version { get; set; } = 1;
        }

        /// <summary>
        /// 获取应用程序数据目录
        /// </summary>
        private async Task<string> GetAppDataDirectoryAsync()
        {
            try
            {
                var localFolder = ApplicationData.Current.LocalFolder;
                WriteDebug($"使用标准 ApplicationData 路径: {localFolder.Path}");
                return localFolder.Path;
            }
            catch (Exception ex)
            {
                WriteDebug($"无法访问 ApplicationData，使用备用方法: {ex.Message}");

                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var appDirectory = Path.Combine(appDataPath, "DreamUnrealManager");

                if (!Directory.Exists(appDirectory))
                {
                    Directory.CreateDirectory(appDirectory);
                    WriteDebug($"创建应用程序数据目录: {appDirectory}");
                }

                WriteDebug($"使用备用数据路径: {appDirectory}");
                return appDirectory;
            }
        }

        /// <inheritdoc />
        public async Task<bool> SaveProjectsAsync(List<ProjectInfo> projects)
        {
            try
            {
                projects ??= new List<ProjectInfo>();
                WriteDebug($"开始保存项目数据，共 {projects.Count} 个项目");

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
                        AdditionalProperties = new Dictionary<string, object>(),
                        IsFavorite = p.IsFavorite
                    }).ToList(),
                    LastSaved = DateTime.Now,
                    Version = 1
                };

                WriteDebug($"创建项目数据对象，包含 {projectsData.Projects.Count} 个项目");

                var appDataDirectory = await GetAppDataDirectoryAsync();
                var filePath = Path.Combine(appDataDirectory, ProjectsFileName);
                var backupPath = Path.Combine(appDataDirectory, BackupFileName);

                WriteDebug($"目标文件路径: {filePath}");

                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                var jsonString = JsonSerializer.Serialize(projectsData, jsonOptions);
                WriteDebug($"JSON 序列化完成，长度: {jsonString.Length}");

                // 先保存备份文件
                try
                {
                    if (File.Exists(filePath))
                    {
                        var existingContent = await File.ReadAllTextAsync(filePath);
                        await File.WriteAllTextAsync(backupPath, existingContent);
                        WriteDebug("备份文件已创建");
                    }
                    else
                    {
                        WriteDebug("没有找到现有文件，跳过备份");
                    }
                }
                catch (Exception ex)
                {
                    WriteDebug($"创建备份文件失败: {ex.Message}");
                }

                // 保存新文件
                await File.WriteAllTextAsync(filePath, jsonString);
                WriteDebug($"文件写入完成: {filePath}");

                // 立即验证文件是否写入成功
                try
                {
                    var verifyContent = await File.ReadAllTextAsync(filePath);
                    WriteDebug($"验证文件内容，长度: {verifyContent.Length}");
                    if (verifyContent.Length != jsonString.Length)
                    {
                        WriteDebug($"警告：保存的文件长度不匹配！期望: {jsonString.Length}, 实际: {verifyContent.Length}");
                    }
                }
                catch (Exception verifyEx)
                {
                    WriteDebug($"验证保存的文件失败: {verifyEx.Message}");
                }

                WriteDebug($"项目数据保存成功，共 {projects.Count} 个项目");
                return true;
            }
            catch (Exception ex)
            {
                WriteDebug($"保存项目数据失败: {ex.Message}");
                WriteDebug($"堆栈跟踪: {ex.StackTrace}");
                return false;
            }
        }

        /// <inheritdoc />
        public async Task<List<ProjectInfo>> LoadProjectsAsync()
        {
            try
            {
                WriteDebug("开始加载项目数据");

                var appDataDirectory = await GetAppDataDirectoryAsync();
                var filePath = Path.Combine(appDataDirectory, ProjectsFileName);

                WriteDebug($"项目数据文件路径: {filePath}");

                if (!File.Exists(filePath))
                {
                    WriteDebug("未找到项目数据文件，尝试迁移旧数据");
                    return await MigrateFromOldFormat();
                }

                WriteDebug($"找到项目数据文件: {filePath}");
                WriteDebug($"文件最后修改时间: {File.GetLastWriteTime(filePath)}");

                var jsonString = await File.ReadAllTextAsync(filePath);
                WriteDebug($"读取文件内容，长度: {jsonString.Length}");

                if (string.IsNullOrWhiteSpace(jsonString))
                {
                    WriteDebug("项目数据文件为空");
                    return new List<ProjectInfo>();
                }

                var projectsData = JsonSerializer.Deserialize<ProjectsData>(jsonString);
                if (projectsData?.Projects == null)
                {
                    WriteDebug("项目数据格式错误或为空");
                    return new List<ProjectInfo>();
                }

                WriteDebug($"JSON 解析成功，找到 {projectsData.Projects.Count} 个项目记录");

                var projects = new List<ProjectInfo>();
                foreach (var dto in projectsData.Projects)
                {
                    WriteDebug($"处理项目记录: {dto.DisplayName} - {dto.ProjectPath}");

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
                            ProjectSize = dto.ProjectSize,
                            IsFavorite = dto.IsFavorite
                        };

                        // 更新文件修改时间（如果文件已更改）
                        var actualModified = File.GetLastWriteTime(dto.ProjectPath);
                        if (actualModified != dto.LastModified)
                        {
                            projectInfo.LastModified = actualModified;
                            WriteDebug($"项目文件已更新: {projectInfo.DisplayName}");
                        }

                        projects.Add(projectInfo);
                        WriteDebug($"加载项目成功: {projectInfo.DisplayName}");
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
                WriteDebug($"加载项目数据失败: {ex.Message}");
                WriteDebug($"堆栈跟踪: {ex.StackTrace}");
                WriteDebug("尝试使用备份");
                return await LoadFromBackup();
            }
        }

        /// <inheritdoc />
        public async Task<int> CleanupInvalidProjectsAsync(List<ProjectInfo> projects)
        {
            projects ??= new List<ProjectInfo>();

            var validProjects = projects.Where(p => p != null && File.Exists(p.ProjectPath)).ToList();
            var removedCount = projects.Count - validProjects.Count;

            if (removedCount > 0)
            {
                await SaveProjectsAsync(validProjects);
                WriteDebug($"清理了 {removedCount} 个无效项目");
            }

            return removedCount;
        }

        // =========================
        // 以下为实现细节（私有方法）
        // =========================

        private async Task<List<ProjectInfo>> LoadFromBackup()
        {
            try
            {
                var appDataDirectory = await GetAppDataDirectoryAsync();
                var backupPath = Path.Combine(appDataDirectory, BackupFileName);

                if (!File.Exists(backupPath))
                {
                    WriteDebug("备份文件不存在");
                    return await MigrateFromOldFormat();
                }

                var jsonString = await File.ReadAllTextAsync(backupPath);
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

        private async Task<List<ProjectInfo>> MigrateFromOldFormat()
        {
            try
            {
                WriteDebug("尝试从旧格式迁移项目数据");

                // 尝试从注册表或其他位置读取旧数据
                try
                {
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
                    WriteDebug($"无法访问 ApplicationData 进行迁移: {ex.Message}");
                    return new List<ProjectInfo>();
                }
            }
            catch (Exception ex)
            {
                WriteDebug($"迁移旧数据失败: {ex.Message}");
                return new List<ProjectInfo>();
            }
        }

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

                return projectInfo;
            }
            catch (Exception ex)
            {
                WriteDebug($"CreateProjectInfoFromPath失败: {ex.Message}");
                return null;
            }
        }

        private void WriteDebug(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[ProjectDataService] {message}");
        }
    }
}
