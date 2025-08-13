using DreamUnrealManager.Contracts.Services;
using DreamUnrealManager.Helpers;
using Microsoft.UI.Xaml;

namespace DreamUnrealManager.Services;

public class ThemeSelectorService : IThemeSelectorService
{
    private const string SettingsKey = "AppBackgroundRequestedTheme";

    public ElementTheme Theme
    {
        get;
        set;
    } = ElementTheme.Default;


    public ThemeSelectorService()
    {
    }

    public async Task InitializeAsync()
    {
        Theme = LoadThemeFromSettingsAsync();
        await Task.CompletedTask;
    }

    public async Task SetThemeAsync(ElementTheme theme)
    {
        Theme = theme;

        await SetRequestedThemeAsync();
        SaveThemeInSettingsAsync(Theme);
    }

    public async Task SetRequestedThemeAsync()
    {
        if (App.MainWindow.Content is FrameworkElement rootElement)
        {
            rootElement.RequestedTheme = Theme;

            TitleBarHelper.UpdateTitleBar(Theme);
        }

        await Task.CompletedTask;
    }

    private ElementTheme LoadThemeFromSettingsAsync()
    {
        var themeName = SettingsService.Get<string>(SettingsKey);

        if (Enum.TryParse(themeName, out ElementTheme cacheTheme))
        {
            return cacheTheme;
        }

        return ElementTheme.Default;
    }

    private void SaveThemeInSettingsAsync(ElementTheme theme)
    {
        SettingsService.Set(SettingsKey, theme.ToString());
    }
}