using DreamUnrealManager.Models;

namespace DreamUnrealManager.Contracts.Services
{
    public interface IEngineResolverService
    {
        Task<UnrealEngineInfo?> ResolveAsync(string engineAssociation, CancellationToken ct = default);
    }
}