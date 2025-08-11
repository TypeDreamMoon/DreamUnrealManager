using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DreamUnrealManager.Models;

namespace DreamUnrealManager.Services
{
    public sealed class EngineResolver : IEngineResolver
    {
        public async Task<UnrealEngineInfo?> ResolveAsync(string engineAssociation, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(engineAssociation))
                return null;

            var mgr = EngineManagerService.Instance;
            await mgr.LoadEngines();
            var engines = mgr.GetValidEngines() ?? Enumerable.Empty<UnrealEngineInfo>();

            return engines.FirstOrDefault(e =>
                e.Version == engineAssociation ||
                e.FullVersion == engineAssociation ||
                (e.BuildVersionInfo?.BranchName?.Contains(engineAssociation) ?? false));
        }
    }
}