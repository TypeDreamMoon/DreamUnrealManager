namespace DreamUnrealManager.Contracts.Services
{
    public interface IBuildService
    {
        Task<bool> GenerateProjectFilesAsync(string uprojectPath, CancellationToken ct = default);

        Task<bool> GenerateProjectFilesAsync(
            string uprojectPath,
            IProgress<string>? log,
            IProgress<int>? percent,
            CancellationToken ct = default);
    }
}