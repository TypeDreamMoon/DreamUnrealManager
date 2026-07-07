using DreamUnrealManager.Contracts.Services;
using DreamUnrealManager.Models;

namespace DreamUnrealManager.Services
{
    public sealed class ProjectSearchService : IProjectSearchService
    {
        private readonly IProjectFactoryService _factoryService;
        public ProjectSearchService(IProjectFactoryService factoryService) => _factoryService = factoryService;

        public async Task<List<ProjectInfo>> SearchAsync(string rootFolder, IProgress<int>? progress = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(rootFolder) || !Directory.Exists(rootFolder))
                return new List<ProjectInfo>();

            var uprojects = EnumerateUProjects(rootFolder);
            var results = new List<ProjectInfo>();
            int count = 0;

            foreach (var path in uprojects)
            {
                ct.ThrowIfCancellationRequested();
                var proj = await _factoryService.CreateAsync(path, ct);
                if (proj != null) results.Add(proj);

                count++;
                progress?.Report((int)((count / (double)uprojects.Count) * 100));
            }

            return results;
        }

        /// <summary>
        /// 逐目录遍历查找 .uproject：对每个目录单独 try/catch，跳过无法访问/路径过长的目录与符号链接，
        /// 避免单个子目录出错导致整棵树搜索失败或目录环造成无限递归。
        /// </summary>
        private static List<string> EnumerateUProjects(string root)
        {
            var results = new List<string>();
            var stack = new Stack<string>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                var dir = stack.Pop();

                try
                {
                    results.AddRange(Directory.EnumerateFiles(dir, "*.uproject"));
                }
                catch
                {
                    // 跳过该目录中无法枚举的文件
                }

                try
                {
                    foreach (var sub in Directory.EnumerateDirectories(dir))
                    {
                        try
                        {
                            // 跳过符号链接/联接，避免目录环导致无限递归
                            if ((new DirectoryInfo(sub).Attributes & FileAttributes.ReparsePoint) != 0)
                            {
                                continue;
                            }
                        }
                        catch
                        {
                            continue;
                        }

                        stack.Push(sub);
                    }
                }
                catch
                {
                    // 跳过无法枚举子目录的目录
                }
            }

            return results;
        }
    }
}