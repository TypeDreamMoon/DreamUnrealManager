namespace DreamUnrealManager.Services
{
    public interface IBuildService
    {
        Task<bool> GenerateProjectFilesAsync(string uprojectPath, CancellationToken ct = default);
    }
}