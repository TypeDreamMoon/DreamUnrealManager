using DreamUnrealManager.Models;

namespace DreamUnrealManager.Contracts.Services
{
    public interface IProjectRepositoryService
    {
        Task<List<ProjectInfo>> LoadAsync();
        Task SaveAsync(IEnumerable<ProjectInfo> projects);

        /// <summary>当前缓存的项目列表。</summary>
        List<ProjectInfo> LoadedData { get; set; }

        /// <summary>丢弃缓存，下次 LoadAsync 重新读盘。</summary>
        void ClearCache();
    }
}