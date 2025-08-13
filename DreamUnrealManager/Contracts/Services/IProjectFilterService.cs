using DreamUnrealManager.Models;
using DreamUnrealManager.Services;

namespace DreamUnrealManager.Contracts.Services
{
    public interface IProjectFilterService
    {
        IEnumerable<ProjectInfo> FilterAndSort(IEnumerable<ProjectInfo> projects, ProjectFilterOptions opt);
    }
}