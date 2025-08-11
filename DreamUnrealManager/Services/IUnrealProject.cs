using DreamUnrealManager.Models;

namespace DreamUnrealManager.Services
{
    public interface IUnrealProjectService
    {
        Task LaunchProject(ProjectInfo project);
    }
}