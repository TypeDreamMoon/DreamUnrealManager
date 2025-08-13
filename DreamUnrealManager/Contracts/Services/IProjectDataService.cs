using System.Collections.Generic;
using System.Threading.Tasks;
using DreamUnrealManager.Models;

namespace DreamUnrealManager.Contracts.Services
{
    public interface IProjectDataService
    {
        /// <summary>保存项目列表到本地存储</summary>
        Task<bool> SaveProjectsAsync(List<ProjectInfo> projects);

        /// <summary>从本地存储加载项目列表</summary>
        Task<List<ProjectInfo>> LoadProjectsAsync();

        /// <summary>清理无效（文件不存在）的项目，并保存</summary>
        Task<int> CleanupInvalidProjectsAsync(List<ProjectInfo> projects);
    }
}