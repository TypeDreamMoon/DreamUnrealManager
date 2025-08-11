using DreamUnrealManager.Models;

namespace DreamUnrealManager.Services
{
    public interface IProjectFactory
    {
        Task<ProjectInfo?> CreateAsync(string uprojectPath, CancellationToken ct = default);
    }
}