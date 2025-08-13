using DreamUnrealManager.Models;

namespace DreamUnrealManager.Contracts.Services
{
    public interface IProjectRepositoryService
    {
        Task<List<ProjectInfo>> LoadAsync();
        Task SaveAsync(IEnumerable<ProjectInfo> projects);
    }
}