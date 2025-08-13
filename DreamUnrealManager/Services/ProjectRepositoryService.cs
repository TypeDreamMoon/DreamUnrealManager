using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using DreamUnrealManager.Contracts.Services;
using DreamUnrealManager.Models;

namespace DreamUnrealManager.Services
{
    public sealed class ProjectRepositoryService : IProjectRepositoryService, INotifyPropertyChanged
    {
        private List<ProjectInfo> _loadedData;
        private bool _isDataLoaded; // 用于标记数据是否已加载

        // INotifyPropertyChanged 接口实现
        public event PropertyChangedEventHandler PropertyChanged;

        // 当 LoadedData 变化时通知 UI
        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // 异步加载数据（只加载一次）
        public async Task<List<ProjectInfo>> LoadAsync()
        {
            if (_isDataLoaded) // 如果数据已经加载过，直接返回缓存
            {
                return _loadedData;
            }

            // 加载项目数据
            _loadedData = await App.GetService<IProjectDataService>().LoadProjectsAsync();

            _isDataLoaded = true; // 标记数据已加载
            OnPropertyChanged(nameof(LoadedData)); // 当 LoadedData 变化时，触发 PropertyChanged
            return _loadedData;
        }

        // 异步保存数据
        public async Task SaveAsync(IEnumerable<ProjectInfo> projects)
        {
            var list = projects?.ToList() ?? new List<ProjectInfo>();
            await App.GetService<IProjectDataService>().SaveProjectsAsync(list);
        }

        // 通过属性加载数据
        public List<ProjectInfo> LoadedData
        {
            get => _loadedData;
            set
            {
                if (_loadedData != value)
                {
                    _loadedData = value;
                    OnPropertyChanged(nameof(LoadedData)); // 当 LoadedData 设置新值时，通知 UI
                }
            }
        }

        // 重置缓存（如果需要清除已加载的数据）
        public void ClearCache()
        {
            _loadedData = null;
            _isDataLoaded = false;
            OnPropertyChanged(nameof(LoadedData));
        }
    }
}