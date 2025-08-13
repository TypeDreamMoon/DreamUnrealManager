using DreamUnrealManager.Models;
using System.Threading.Tasks;

namespace DreamUnrealManager.Contracts.Services
{
    public interface IIdeLauncherService
    {
        Task LaunchAsync(ProjectInfo project);
    }
}