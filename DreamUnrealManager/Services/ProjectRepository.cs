using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DreamUnrealManager.Models;

namespace DreamUnrealManager.Services
{
    public sealed class ProjectRepository : IProjectRepository
    {
        public async Task<List<ProjectInfo>> LoadAsync()
        {
            return await ProjectDataService.Instance.LoadProjectsAsync();
        }

        public async Task SaveAsync(IEnumerable<ProjectInfo> projects)
        {
            var list = projects?.ToList() ?? new List<ProjectInfo>();
            await ProjectDataService.Instance.SaveProjectsAsync(list);
        }
    }
}