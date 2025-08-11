using DreamUnrealManager.Models;

namespace DreamUnrealManager.Services
{
    public interface IProjectRepository
    {
        Task<List<ProjectInfo>> LoadAsync();
        Task SaveAsync(IEnumerable<ProjectInfo> projects);
    }
}