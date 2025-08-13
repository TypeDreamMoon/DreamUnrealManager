using System.Threading.Tasks;
using DreamUnrealManager.Models;

namespace DreamUnrealManager.Contracts.Services
{
    public interface IEngineSwitchService
    {
        Task<bool> SwitchAsync(ProjectInfo project, string newEngineVersion);
    }
}