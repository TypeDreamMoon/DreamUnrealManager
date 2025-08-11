using DreamUnrealManager.Models;

namespace DreamUnrealManager.Services
{
    public interface IEngineResolver
    {
        Task<UnrealEngineInfo?> ResolveAsync(string engineAssociation, CancellationToken ct = default);
    }
}