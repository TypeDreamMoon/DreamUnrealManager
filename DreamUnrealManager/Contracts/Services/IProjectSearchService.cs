using DreamUnrealManager.Models;

namespace DreamUnrealManager.Contracts.Services
{
    public interface IProjectSearchService
    {
        Task<List<ProjectInfo>> SearchAsync(
            string rootFolder,
            IProgress<int>? progress = null,
            CancellationToken ct = default);
    }
}