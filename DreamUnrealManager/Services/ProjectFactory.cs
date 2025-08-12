using System.Text.Json;
using System.Text.RegularExpressions;
using DreamUnrealManager.Models;

namespace DreamUnrealManager.Services
{
    public sealed class ProjectFactory : IProjectFactory
    {
        private readonly IEngineResolver _resolver;

        public ProjectFactory(IEngineResolver resolver)
        {
            _resolver = resolver;
        }

        public async Task<ProjectInfo?> CreateAsync(string uprojectPath, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(uprojectPath) || !File.Exists(uprojectPath))
                return null;

            var project = new ProjectInfo
            {
                ProjectPath = uprojectPath,
                ProjectDirectory = Path.GetDirectoryName(uprojectPath),
                DisplayName = Path.GetFileNameWithoutExtension(uprojectPath),
                LastModified = File.GetLastWriteTime(uprojectPath)
            };

            // 读取 .uproject 元数据
            try
            {
                var json = await File.ReadAllTextAsync(uprojectPath, ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("EngineAssociation", out var engineAssoc))
                    project.EngineAssociation = engineAssoc.GetString() ?? "";

                if (root.TryGetProperty("Description", out var desc))
                    project.Description = desc.GetString() ?? "";

                if (root.TryGetProperty("Category", out var cat))
                    project.Category = cat.GetString() ?? "";

                if (root.TryGetProperty("Modules", out var modulesEl) && modulesEl.ValueKind == JsonValueKind.Array)
                {
                    var modules = new List<ProjectModule>();
                    foreach (var m in modulesEl.EnumerateArray())
                    {
                        var mod = new ProjectModule();
                        if (m.TryGetProperty("Name", out var n)) mod.Name = n.GetString();
                        if (m.TryGetProperty("Type", out var t)) mod.Type = t.GetString();
                        if (m.TryGetProperty("LoadingPhase", out var lp)) mod.LoadingPhase = lp.GetString();
                        if (m.TryGetProperty("AdditionalDependencies", out var deps) && deps.ValueKind == JsonValueKind.Array)
                        {
                            mod.AdditionalDependencies = deps.EnumerateArray()
                                .Select(x => x.GetString())
                                .Where(x => !string.IsNullOrEmpty(x))
                                .ToArray();
                        }

                        modules.Add(mod);
                    }

                    project.Modules = modules;
                }

                if (root.TryGetProperty("Plugins", out var pluginsEl) && pluginsEl.ValueKind == JsonValueKind.Array)
                {
                    var plugins = new List<ProjectPlugin>();
                    foreach (var p in pluginsEl.EnumerateArray())
                    {
                        var pl = new ProjectPlugin();
                        if (p.TryGetProperty("Name", out var n)) pl.Name = n.GetString();
                        if (p.TryGetProperty("Enabled", out var en)) pl.Enabled = en.GetBoolean();
                        if (p.TryGetProperty("TargetAllowList", out var tal) && tal.ValueKind == JsonValueKind.Array)
                            pl.TargetAllowList = tal.EnumerateArray().Select(x => x.GetString()).ToArray();
                        if (p.TryGetProperty("SupportedTargetPlatforms", out var stp) && stp.ValueKind == JsonValueKind.Array)
                            pl.SupportedTargetPlatforms = stp.EnumerateArray().Select(x => x.GetString()).ToArray();
                        plugins.Add(pl);
                    }

                    project.Plugins = plugins;
                }
            }
            catch
            {
                project.EngineAssociation ??= "未知版本";
                project.Description ??= "无法读取项目描述";
            }

            // 解析引擎（支持 GUID → 注册表 → 引擎路径 → Build.version → 版本号）
            try
            {
                if (!string.IsNullOrEmpty(project.EngineAssociation))
                {
                    var engine = await _resolver.ResolveAsync(project.EngineAssociation, ct).ConfigureAwait(false);
                    project.AssociatedEngine = engine;

                    if (engine != null)
                    {
                        var readable = !string.IsNullOrEmpty(engine.FullVersion)
                            ? engine.FullVersion
                            : engine.Version;

                        if (!string.IsNullOrWhiteSpace(readable))
                            project.EngineAssociation = readable;
                    }
                    else
                    {
                        // 如果是 GUID 但没找到，就显示“无法解析”
                        if (Regex.IsMatch(project.EngineAssociation.Trim(), @"^\{?[0-9a-fA-F\-]{36}\}?$"))
                            project.EngineAssociation = "无法解析";
                    }

                    project.NotifyDerived();
                }
            }
            catch
            {
                /* 忽略关联异常 */
            }

            project.RefreshThumbnail();
            project.CheckGitStatus();

            return project;
        }
    }
}