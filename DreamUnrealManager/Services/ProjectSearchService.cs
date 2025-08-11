using DreamUnrealManager.Models;

namespace DreamUnrealManager.Services
{
    public sealed class ProjectSearchService : IProjectSearchService
    {
        private readonly IProjectFactory _factory;
        public ProjectSearchService(IProjectFactory factory) => _factory = factory;

        public async Task<List<ProjectInfo>> SearchAsync(string rootFolder, IProgress<int>? progress = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(rootFolder) || !Directory.Exists(rootFolder))
                return new List<ProjectInfo>();

            var uprojects = Directory.EnumerateFiles(rootFolder, "*.uproject", SearchOption.AllDirectories).ToList();
            var results = new List<ProjectInfo>();
            int count = 0;

            foreach (var path in uprojects)
            {
                ct.ThrowIfCancellationRequested();
                var proj = await _factory.CreateAsync(path, ct);
                if (proj != null) results.Add(proj);

                count++;
                progress?.Report((int)((count / (double)uprojects.Count) * 100));
            }

            return results;
        }
    }
}