using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Windows.System;
using DreamUnrealManager.Services;
using Microsoft.UI.Xaml;

namespace DreamUnrealManager.Models
{
    public class ProjectInfo : INotifyPropertyChanged
    {
        // ╭─ 基础数据（可序列化） ─────────────────────────────────────────────╮
        [JsonPropertyName("FileVersion")]
        public int FileVersion
        {
            get;
            set;
        }

        [JsonPropertyName("EngineAssociation")]
        public string EngineAssociation
        {
            get;
            set;
        }

        [JsonPropertyName("Category")]
        public string Category
        {
            get;
            set;
        }

        [JsonPropertyName("Description")]
        public string Description
        {
            get;
            set;
        }

        [JsonPropertyName("FriendlyName")]
        public string FriendlyName
        {
            get;
            set;
        }

        [JsonPropertyName("Modules")]
        public List<ProjectModule> Modules
        {
            get;
            set;
        } = new();

        [JsonPropertyName("Plugins")]
        public List<ProjectPlugin> Plugins
        {
            get;
            set;
        } = new();

        [JsonPropertyName("TargetPlatforms")]
        public string[] TargetPlatforms
        {
            get;
            set;
        }

        [JsonPropertyName("IsFavorite")]
        public bool IsFavorite
        {
            get => _isFavorite;
            set
            {
                if (_isFavorite != value)
                {
                    _isFavorite = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FavoriteGlyph));
                }
            }
        }

        private bool _isFavorite;

        // 路径信息
        public string ProjectPath
        {
            get;
            set;
        }

        public string ProjectDirectory
        {
            get => !string.IsNullOrEmpty(_projectDirectory) ? _projectDirectory : Path.GetDirectoryName(ProjectPath);
            set => _projectDirectory = value;
        }

        private string _projectDirectory;

        public string ProjectName => Path.GetFileNameWithoutExtension(ProjectPath);

        // 可编辑显示名（优先级：显式设置 > FriendlyName > 文件名）
        private string _displayName;

        public string DisplayName
        {
            get => !string.IsNullOrEmpty(_displayName)
                ? _displayName
                : (!string.IsNullOrEmpty(FriendlyName) ? FriendlyName : ProjectName);
            set
            {
                _displayName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayName));
            }
        }

        public DateTime LastModified
        {
            get;
            set;
        }

        public DateTime? LastUsed
        {
            get;
            set;
        }

        // 项目体积（字节）
        private long _projectSize;

        public long ProjectSize
        {
            get => _projectSize;
            set
            {
                _projectSize = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ProjectSizeString));
            }
        }

        public bool IsValid
        {
            get;
            set;
        } = true;

        // ╭─ 运行期/UI 专用（不序列化） ───────────────────────────────────────╮
        public event PropertyChangedEventHandler PropertyChanged;

        // 引擎对象（运行期注入）
        [JsonIgnore]
        public UnrealEngineInfo AssociatedEngine
        {
            get;
            set;
        }

        // UI 状态：阶段 A（元数据）、阶段 B（体积）
        [JsonIgnore]
        public bool IsLoadingMeta
        {
            get => _isLoadingMeta;
            set
            {
                _isLoadingMeta = value;
                OnPropertyChanged();
            }
        }

        private bool _isLoadingMeta;

        [JsonIgnore]
        public bool IsSizing
        {
            get => _isSizing;
            set
            {
                _isSizing = value;
                OnPropertyChanged();
            }
        }

        private bool _isSizing;

        // Git 状态
        [JsonIgnore]
        public bool IsGitEnabled
        {
            get => _isGitEnabled;
            set
            {
                _isGitEnabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(GitInfoString));
            }
        }

        private bool _isGitEnabled;

        [JsonIgnore]
        public long GitFolderSize
        {
            get => _gitFolderSize;
            set
            {
                _gitFolderSize = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(GitInfoString));
            }
        }

        private long _gitFolderSize;

        // 缩略图：Unpackaged 友好（file:///...）
        [JsonIgnore]
        public Uri ThumbnailUri
        {
            get;
            private set;
        }

        [JsonIgnore]
        public string ThumbnailPath
        {
            get;
            private set;
        } // 可选，保留兼容

        // 旧接口：仍可用（默认取 Saved/AutoScreenshot.png）
        [JsonIgnore] public string ProjectIconPath => Path.Combine(ProjectDirectory, "Saved", "AutoScreenshot.png");

        // ╭─ 显示用派生属性（只读，配合 NotifyDerived 一次性刷新） ───────────╮
        [JsonIgnore] public string EngineDisplayName => GetEngineDisplayName();
        [JsonIgnore] public string DescriptionDisplay => GetDescription();
        [JsonIgnore] public string LastModifiedString => LastModified.ToString("yyyy年MM月dd日 HH:mm:ss");
        [JsonIgnore] public string ProjectSizeString => GetProjectSizeString();
        [JsonIgnore] public string DetailedInfo => GetDetailedInfo();
        [JsonIgnore] public int ModulesCount => Modules?.Count ?? 0;
        [JsonIgnore] public int EnabledPluginsCount => Plugins?.Count(p => p.Enabled) ?? 0;
        [JsonIgnore] public string GitInfoString => GetGitInfoString();
        [JsonIgnore] public bool IsLoading => IsLoadingMeta || IsSizing;

        [JsonIgnore] public string FavoriteGlyph => IsFavorite ? "\uE735" /* FavoriteStarFill */ : "\uE734" /* FavoriteStar */;

        public void ToggleFavorite()
        {
            IsFavorite = !IsFavorite;
        }

        // ╭─ 派生文本实现 ───────────────────────────────────────────────────╮
        public string GetEngineDisplayName()
        {
            if (AssociatedEngine != null) return AssociatedEngine.DisplayName;
            if (!string.IsNullOrEmpty(EngineAssociation)) return $"UE {EngineAssociation}";
            return "未知引擎";
        }

        public string GetProjectSizeString()
        {
            if (ProjectSize <= 0) return "计算中...";
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = ProjectSize;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }

        public string GetDescription()
        {
            if (!string.IsNullOrWhiteSpace(Description)) return Description;

            var parts = new List<string>();
            if (!string.IsNullOrEmpty(EngineAssociation)) parts.Add($"UE {EngineAssociation} 项目");
            if (Modules?.Count > 0) parts.Add($"包含 {Modules.Count} 个模块");
            if (Plugins?.Any(p => p.Enabled) == true) parts.Add($"启用 {Plugins.Count(p => p.Enabled)} 个插件");
            return parts.Count > 0 ? string.Join(" · ", parts) : "Unreal Engine 项目";
        }

        public string GetDetailedInfo()
        {
            var info = new List<string>();
            if (!string.IsNullOrEmpty(EngineAssociation)) info.Add($"引擎版本: UE {EngineAssociation}");
            if (FileVersion > 0) info.Add($"文件版本: {FileVersion}");
            if (ModulesCount > 0) info.Add($"模块数量: {ModulesCount}");
            if (EnabledPluginsCount > 0) info.Add($"启用插件: {EnabledPluginsCount}");
            if (TargetPlatforms?.Length > 0) info.Add($"目标平台: {string.Join(", ", TargetPlatforms)}");
            return string.Join(" | ", info);
        }

        public string GetLastUsedString()
        {
            if (!LastUsed.HasValue) return "从未使用";
            var ts = DateTime.Now - LastUsed.Value;
            if (ts.TotalDays < 1) return $"{(int)ts.TotalHours} 小时前";
            if (ts.TotalDays < 7) return $"{(int)ts.TotalDays} 天前";
            if (ts.TotalDays < 30) return $"{(int)(ts.TotalDays / 7)} 周前";
            return LastUsed.Value.ToString("yyyy年MM月dd日");
        }

        public string GetMainPluginsList()
        {
            if (Plugins == null || Plugins.Count == 0) return "无插件";
            var names = Plugins.Where(p => p.Enabled).Select(p => p.Name).Take(3);
            var txt = string.Join(", ", names);
            var total = Plugins.Count(p => p.Enabled);
            if (total > 3) txt += $" 等 {total} 个插件";
            return string.IsNullOrEmpty(txt) ? "无启用插件" : txt;
        }

        public string GetGitInfoString()
        {
            if (!IsGitEnabled) return "未启用Git";
            if (GitFolderSize <= 0) return "Git已启用 (大小计算中...)";
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = GitFolderSize;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }

            return $"Git已启用 ({len:0.##} {sizes[order]})";
        }

        // ╭─ 行为 ───────────────────────────────────────────────────────────╮
        public void UpdateLastUsed()
        {
            LastUsed = DateTime.Now;
            OnPropertyChanged(nameof(LastUsed));
        }

        public void ValidateProject()
        {
            IsValid = !string.IsNullOrEmpty(ProjectPath) && File.Exists(ProjectPath);
            OnPropertyChanged(nameof(IsValid));
        }

        public void RefreshThumbnail()
        {
            if (!string.IsNullOrEmpty(ProjectDirectory))
            {
                var candidates = new[]
                {
                    Path.Combine(ProjectDirectory, "Saved", "AutoScreenshot.png"),
                    Path.Combine(ProjectDirectory, "Saved", "Screenshots", "Screenshot.png"),
                    Path.Combine(ProjectDirectory, "Content", "Splash", "Splash.png"),
                    Path.Combine(ProjectDirectory, "Content", "UI", "Splash.png"),
                    Path.Combine(ProjectDirectory, "thumbnail.png"),
                    Path.Combine(ProjectDirectory, "screenshot.png")
                };
                var physical = candidates.FirstOrDefault(File.Exists);
                ThumbnailUri = !string.IsNullOrEmpty(physical) ? new Uri(physical, UriKind.Absolute) : null;
                ThumbnailPath = physical;
                OnPropertyChanged(nameof(ThumbnailUri));
                OnPropertyChanged(nameof(ThumbnailPath));
            }

            CheckGitStatus();
        }

        public void CheckGitStatus()
        {
            try
            {
                if (string.IsNullOrEmpty(ProjectDirectory))
                {
                    IsGitEnabled = false;
                    GitFolderSize = 0;
                    return;
                }

                var git = Path.Combine(ProjectDirectory, ".git");
                bool exists;
                try
                {
                    exists = new DirectoryInfo(git).Exists;
                }
                catch
                {
                    exists = Directory.Exists(git);
                }

                IsGitEnabled = exists;

                if (IsGitEnabled)
                {
                    Task.Run(() =>
                    {
                        try
                        {
                            SetGitFolderSize(CalculateDirectorySize(git));
                        }
                        catch
                        {
                            SetGitFolderSize(0);
                        }
                    });
                }
                else
                {
                    SetGitFolderSize(0);
                }
            }
            catch
            {
                IsGitEnabled = false;
                SetGitFolderSize(0);
            }
        }

        // ╭─ “集中通知”工具 ─────────────────────────────────────────────────╮
        public void NotifyDerived()
        {
            if (UiDispatcher.Queue != null && !UiDispatcher.Queue.HasThreadAccess)
            {
                UiDispatcher.Queue.TryEnqueue(NotifyDerived);
                return;
            }

            OnPropertyChanged(nameof(EngineDisplayName));
            OnPropertyChanged(nameof(DescriptionDisplay));
            OnPropertyChanged(nameof(LastModifiedString));
            OnPropertyChanged(nameof(ProjectSizeString));
            OnPropertyChanged(nameof(DetailedInfo));
            OnPropertyChanged(nameof(ModulesCount));
            OnPropertyChanged(nameof(EnabledPluginsCount));
            OnPropertyChanged(nameof(GitInfoString));
        }


        public void UpdateFrom(ProjectInfo fresh)
        {
            // 把解析得到的新数据拷回当前实例
            EngineAssociation = fresh.EngineAssociation;
            Description = fresh.Description;
            Category = fresh.Category;
            Modules = fresh.Modules ?? new List<ProjectModule>();
            Plugins = fresh.Plugins ?? new List<ProjectPlugin>();
            LastModified = fresh.LastModified;
            AssociatedEngine = fresh.AssociatedEngine;

            // 通知派生属性刷新
            NotifyDerived();
        }

        public void SetProjectSize(long bytes)
        {
            ProjectSize = bytes; // 会触发 ProjectSizeString
        }

        public void SetGitFolderSize(long bytes)
        {
            GitFolderSize = bytes; // 会触发 GitInfoString
        }

        // ╭─ 私用 ───────────────────────────────────────────────────────────╮
        // protected virtual void OnPropertyChanged([CallerMemberName] string name = null)
        //     => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        protected virtual void OnPropertyChanged([CallerMemberName] string name = null)
        {
            if (UiDispatcher.Queue != null && !UiDispatcher.Queue.HasThreadAccess)
            {
                UiDispatcher.Queue.TryEnqueue(() =>
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)));
                return;
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private static long CalculateDirectorySize(string root)
        {
            long total = 0;
            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        total += new FileInfo(file).Length;
                    }
                    catch
                    {
                        /* ignore */
                    }
                }
            }
            catch
            {
                /* ignore */
            }

            return total;
        }

        public string GetIdeButtonText()
        {
            var defaultIde = Settings.Get("Default.IDE", "VS");
            return defaultIde switch
            {
                "VS" => "用 VS 打开",
                "RD" => "用 Rider 打开",
                "VSCode" => "用 VS Code 打开",
                _ => "用 IDE 打开"
            };
        }

        [JsonIgnore]
        public bool IsCPlusPlusProject
        {
            get
            {
                if (string.IsNullOrEmpty(ProjectDirectory)) return false;
                var src = Path.Combine(ProjectDirectory, "Source");
                return Directory.Exists(src);
            }
        }

        [JsonIgnore] public string ProjectSolutionPath => Path.Combine(ProjectDirectory, ProjectName + ".sln");
    }

    public class ProjectModule
    {
        [JsonPropertyName("Name")]
        public string Name
        {
            get;
            set;
        }

        [JsonPropertyName("Type")]
        public string Type
        {
            get;
            set;
        }

        [JsonPropertyName("LoadingPhase")]
        public string LoadingPhase
        {
            get;
            set;
        }

        [JsonPropertyName("AdditionalDependencies")]
        public string[] AdditionalDependencies
        {
            get;
            set;
        }
    }

    public class ProjectPlugin
    {
        [JsonPropertyName("Name")]
        public string Name
        {
            get;
            set;
        }

        [JsonPropertyName("Enabled")]
        public bool Enabled
        {
            get;
            set;
        }

        [JsonPropertyName("TargetAllowList")]
        public string[] TargetAllowList
        {
            get;
            set;
        }

        [JsonPropertyName("SupportedTargetPlatforms")]
        public string[] SupportedTargetPlatforms
        {
            get;
            set;
        }
    }
}