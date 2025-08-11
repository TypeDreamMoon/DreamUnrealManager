using DreamUnrealManager.Models;

namespace DreamUnrealManager.Services
{
    public interface IProjectFilter
    {
        IEnumerable<ProjectInfo> FilterAndSort(IEnumerable<ProjectInfo> projects, ProjectFilterOptions opt);
    }
}