using System.Text.Json.Serialization;

namespace DreamUnrealManager.Models
{
    public class BuildVersionInfo
    {
        [JsonPropertyName("MajorVersion")]
        public int MajorVersion
        {
            get;
            set;
        }

        [JsonPropertyName("MinorVersion")]
        public int MinorVersion
        {
            get;
            set;
        }

        [JsonPropertyName("PatchVersion")]
        public int PatchVersion
        {
            get;
            set;
        }

        [JsonPropertyName("Changelist")]
        public long Changelist
        {
            get;
            set;
        }

        [JsonPropertyName("CompatibleChangelist")]
        public long CompatibleChangelist
        {
            get;
            set;
        }

        [JsonPropertyName("IsLicenseeVersion")]
        public int IsLicenseeVersion
        {
            get;
            set;
        }

        [JsonPropertyName("IsPromotedBuild")]
        public int IsPromotedBuild
        {
            get;
            set;
        }

        [JsonPropertyName("BranchName")]
        public string BranchName
        {
            get;
            set;
        }

        /// <summary>
        /// 获取完整版本字符串，例如：5.4.4
        /// </summary>
        public string GetFullVersionString()
        {
            return $"{MajorVersion}.{MinorVersion}.{PatchVersion}";
        }

        /// <summary>
        /// 获取简短版本字符串，例如：5.4
        /// </summary>
        public string GetShortVersionString()
        {
            return $"{MajorVersion}.{MinorVersion}";
        }

        /// <summary>
        /// 获取显示版本字符串，例如：UE 5.4.4
        /// </summary>
        public string GetDisplayVersionString()
        {
            return $"UE {GetFullVersionString()}";
        }
    }
}