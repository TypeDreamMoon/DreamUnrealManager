using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System;
using DreamUnrealManager.Models;

namespace DreamUnrealManager.ViewModels
{
    // 让 XAML 强类型绑定方便，这里直接公开 UnrealEngineInfo
    public partial class UnrealLauncherViewModel : ObservableRecipient
    {
        private const string StoreFileName = "engines.json";
        private const string DefaultEngineKey = "Launcher.DefaultEngineId";

        public ObservableCollection<UnrealEngineInfo> Engines
        {
            get;
        } = new();

        public ObservableCollection<UnrealEngineInfo> FilteredEngines
        {
            get;
        } = new();

        // 排序
        public string[] SortOptions
        {
            get;
        } = new[] { "最近使用", "名称", "版本" };

        private string _selectedSortOption = "最近使用";
        public string SelectedSortOption
        {
            get => _selectedSortOption;
            set => SetProperty(ref _selectedSortOption, value);
        }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set => SetProperty(ref _searchText, value);
        }

        private bool _favoriteFirst = true;
        public bool FavoriteFirst
        {
            get => _favoriteFirst;
            set => SetProperty(ref _favoriteFirst, value);
        }

        private string _statusText = "就绪";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public UnrealLauncherViewModel()
        {
        }

        #region 公共操作

        public async Task LoadAsync()
        {
            try
            {
                StatusText = "正在加载引擎列表...";
                var file = GetStorePath();
                if (File.Exists(file))
                {
                    using var s = File.OpenRead(file);
                    var list = await JsonSerializer.DeserializeAsync<List<UnrealEngineInfo>>(s) ?? new();
                    Engines.Clear();
                    foreach (var e in list.OrderByDescending(e => e.LastUsed))
                        Engines.Add(e);
                }

                ApplyFilters();
                StatusText = "就绪";
            }
            catch (Exception ex)
            {
                StatusText = $"加载失败：{ex.Message}";
            }
        }

        public async Task SaveAsync()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(GetStorePath()) ?? string.Empty);
                using var s = File.Create(GetStorePath());
                await JsonSerializer.SerializeAsync(s, Engines.ToList(), new JsonSerializerOptions { WriteIndented = true });
            }
            catch
            {
            }
        }

        public string GetStorePath()
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DreamUnrealManager");
            return Path.Combine(root, StoreFileName);
        }

        public async Task AddEngineAsync(string engineRootPath)
        {
            try
            {
                var info = new UnrealEngineInfo
                {
                    EnginePath = engineRootPath,
                    DisplayName = Path.GetFileName(engineRootPath)
                };
                info.RefreshVersionInfo(); // 触发版本解析与校验

                // 去重：按路径
                if (Engines.Any(e => string.Equals(e.EnginePath, info.EnginePath, StringComparison.OrdinalIgnoreCase)))
                {
                    await ToastAsync("该引擎路径已存在");
                    return;
                }

                info.LastUsed = DateTime.Now;
                Engines.Add(info);
                await SaveAsync();
                ApplyFilters();

                StatusText = info.IsValid ? $"已添加：{info.DisplayName}（{info.GetDisplayVersion()}）" : "添加完成，但路径无效";
            }
            catch (Exception ex)
            {
                StatusText = $"添加失败：{ex.Message}";
            }
        }

        public async Task RemoveAsync(string id)
        {
            var item = FindEngine(id);
            if (item == null) return;
            Engines.Remove(item);
            await SaveAsync();
            ApplyFilters();
            StatusText = $"已移除：{item.DisplayName}";
        }

        public async Task RefreshAllAsync()
        {
            StatusText = "正在刷新...";
            foreach (var e in Engines)
            {
                try
                {
                    e.RefreshVersionInfo();
                }
                catch
                {
                }
            }

            await SaveAsync();
            ApplyFilters();
            StatusText = "刷新完成";
        }

        public async Task RefreshOneAsync(string id)
        {
            var item = FindEngine(id);
            if (item == null) return;
            item.RefreshVersionInfo();
            item.LastUsed = DateTime.Now;
            await SaveAsync();
            ApplyFilters();
            StatusText = $"已刷新：{item.DisplayName}";
        }

        public async Task SetDefaultAsync(string id)
        {
            var item = FindEngine(id);
            if (item == null) return;
            // 这里简单地把默认引擎记在 engines.json 同目录的一个小文件里
            try
            {
                var flagPath = Path.Combine(Path.GetDirectoryName(GetStorePath()) ?? string.Empty, "default.txt");
                File.WriteAllText(flagPath, id);
            }
            catch
            {
            }

            // 也顺手把它置顶/更新时间
            item.LastUsed = DateTime.Now;
            Engines.Move(Engines.IndexOf(item), 0);
            await SaveAsync();
            ApplyFilters();
            await ToastAsync($"已设为默认：{item.DisplayName}");
        }

        public Task ToastAsync(string message)
        {
            // 这里简单设置状态文本；如你已有 Dialog/Toast 服务可替换
            StatusText = message;
            return Task.CompletedTask;
        }

        #endregion

        private UnrealEngineInfo? FindEngine(string idOrPath)
        {
            if (string.IsNullOrWhiteSpace(idOrPath))
            {
                return null;
            }

            // 优先按稳定 Id 查找，兼容历史 UI 传路径的情况。
            return Engines.FirstOrDefault(e => string.Equals(e.Id, idOrPath, StringComparison.Ordinal))
                   ?? Engines.FirstOrDefault(e => string.Equals(e.EnginePath, idOrPath, StringComparison.OrdinalIgnoreCase));
        }

        #region 过滤与排序

        public void ApplyFilters()
        {
            IEnumerable<UnrealEngineInfo> q = Engines;

            // 搜索
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var key = SearchText.Trim();
                q = q.Where(e =>
                    (!string.IsNullOrEmpty(e.DisplayName) && e.DisplayName.Contains(key, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(e.EnginePath) && e.EnginePath.Contains(key, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(e.Version) && e.Version.Contains(key, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(e.FullVersion) && e.FullVersion.Contains(key, StringComparison.OrdinalIgnoreCase)));
            }

            // 排序
            q = SelectedSortOption switch
            {
                "名称" => q.OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase),
                "版本" => q.OrderByDescending(e => e.FullVersion ?? e.Version),
                _ => q.OrderByDescending(e => e.LastUsed)
            };

            // 收藏置顶（如果你后续给 UnrealEngineInfo 加 IsFavorite 字段这里可以启用）
            if (FavoriteFirst)
            {
                // 暂无收藏字段，保留接口
            }

            // 回填
            FilteredEngines.Clear();
            foreach (var e in q) FilteredEngines.Add(e);
        }

        #endregion

        protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.PropertyName == nameof(SearchText) ||
                e.PropertyName == nameof(SelectedSortOption) ||
                e.PropertyName == nameof(FavoriteFirst))
            {
                ApplyFilters();
            }
        }
    }
}
