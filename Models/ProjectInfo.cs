    public class ProjectPlugin
    {
        [JsonPropertyName("Name")]
        public string Name
        {
            get;
            set;
        }

        [JsonPropertyName("Enabled")]
        public bool Enabled
        {
            get;
            set;
        }

        [JsonPropertyName("TargetAllowList")]
        public string[] TargetAllowList
        {
            get;
            set;
        }
    }

    /// <summary>
    /// 获取启用的插件数量
    /// </summary>
    public int GetEnabledPluginsCount()
    {
        return Plugins?.Count(p => p.Enabled) ?? 0;
    }

    /// <summary>
    /// 获取模块数量
    /// </summary>
    public int GetModulesCount()
    {
        return Modules?.Length ?? 0;
    }

    /// <summary>
    /// 获取项目的详细信息字符串
    /// </summary>
    public string GetDetailedInfo()
    {
        var info = new List<string>();

        if (!string.IsNullOrEmpty(EngineAssociation))
            info.Add($"引擎版本: {EngineAssociation}");

        if (FileVersion > 0)
            info.Add($"文件版本: {FileVersion}");

        var modulesCount = GetModulesCount();
        if (modulesCount > 0)
            info.Add($"模块数量: {modulesCount}");

        var pluginsCount = GetEnabledPluginsCount();
        if (pluginsCount > 0)
            info.Add($"启用插件: {pluginsCount}");

        if (TargetPlatforms?.Length > 0)
            info.Add($"目标平台: {string.Join(", ", TargetPlatforms)}");

        return string.Join(" | ", info);
    }

    /// <summary>
    /// 获取主要插件列表（仅显示名称）
    /// </summary>
    public string GetMainPluginsList()
    {
        if (Plugins == null || Plugins.Length == 0)
            return "无插件";

        var enabledPlugins = Plugins.Where(p => p.Enabled).Select(p => p.Name).Take(3);
        var pluginsList = string.Join(", ", enabledPlugins);

        var totalEnabledCount = Plugins.Count(p => p.Enabled);
        if (totalEnabledCount > 3)
            pluginsList += $" 等 {totalEnabledCount} 个插件";

        return pluginsList;
    }
