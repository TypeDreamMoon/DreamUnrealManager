using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DreamUnrealManager.Models
{
    public class PluginModule
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

        [JsonPropertyName("WhitelistPlatforms")]
        public List<string> WhitelistPlatforms
        {
            get;
            set;
        }

        [JsonPropertyName("BlacklistPlatforms")]
        public List<string> BlacklistPlatforms
        {
            get;
            set;
        }
    }

    public class PluginInfo
    {
        [JsonPropertyName("FileVersion")]
        public int FileVersion
        {
            get;
            set;
        }

        [JsonPropertyName("Version")]
        public int Version
        {
            get;
            set;
        }

        [JsonPropertyName("VersionName")]
        public string VersionName
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

        [JsonPropertyName("Description")]
        public string Description
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

        [JsonPropertyName("CreatedBy")]
        public string CreatedBy
        {
            get;
            set;
        }

        [JsonPropertyName("CreatedByURL")]
        public string CreatedByURL
        {
            get;
            set;
        }

        [JsonPropertyName("DocsURL")]
        public string DocsURL
        {
            get;
            set;
        }

        [JsonPropertyName("MarketplaceURL")]
        public string MarketplaceURL
        {
            get;
            set;
        }

        [JsonPropertyName("SupportURL")]
        public string SupportURL
        {
            get;
            set;
        }

        [JsonPropertyName("CanContainContent")]
        public bool CanContainContent
        {
            get;
            set;
        }

        [JsonPropertyName("IsBetaVersion")]
        public bool IsBetaVersion
        {
            get;
            set;
        }

        [JsonPropertyName("IsExperimentalVersion")]
        public bool IsExperimentalVersion
        {
            get;
            set;
        }

        [JsonPropertyName("Installed")]
        public bool Installed
        {
            get;
            set;
        }

        [JsonPropertyName("Modules")]
        public List<PluginModule> Modules
        {
            get;
            set;
        } = new List<PluginModule>();

        [JsonPropertyName("Plugins")]
        public List<PluginDependency> Plugins
        {
            get;
            set;
        } = new List<PluginDependency>();

        /// <summary>
        /// 获取插件的显示名称
        /// </summary>
        public string GetDisplayName()
        {
            return !string.IsNullOrEmpty(FriendlyName) ? FriendlyName : "未命名插件";
        }

        /// <summary>
        /// 获取插件的版本字符串
        /// </summary>
        public string GetVersionString()
        {
            return !string.IsNullOrEmpty(VersionName) ? VersionName : Version.ToString();
        }

        /// <summary>
        /// 获取模块数量
        /// </summary>
        public int GetModuleCount()
        {
            return Modules?.Count ?? 0;
        }

        /// <summary>
        /// 检查是否包含运行时模块
        /// </summary>
        public bool HasRuntimeModules()
        {
            return Modules?.Any(m => m.Type == "Runtime" || m.Type == "RuntimeAndProgram") ?? false;
        }

        /// <summary>
        /// 检查是否包含编辑器模块
        /// </summary>
        public bool HasEditorModules()
        {
            return Modules?.Any(m => m.Type == "Editor" || m.Type == "UncookedOnly") ?? false;
        }
    }

    public class PluginDependency
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

        [JsonPropertyName("Optional")]
        public bool Optional
        {
            get;
            set;
        }
    }
}