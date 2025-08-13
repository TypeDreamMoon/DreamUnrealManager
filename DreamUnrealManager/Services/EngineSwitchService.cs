using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using DreamUnrealManager.Contracts.Services;
using DreamUnrealManager.Models;

namespace DreamUnrealManager.Services
{
    public sealed class EngineSwitchService : IEngineSwitchService
    {
        public async Task<bool> SwitchAsync(ProjectInfo project, string newEngineVersion)
        {
            if (project == null || string.IsNullOrWhiteSpace(newEngineVersion) || !File.Exists(project.ProjectPath))
                return false;

            var json = await File.ReadAllTextAsync(project.ProjectPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new();
            dict["EngineAssociation"] = newEngineVersion;

            var newJson = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(project.ProjectPath, newJson);
            return true;
        }
    }
}