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
        public static AppThemeOption Load()
        {
            try
            {
                return Settings.Get("App.Theme", AppThemeOption.System);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return AppThemeOption.System;
            }
        }

        public static void Save(AppThemeOption option)
        {
            try
            {
                Settings.Set("App.Theme", option);
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
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