using System.Collections.Generic;
using System.Threading.Tasks;
using DreamUnrealManager.Models;

namespace DreamUnrealManager.Contracts.Services
{
    public interface IEngineManagerService
    {
        Task LoadEngines();
        Task SaveEngines();
        Task<UnrealEngineInfo> AddEngine(string displayName, string enginePath);
        Task UpdateEngine(UnrealEngineInfo engine);
        Task RemoveEngine(UnrealEngineInfo engine);
        Task AutoDetectEngines();
        UnrealEngineInfo GetEngineByDisplayName(string displayName);
        UnrealEngineInfo GetEngineByVersion(string version);
        List<UnrealEngineInfo> GetValidEngines();
        List<UnrealEngineInfo> GetEnginesByMajorVersion(int majorVersion);
        Task RefreshAllEngines();
    }
}