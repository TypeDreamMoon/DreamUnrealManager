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
    public class EngineManagerService
    {
        private static EngineManagerService _instance;
        public static EngineManagerService Instance => _instance ??= new EngineManagerService();

        private readonly string _configFilePath;

        public ObservableCollection<UnrealEngineInfo> Engines
        {
            get;
            private set;
        }

        private EngineManagerService()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appFolder = Path.Combine(appDataPath, "DreamUnrealManager");
            Directory.CreateDirectory(appFolder);
            _configFilePath = Path.Combine(appFolder, "engines.json");

            Engines = new ObservableCollection<UnrealEngineInfo>();
            LoadEngines();
        }

        public async Task LoadEngines()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    var json = await File.ReadAllTextAsync(_configFilePath);
                    var engines = JsonSerializer.Deserialize<List<UnrealEngineInfo>>(json);

                    Engines.Clear();
                    foreach (var engine in engines ?? new List<UnrealEngineInfo>())
                    {
                        // 刷新版本信息以确保最新
                        engine.RefreshVersionInfo();
                        Engines.Add(engine);
                    }
                }
                else
                {
                    // 首次运行，尝试自动检测引擎
                    await AutoDetectEngines();
                }
            }
            catch (Exception ex)
            {
                // 记录错误但不抛出异常
                System.Diagnostics.Debug.WriteLine($"Failed to load engines: {ex.Message}");
            }
        }

        public async Task SaveEngines()
        {
            try
            {
                var json = JsonSerializer.Serialize(Engines.ToList(), new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                await File.WriteAllTextAsync(_configFilePath, json);
            }
            catch (Exception ex)
            {
                throw new Exception($"保存引擎配置失败: {ex.Message}");
            }
        }

        public async Task<UnrealEngineInfo> AddEngine(string displayName, string enginePath)
        {
            var engineInfo = new UnrealEngineInfo
            {
                DisplayName = displayName,
                EnginePath = enginePath
            };

            // 检查是否已存在相同路径的引擎
            if (Engines.Any(e => string.Equals(e.EnginePath, enginePath, StringComparison.OrdinalIgnoreCase)))
            {
                throw new Exception("此引擎路径已存在");
            }

            Engines.Add(engineInfo);
            await SaveEngines();

            return engineInfo;
        }

        public async Task UpdateEngine(UnrealEngineInfo engine)
        {
            // 刷新版本信息
            engine.RefreshVersionInfo();
            await SaveEngines();
        }

        public async Task RemoveEngine(UnrealEngineInfo engine)
        {
            Engines.Remove(engine);
            await SaveEngines();
        }

        public async Task AutoDetectEngines()
        {
            var detectedEngines = new List<UnrealEngineInfo>();

            var drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed).ToArray();

            var relativePaths = new[]
            {
                @"Program Files\Epic Games",
                @"Program Files\Unreal Engine",
                @"Program Files\UnrealEngine",
                @"Program Files\UE",
                @"Unreal Engine",
                @"UE",
                @"UnrealEngine",
                @"Unreal",
            };
            
            var otherPaths = drives.SelectMany(drive => relativePaths.Select(relativePath => Path.Combine(drive.RootDirectory.FullName, relativePath)));

            foreach (var basePath in otherPaths)
            {
                if (Directory.Exists(basePath))
                {
                    var engineDirs = Directory.GetDirectories(basePath)
                        .Where(dir =>
                        {
                            var folderName = Path.GetFileName(dir);
                            return folderName.StartsWith("UE_") ||
                                   folderName.StartsWith("UnrealEngine") ||
                                   folderName.Contains("Unreal");
                        })
                        .ToList();

                    foreach (var engineDir in engineDirs)
                    {
                        var engine = new UnrealEngineInfo
                        {
                            EnginePath = engineDir
                        };

                        if (engine.IsValid &&
                            !detectedEngines.Any(e => string.Equals(e.EnginePath, engineDir, StringComparison.OrdinalIgnoreCase)) &&
                            !Engines.Any(e => string.Equals(e.EnginePath, engineDir, StringComparison.OrdinalIgnoreCase)))
                        {
                            detectedEngines.Add(engine);
                        }
                    }
                }
            }

            // 添加检测到的引擎
            foreach (var engine in detectedEngines)
            {
                Engines.Add(engine);
            }

            if (detectedEngines.Any())
            {
                await SaveEngines();
            }
        }

        public UnrealEngineInfo GetEngineByDisplayName(string displayName)
        {
            return Engines.FirstOrDefault(e => e.DisplayName == displayName);
        }

        public UnrealEngineInfo GetEngineByVersion(string version)
        {
            return Engines.FirstOrDefault(e => e.Version == version || e.FullVersion == version);
        }

        public List<UnrealEngineInfo> GetValidEngines()
        {
            return Engines.Where(e => e.IsValid).OrderByDescending(e => e.LastUsed).ToList();
        }

        public List<UnrealEngineInfo> GetEnginesByMajorVersion(int majorVersion)
        {
            return Engines.Where(e => e.IsValid && e.BuildVersionInfo?.MajorVersion == majorVersion).ToList();
        }

        /// <summary>
        /// 刷新所有引擎的版本信息
        /// </summary>
        public async Task RefreshAllEngines()
        {
            foreach (var engine in Engines)
            {
                engine.RefreshVersionInfo();
            }

            await SaveEngines();
        }
    }
}