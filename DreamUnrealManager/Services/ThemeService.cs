using System;
using System.IO;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Windows.Storage;

namespace DreamUnrealManager.Services
{
    public enum AppThemeOption
    {
        System,
        Light,
        Dark
    }

    public static class ThemeService
    {
        private const string SettingsKey = "AppTheme"; // LocalSettings 键

        // 文件兜底路径：%LOCALAPPDATA%\DreamUnrealManager\settings.json
        private static readonly string FallbackDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DreamUnrealManager");

        private static readonly string FallbackFile = Path.Combine(FallbackDir, "settings.json");

        public static AppThemeOption Load()
        {
            // 1) 先试 ApplicationData.LocalSettings
            try
            {
                var ls = ApplicationData.Current.LocalSettings;
                if (ls.Values.TryGetValue(SettingsKey, out var value) && value is string s &&
                    Enum.TryParse<AppThemeOption>(s, out var opt))
                {
                    return opt;
                }

                return AppThemeOption.System;
            }
            catch
            {
                // 2) 失败就用文件兜底
                try
                {
                    if (!File.Exists(FallbackFile)) return AppThemeOption.System;
                    var json = File.ReadAllText(FallbackFile);
                    var dto = JsonSerializer.Deserialize<FallbackSettings>(json);
                    return dto?.AppTheme switch
                    {
                        "Light" => AppThemeOption.Light,
                        "Dark" => AppThemeOption.Dark,
                        _ => AppThemeOption.System
                    };
                }
                catch
                {
                    return AppThemeOption.System;
                }
            }
        }

        public static void Save(AppThemeOption option)
        {
            // 1) 先试 ApplicationData.LocalSettings
            try
            {
                ApplicationData.Current.LocalSettings.Values[SettingsKey] = option.ToString();
                return;
            }
            catch
            {
                // 2) 失败就写到文件兜底
                try
                {
                    Directory.CreateDirectory(FallbackDir);
                    var dto = new FallbackSettings { AppTheme = option.ToString() };
                    var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(FallbackFile, json);
                }
                catch
                {
                    // 忽略：主题无法持久化时，不再抛异常以免影响 UI
                }
            }
        }

        /// <summary>把主题应用到窗口根元素（全局生效）。</summary>
        public static void ApplyToWindow(Window window, AppThemeOption option)
        {
            if (window?.Content is FrameworkElement root)
            {
                root.RequestedTheme = option switch
                {
                    AppThemeOption.Light => ElementTheme.Light,
                    AppThemeOption.Dark => ElementTheme.Dark,
                    _ => ElementTheme.Default, // 跟随系统
                };
            }
        }

        private sealed class FallbackSettings
        {
            public string AppTheme
            {
                get;
                set;
            }
        }
    }
}