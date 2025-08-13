using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DreamUnrealManager.Models
{
    public class UnrealEngineInfo : INotifyPropertyChanged
    {
        private string _displayName;
        private string _enginePath;
        private string _version;
        private string _fullVersion;
        private bool _isValid;
        private BuildVersionInfo _buildVersionInfo;

        public string Id
        {
            get;
            set;
        } = Guid.NewGuid().ToString();

        public string DisplayName
        {
            get => _displayName;
            set
            {
                _displayName = value;
                OnPropertyChanged();
            }
        }

        public string EnginePath
        {
            get => _enginePath;
            set
            {
                _enginePath = value;
                ValidateEngine();
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsValid));
                OnPropertyChanged(nameof(StatusText));
            }
        }

        /// <summary>
        /// 简短版本号，例如：5.4
        /// </summary>
        public string Version
        {
            get => _version;
            set
            {
                _version = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 完整版本号，例如：5.4.4
        /// </summary>
        public string FullVersion
        {
            get => _fullVersion;
            set
            {
                _fullVersion = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 构建版本信息对象
        /// </summary>
        public BuildVersionInfo BuildVersionInfo
        {
            get => _buildVersionInfo;
            set
            {
                _buildVersionInfo = value;
                OnPropertyChanged();
            }
        }

        public bool IsValid
        {
            get => _isValid;
            private set
            {
                _isValid = value;
                OnPropertyChanged();
            }
        }

        public string StatusText => IsValid ? "有效" : "无效路径";

        public string UATPath => Path.Combine(EnginePath ?? "", "Engine", "Build", "BatchFiles", "RunUAT.bat");

        public string UBTPath => Path.Combine(EnginePath ?? "", "Engine", "Binaries", "DotNET", "UnrealBuildTool",
            "UnrealBuildTool.exe");

        public string BuildVersionPath => Path.Combine(EnginePath ?? "", "Engine", "Build", "Build.version");

        public DateTime CreatedAt
        {
            get;
            set;
        } = DateTime.Now;

        public DateTime LastUsed
        {
            get;
            set;
        } = DateTime.Now;

        /// <summary>
        /// 获取用于显示的版本字符串
        /// </summary>
        public string GetDisplayVersion()
        {
            if (!string.IsNullOrEmpty(FullVersion))
            {
                return $"UE {FullVersion}";
            }
            else if (!string.IsNullOrEmpty(Version))
            {
                return $"UE {Version}";
            }

            return "未知版本";
        }

        private void ValidateEngine()
        {
            if (string.IsNullOrWhiteSpace(EnginePath) || !Directory.Exists(EnginePath))
            {
                IsValid = false;
                IsSourceBuild = false; // 保持状态一致
                return;
            }

            try
            {
                var engineDirExists = Directory.Exists(SafeCombine(EnginePath, "Engine"));
                var uatExists = File.Exists(UATPath); // 确保 UATPath 内部也用 SafeCombine

                IsValid = engineDirExists && uatExists;

                if (IsValid)
                {
                    // 先判源码版，再读版本
                    DetectSourceBuild();
                    DetectVersion();
                }
                else
                {
                    IsSourceBuild = false;
                }
            }
            catch
            {
                IsValid = false;
                IsSourceBuild = false;
            }
        }



        private void DetectVersion()
        {
            try
            {
                // 首先尝试读取 Build.version 文件
                if (File.Exists(BuildVersionPath))
                {
                    var content = File.ReadAllText(BuildVersionPath);
                    var buildVersionInfo = JsonSerializer.Deserialize<BuildVersionInfo>(content);

                    if (buildVersionInfo != null)
                    {
                        BuildVersionInfo = buildVersionInfo;
                        Version = buildVersionInfo.GetShortVersionString();
                        FullVersion = buildVersionInfo.GetFullVersionString();

                        // 如果显示名称为空，自动生成
                        if (string.IsNullOrWhiteSpace(DisplayName))
                        {
                            DisplayName = buildVersionInfo.GetDisplayVersionString();
                        }

                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                // JSON 解析失败，尝试简单的字符串匹配
                System.Diagnostics.Debug.WriteLine($"Failed to parse Build.version: {ex.Message}");
            }

            // 如果 JSON 解析失败，回退到简单的字符串匹配
            try
            {
                if (File.Exists(BuildVersionPath))
                {
                    var content = File.ReadAllText(BuildVersionPath);
                    ParseVersionFromString(content);
                }
                else
                {
                    // 根据文件夹名称推测版本
                    ParseVersionFromFolderName();
                }
            }
            catch
            {
                // 忽略版本检测失败
                Version = "未知";
            }
        }

        private void ParseVersionFromString(string content)
        {
            try
            {
                // 提取主版本号
                var majorMatch = System.Text.RegularExpressions.Regex.Match(content, @"""MajorVersion"":\s*(\d+)");
                var minorMatch = System.Text.RegularExpressions.Regex.Match(content, @"""MinorVersion"":\s*(\d+)");
                var patchMatch = System.Text.RegularExpressions.Regex.Match(content, @"""PatchVersion"":\s*(\d+)");

                if (majorMatch.Success && minorMatch.Success)
                {
                    var major = int.Parse(majorMatch.Groups[1].Value);
                    var minor = int.Parse(minorMatch.Groups[1].Value);
                    var patch = patchMatch.Success ? int.Parse(patchMatch.Groups[1].Value) : 0;

                    Version = $"{major}.{minor}";
                    FullVersion = $"{major}.{minor}.{patch}";

                    if (string.IsNullOrWhiteSpace(DisplayName))
                    {
                        DisplayName = $"UE {FullVersion}";
                    }
                }
            }
            catch
            {
                // 如果正则表达式解析失败，使用简单的字符串包含检查
                if (content.Contains("\"MajorVersion\": 5"))
                {
                    if (content.Contains("\"MinorVersion\": 4"))
                        Version = "5.4";
                    else if (content.Contains("\"MinorVersion\": 3"))
                        Version = "5.3";
                    else if (content.Contains("\"MinorVersion\": 2"))
                        Version = "5.2";
                    else if (content.Contains("\"MinorVersion\": 1"))
                        Version = "5.1";
                    else if (content.Contains("\"MinorVersion\": 0"))
                        Version = "5.0";
                    else
                        Version = "5.x";
                }
                else if (content.Contains("\"MajorVersion\": 4"))
                {
                    Version = "4.27"; // 假设是 4.27
                }
            }
        }

        private void ParseVersionFromFolderName()
        {
            var folderName = Path.GetFileName(EnginePath);
            if (folderName != null && folderName.Contains("UE_"))
            {
                var versionPart = folderName.Replace("UE_", "").Replace("_", ".");
                Version = versionPart;

                if (string.IsNullOrWhiteSpace(DisplayName))
                {
                    DisplayName = $"UE {versionPart}";
                }
            }
        }

        /// <summary>
        /// 刷新版本信息
        /// </summary>
        public void RefreshVersionInfo()
        {
            if (IsValid)
            {
                DetectVersion();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool _isSourceBuild;

        public bool IsSourceBuild
        {
            get => _isSourceBuild;
            private set
            {
                _isSourceBuild = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SourceStatusText));
            }
        }

        public string SourceStatusText => IsSourceBuild ? "源码版" : "原版";

        private static string SafeCombine(params string[] parts)
        {
            return Path.Combine(Array.FindAll(parts, p => !string.IsNullOrWhiteSpace(p)));
        }

        public string InstalledBuildMarkerPath =>
            SafeCombine(EnginePath, "Engine", "Build", "InstalledBuild.txt");


        private void DetectSourceBuild()
        {
            try
            {
                // EnginePath 为空或目录不存在：直接认为不是源码版（并且不再继续往下做 IO）
                if (string.IsNullOrWhiteSpace(EnginePath) || !Directory.Exists(EnginePath))
                {
                    IsSourceBuild = false;
                    return;
                }

                // 1) 明确“安装/二进制版”标记
                var hasInstalledMarker = File.Exists(InstalledBuildMarkerPath);

                // 2) 源码版常见迹象（全部用 SafeCombine）
                var gitDir = SafeCombine(EnginePath, ".git");
                var hasGit = Directory.Exists(gitDir);

                var setupBat = SafeCombine(EnginePath, "Setup.bat");
                var hasSetupBat = File.Exists(setupBat);

                var genBat = SafeCombine(EnginePath, "GenerateProjectFiles.bat");
                var genPs1 = SafeCombine(EnginePath, "GenerateProjectFiles.ps1");
                var hasGenerateProjectFiles = File.Exists(genBat) || File.Exists(genPs1);

                // 3) Engine/Source 下是否含 .cpp（使用 EnumerationOptions 防止拒绝访问）
                bool hasEngineSourceCpp = false;
                var engineSourceDir = SafeCombine(EnginePath, "Engine", "Source");
                if (Directory.Exists(engineSourceDir))
                {
                    var opts = new EnumerationOptions
                    {
                        IgnoreInaccessible = true,
                        RecurseSubdirectories = true,
                        AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint
                    };

                    // 只要发现第一个 .cpp 就停止，避免全盘扫描导致卡顿
                    foreach (var _ in Directory.EnumerateFiles(engineSourceDir, "*.cpp", opts))
                    {
                        hasEngineSourceCpp = true;
                        break;
                    }
                }

                // 4) 归纳判定（同之前逻辑）：不是 InstalledBuild 且有任一源码迹象 => 源码版
                IsSourceBuild = !hasInstalledMarker && (hasGit || hasSetupBat || hasGenerateProjectFiles || hasEngineSourceCpp);
            }
            catch
            {
                // 任何异常都不要继续冒泡到 UI
                IsSourceBuild = false;
            }
        }

        public void RefreshSourceBuildFlag()
        {
            if (IsValid)
            {
                DetectSourceBuild();
            }
        }
    }
}