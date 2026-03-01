using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DreamUnrealManager.Contracts.Services;
using DreamUnrealManager.Models;

namespace DreamUnrealManager.Services
{
    public class EngineManagerService : IEngineManagerService
    {
        private static EngineManagerService? _instance;
        public static EngineManagerService Instance => _instance ??= new EngineManagerService();

        private readonly string _configFilePath;
        private readonly SemaphoreSlim _loadLock = new(1, 1);
        private bool _isLoaded;
        private bool _lastConfigExisted;
        private DateTime? _lastConfigWriteTimeUtc;
        private long? _lastConfigLength;

        public ObservableCollection<UnrealEngineInfo> Engines
        {
            get;
            private set;
        }

        public EngineManagerService()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appFolder = Path.Combine(appDataPath, "DreamUnrealManager");
            Directory.CreateDirectory(appFolder);
            _configFilePath = Path.Combine(appFolder, "engines.json");

            Engines = new ObservableCollection<UnrealEngineInfo>();
            _ = LoadEngines();
        }

        public async Task LoadEngines()
        {
            if (_isLoaded && !HasExternalConfigChanges())
            {
                return;
            }

            await _loadLock.WaitAsync();
            try
            {
                if (_isLoaded && !HasExternalConfigChanges())
                {
                    return;
                }

                if (File.Exists(_configFilePath))
                {
                    var json = await File.ReadAllTextAsync(_configFilePath);
                    var engines = JsonSerializer.Deserialize<List<UnrealEngineInfo>>(json) ?? new List<UnrealEngineInfo>();

                    // 过滤掉历史数据里的 null 项
                    engines = engines.Where(e => e != null).ToList();

                    Engines.Clear();
                    foreach (var engine in engines)
                    {
                        // 防御式刷新，单个失败不影响整体
                        try { engine.RefreshVersionInfo(); } catch { }
                        Engines.Add(engine);
                    }
                }
                else
                {
                    // 首次运行，尝试自动检测引擎
                    await AutoDetectEngines();
                }

                MarkConfigSnapshot();
                _isLoaded = true;
            }
            catch (Exception ex)
            {
                // 记录错误但不抛出异常
                System.Diagnostics.Debug.WriteLine($"Failed to load engines: {ex.Message}");
            }
            finally
            {
                _loadLock.Release();
            }
        }


        public async Task SaveEngines()
        {
            try
            {
                // 写盘前再次过滤 null
                var json = JsonSerializer.Serialize(
                    Engines.Where(e => e != null).ToList(),
                    new JsonSerializerOptions { WriteIndented = true }
                );
                await File.WriteAllTextAsync(_configFilePath, json);
                MarkConfigSnapshot();
                _isLoaded = true;
            }
            catch (Exception ex)
            {
                throw new Exception($"保存引擎配置失败: {ex.Message}");
            }
        }

        private bool HasExternalConfigChanges()
        {
            try
            {
                var exists = File.Exists(_configFilePath);
                if (!exists)
                {
                    return _lastConfigExisted;
                }

                var fi = new FileInfo(_configFilePath);

                if (!_lastConfigExisted || !_lastConfigWriteTimeUtc.HasValue || !_lastConfigLength.HasValue)
                {
                    return true;
                }

                return fi.LastWriteTimeUtc != _lastConfigWriteTimeUtc.Value
                       || fi.Length != _lastConfigLength.Value;
            }
            catch
            {
                // IO 异常时保守处理为“有变化”，避免一直用到旧缓存。
                return true;
            }
        }

        private void MarkConfigSnapshot()
        {
            try
            {
                var exists = File.Exists(_configFilePath);
                _lastConfigExisted = exists;

                if (!exists)
                {
                    _lastConfigWriteTimeUtc = null;
                    _lastConfigLength = null;
                    return;
                }

                var fi = new FileInfo(_configFilePath);
                _lastConfigWriteTimeUtc = fi.LastWriteTimeUtc;
                _lastConfigLength = fi.Length;
            }
            catch
            {
                _lastConfigExisted = false;
                _lastConfigWriteTimeUtc = null;
                _lastConfigLength = null;
            }
        }


        public async Task<UnrealEngineInfo> AddEngine(string displayName, string enginePath)
        {
            if (string.IsNullOrWhiteSpace(enginePath))
                throw new Exception("引擎路径不能为空");

            // 去重前先排除 null
            if (Engines.Any(e => e != null &&
                                 string.Equals(e.EnginePath, enginePath, StringComparison.OrdinalIgnoreCase)))
            {
                throw new Exception("此引擎路径已存在");
            }

            var engineInfo = new UnrealEngineInfo
            {
                DisplayName = displayName,
                EnginePath = enginePath
            };

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

            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
                .ToArray();

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

            var otherPaths = drives.SelectMany(drive =>
                relativePaths.Select(relativePath => Path.Combine(drive.RootDirectory.FullName, relativePath)));

            foreach (var basePath in otherPaths)
            {
                if (!Directory.Exists(basePath)) continue;

                var engineDirs = Directory.GetDirectories(basePath)
                    .Where(dir =>
                    {
                        var folderName = Path.GetFileName(dir);
                        return folderName.StartsWith("UE_", StringComparison.OrdinalIgnoreCase) ||
                               folderName.StartsWith("UnrealEngine", StringComparison.OrdinalIgnoreCase) ||
                               folderName.Contains("Unreal", StringComparison.OrdinalIgnoreCase);
                    })
                    .ToList();

                foreach (var engineDir in engineDirs)
                {
                    var engine = new UnrealEngineInfo { EnginePath = engineDir };

                    if (engine != null && engine.IsValid &&
                        !detectedEngines.Any(e => e != null &&
                                                  string.Equals(e.EnginePath, engineDir, StringComparison.OrdinalIgnoreCase)) &&
                        !Engines.Any(e => e != null &&
                                          string.Equals(e.EnginePath, engineDir, StringComparison.OrdinalIgnoreCase)))
                    {
                        detectedEngines.Add(engine);
                    }
                }
            }

            // 添加检测到的引擎
            foreach (var engine in detectedEngines.Where(e => e != null))
            {
                Engines.Add(engine);
            }

            if (detectedEngines.Any(e => e != null))
            {
                await SaveEngines();
            }
        }

        public UnrealEngineInfo? GetEngineByDisplayName(string displayName)
        {
            return Engines.FirstOrDefault(e => e != null && e.DisplayName == displayName);
        }

        public UnrealEngineInfo? GetEngineByVersion(string version)
        {
            return Engines.FirstOrDefault(e => e != null &&
                                               (e.Version == version || e.FullVersion == version));
        }

        public List<UnrealEngineInfo> GetValidEngines()
        {
            return Engines.Where(e => e.IsValid).OrderByDescending(e => e.LastUsed).ToList();
        }

        public List<UnrealEngineInfo> GetEnginesByMajorVersion(int majorVersion)
        {
            return Engines
                .Where(e => e != null && e.IsValid && (e.BuildVersionInfo?.MajorVersion == majorVersion))
                .ToList();
        }

        /// <summary>
        /// 刷新所有引擎的版本信息
        /// </summary>
        public async Task RefreshAllEngines()
        {
            foreach (var engine in Engines.Where(e => e != null))
            {
                try { engine.RefreshVersionInfo(); } catch { }
            }

            await SaveEngines();
        }

    }
}
