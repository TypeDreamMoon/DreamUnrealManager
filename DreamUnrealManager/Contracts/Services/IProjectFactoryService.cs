using DreamUnrealManager.Models;

namespace DreamUnrealManager.Contracts.Services
{
    public interface IProjectFactoryService
    {
        Task<ProjectInfo?> CreateAsync(string uprojectPath, CancellationToken ct = default);
    }
}