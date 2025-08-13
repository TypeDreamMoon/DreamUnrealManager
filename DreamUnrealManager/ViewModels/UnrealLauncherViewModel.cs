using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System;
using DreamUnrealManager.Models;

namespace DreamUnrealManager.ViewModels
{
    // 让 XAML 强类型绑定方便，这里直接公开 UnrealEngineInfo
    public partial class UnrealLauncherViewModel : ObservableRecipient, INotifyPropertyChanged
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

        [ObservableProperty] private string selectedSortOption = "最近使用";
        [ObservableProperty] private string searchText = string.Empty;
        [ObservableProperty] private bool favoriteFirst = true;
        [ObservableProperty] private string statusText = "就绪";

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
                Directory.CreateDirectory(Path.GetDirectoryName(GetStorePath())!);
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
            var item = Engines.FirstOrDefault(e => e.Id == id);
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
            var item = Engines.FirstOrDefault(e => e.Id == id);
            if (item == null) return;
            item.RefreshVersionInfo();
            item.LastUsed = DateTime.Now;
            await SaveAsync();
            ApplyFilters();
            StatusText = $"已刷新：{item.DisplayName}";
        }

        public async Task SetDefaultAsync(string id)
        {
            var item = Engines.FirstOrDefault(e => e.Id == id);
            if (item == null) return;
            // 这里简单地把默认引擎记在 engines.json 同目录的一个小文件里
            try
            {
                var flagPath = Path.Combine(Path.GetDirectoryName(GetStorePath())!, "default.txt");
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

        #region INotifyPropertyChanged（补充触发过滤）

        public new event PropertyChangedEventHandler? PropertyChanged;

        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.PropertyName == nameof(SearchText) ||
                e.PropertyName == nameof(SelectedSortOption) ||
                e.PropertyName == nameof(FavoriteFirst))
            {
                ApplyFilters();
            }

            PropertyChanged?.Invoke(this, e);
        }

        protected void Raise([CallerMemberName] string? name = null)
            => OnPropertyChanged(new PropertyChangedEventArgs(name));

        #endregion
    }
}