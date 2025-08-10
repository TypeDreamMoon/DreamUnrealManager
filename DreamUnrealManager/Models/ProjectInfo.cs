using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.Linq;

namespace DreamUnrealManager.Models
{
    public class ProjectInfo : INotifyPropertyChanged
    {
        [JsonPropertyName("FileVersion")]
        public int FileVersion { get; set; }

        [JsonPropertyName("EngineAssociation")]
        public string EngineAssociation { get; set; }

        [JsonPropertyName("Category")]
        public string Category { get; set; }

        [JsonPropertyName("Description")]
        public string Description { get; set; }

        [JsonPropertyName("FriendlyName")]
        public string FriendlyName { get; set; }

        [JsonPropertyName("Modules")]
        public List<ProjectModule> Modules { get; set; } = new List<ProjectModule>();

        [JsonPropertyName("Plugins")]
        public List<ProjectPlugin> Plugins { get; set; } = new List<ProjectPlugin>();

        [JsonPropertyName("TargetPlatforms")]
        public string[] TargetPlatforms { get; set; }

        // 项目路径信息
        public string ProjectPath { get; set; }

        public string ProjectName => Path.GetFileNameWithoutExtension(ProjectPath);

        // 修改为可读写属性
        private string _displayName;
        public string DisplayName 
        { 
            get => !string.IsNullOrEmpty(_displayName) ? _displayName : 
                   (!string.IsNullOrEmpty(FriendlyName) ? FriendlyName : ProjectName);
            set => _displayName = value;
        }

        private string _projectDirectory;
        public string ProjectDirectory 
        { 
            get => !string.IsNullOrEmpty(_projectDirectory) ? _projectDirectory : 
                   Path.GetDirectoryName(ProjectPath);
            set => _projectDirectory = value;
        }

        public DateTime LastModified { get; set; }

        // 添加 LastUsed 属性
        public DateTime? LastUsed { get; set; }

        public string ThumbnailPath { get; set; }

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

        public bool IsValid { get; set; } = true;

        // 引擎信息
        [JsonIgnore]
        public UnrealEngineInfo AssociatedEngine { get; set; }

        // 添加项目大小字符串属性
        public string ProjectSizeString => GetProjectSizeString();

        // 公共方法用于绑定
        public string GetEngineDisplayName()
        {
            if (AssociatedEngine != null)
                return AssociatedEngine.DisplayName;

            if (!string.IsNullOrEmpty(EngineAssociation))
                return $"UE {EngineAssociation}";

            return "未知引擎";
        }

        public string GetProjectSizeString()
        {
            if (ProjectSize == 0) return "计算中...";

            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = ProjectSize;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }

        public string GetDescription()
        {
            if (!string.IsNullOrEmpty(Description))
                return Description;
            
            // 如果没有描述，生成基于项目信息的描述
            var descriptionParts = new List<string>();
            
            // 添加引擎版本信息
            if (!string.IsNullOrEmpty(EngineAssociation))
            {
                descriptionParts.Add($"UE {EngineAssociation} 项目");
            }
            
            // 添加模块信息
            if (Modules != null && Modules.Count > 0)
            {
                descriptionParts.Add($"包含 {Modules.Count} 个模块");
            }
            
            // 添加插件信息
            if (Plugins != null && Plugins.Count > 0)
            {
                var enabledPlugins = Plugins.Where(p => p.Enabled).Count();
                if (enabledPlugins > 0)
                {
                    descriptionParts.Add($"启用 {enabledPlugins} 个插件");
                }
            }
            
            return descriptionParts.Any() ? string.Join(" · ", descriptionParts) : "Unreal Engine 项目";
        }

        /// <summary>
        /// 获取启用的插件数量 - 公共方法用于绑定
        /// </summary>
        public int GetEnabledPluginsCount()
        {
            return Plugins?.Count(p => p.Enabled) ?? 0;
        }

        /// <summary>
        /// 获取模块数量 - 公共方法用于绑定
        /// </summary>
        public int GetModulesCount()
        {
            return Modules?.Count ?? 0;
        }

        /// <summary>
        /// 获取格式化的最后使用时间
        /// </summary>
        public string GetLastUsedString()
        {
            if (!LastUsed.HasValue)
                return "从未使用";
            
            var timeSpan = DateTime.Now - LastUsed.Value;
            
            if (timeSpan.TotalDays < 1)
                return $"{(int)timeSpan.TotalHours} 小时前";
            else if (timeSpan.TotalDays < 7)
                return $"{(int)timeSpan.TotalDays} 天前";
            else if (timeSpan.TotalDays < 30)
                return $"{(int)(timeSpan.TotalDays / 7)} 周前";
            else
                return LastUsed.Value.ToString("yyyy年MM月dd日");
        }

        /// <summary>
        /// 获取项目的详细信息字符串
        /// </summary>
        public string GetDetailedInfo()
        {
            var info = new List<string>();
            
            if (!string.IsNullOrEmpty(EngineAssociation))
                info.Add($"引擎版本: UE {EngineAssociation}");
            
            if (FileVersion > 0)
                info.Add($"文件版本: {FileVersion}");
            
            var modulesCount = GetModulesCount();
            if (modulesCount > 0)
                info.Add($"模块数量: {modulesCount}");
            
            var pluginsCount = GetEnabledPluginsCount();
            if (pluginsCount > 0)
                info.Add($"启用插件: {pluginsCount}");
            
            if (TargetPlatforms?.Length > 0)
                info.Add($"目标平台: {string.Join(", ", TargetPlatforms)}");
            
            return string.Join(" | ", info);
        }

        /// <summary>
        /// 获取主要插件列表（仅显示名称）
        /// </summary>
        public string GetMainPluginsList()
        {
            if (Plugins == null || Plugins.Count == 0)
                return "无插件";
            
            var enabledPlugins = Plugins.Where(p => p.Enabled).Select(p => p.Name).Take(3);
            var pluginsList = string.Join(", ", enabledPlugins);
            
            var totalEnabledCount = Plugins.Count(p => p.Enabled);
            if (totalEnabledCount > 3)
                pluginsList += $" 等 {totalEnabledCount} 个插件";
            
            return pluginsList.Length > 0 ? pluginsList : "无启用插件";
        }

        /// <summary>
        /// 检查缩略图是否存在并更新路径
        /// </summary>
        public void RefreshThumbnail()
        {
            if (!string.IsNullOrEmpty(ProjectDirectory))
            {
                // 检查多个可能的缩略图路径
                var possiblePaths = new[]
                {
                    Path.Combine(ProjectDirectory, "Saved", "AutoScreenshot.png"),
                    Path.Combine(ProjectDirectory, "Saved", "Screenshots", "Screenshot.png"),
                    Path.Combine(ProjectDirectory, "Content", "Splash", "Splash.png"),
                    Path.Combine(ProjectDirectory, "Content", "UI", "Splash.png"),
                    Path.Combine(ProjectDirectory, "thumbnail.png"),
                    Path.Combine(ProjectDirectory, "screenshot.png")
                };

                ThumbnailPath = possiblePaths.FirstOrDefault(File.Exists);
            }
        }

        /// <summary>
        /// 更新最后使用时间
        /// </summary>
        public void UpdateLastUsed()
        {
            LastUsed = DateTime.Now;
            OnPropertyChanged(nameof(LastUsed));
        }

        /// <summary>
        /// 验证项目文件的有效性
        /// </summary>
        public void ValidateProject()
        {
            IsValid = !string.IsNullOrEmpty(ProjectPath) && File.Exists(ProjectPath);
            OnPropertyChanged(nameof(IsValid));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class ProjectModule
    {
        [JsonPropertyName("Name")]
        public string Name { get; set; }

        [JsonPropertyName("Type")]
        public string Type { get; set; }

        [JsonPropertyName("LoadingPhase")]
        public string LoadingPhase { get; set; }

        [JsonPropertyName("AdditionalDependencies")]
        public string[] AdditionalDependencies { get; set; }
    }

    public class ProjectPlugin
    {
        [JsonPropertyName("Name")]
        public string Name { get; set; }

        [JsonPropertyName("Enabled")]
        public bool Enabled { get; set; }

        [JsonPropertyName("TargetAllowList")]
        public string[] TargetAllowList { get; set; }

        [JsonPropertyName("SupportedTargetPlatforms")]
        public string[] SupportedTargetPlatforms { get; set; }
    }
}