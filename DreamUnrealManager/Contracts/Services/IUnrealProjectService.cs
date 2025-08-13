using DreamUnrealManager.Models;

namespace DreamUnrealManager.Contracts.Services
{
    public interface IUnrealProjectService
    {
        Task LaunchProject(ProjectInfo project);
    }
}