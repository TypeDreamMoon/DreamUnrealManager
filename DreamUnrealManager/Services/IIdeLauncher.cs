using DreamUnrealManager.Models;
using System.Threading.Tasks;

namespace DreamUnrealManager.Services
{
    public interface IIdeLauncher
    {
        Task LaunchAsync(ProjectInfo project);
    }
}