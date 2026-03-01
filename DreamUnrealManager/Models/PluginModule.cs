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
        } = string.Empty;

        [JsonPropertyName("Type")]
        public string Type
        {
            get;
            set;
        } = string.Empty;

        [JsonPropertyName("LoadingPhase")]
        public string LoadingPhase
        {
            get;
            set;
        } = string.Empty;

        [JsonPropertyName("WhitelistPlatforms")]
        public List<string> WhitelistPlatforms
        {
            get;
            set;
        } = new();

        [JsonPropertyName("BlacklistPlatforms")]
        public List<string> BlacklistPlatforms
        {
            get;
            set;
        } = new();
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
        } = string.Empty;

        [JsonPropertyName("FriendlyName")]
        public string FriendlyName
        {
            get;
            set;
        } = string.Empty;

        [JsonPropertyName("Description")]
        public string Description
        {
            get;
            set;
        } = string.Empty;

        [JsonPropertyName("Category")]
        public string Category
        {
            get;
            set;
        } = string.Empty;

        [JsonPropertyName("CreatedBy")]
        public string CreatedBy
        {
            get;
            set;
        } = string.Empty;

        [JsonPropertyName("CreatedByURL")]
        public string CreatedByURL
        {
            get;
            set;
        } = string.Empty;

        [JsonPropertyName("DocsURL")]
        public string DocsURL
        {
            get;
            set;
        } = string.Empty;

        [JsonPropertyName("MarketplaceURL")]
        public string MarketplaceURL
        {
            get;
            set;
        } = string.Empty;

        [JsonPropertyName("SupportURL")]
        public string SupportURL
        {
            get;
            set;
        } = string.Empty;

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
        } = string.Empty;

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
