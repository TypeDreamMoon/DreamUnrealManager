using System.Reflection;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DreamUnrealManager.Contracts.Services;
using DreamUnrealManager.Helpers;
using DreamUnrealManager.Services;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel;

namespace DreamUnrealManager.ViewModels;

public partial class SettingsViewModel : ObservableRecipient
{
    private readonly IThemeSelectorService _themeSelectorService;

    private ElementTheme _elementTheme;
    public ElementTheme ElementTheme
    {
        get => _elementTheme;
        set => SetProperty(ref _elementTheme, value);
    }

    private string _versionDescription = string.Empty;
    public string VersionDescription
    {
        get => _versionDescription;
        set => SetProperty(ref _versionDescription, value);
    }

    public ICommand SwitchThemeCommand
    {
        get;
    }

    public SettingsViewModel(IThemeSelectorService themeSelectorService)
    {
        _themeSelectorService = themeSelectorService;
        _elementTheme = _themeSelectorService.Theme;
        _versionDescription = GetVersionDescription();

        SwitchThemeCommand = new RelayCommand<ElementTheme>(
            async (param) =>
            {
                if (ElementTheme != param)
                {
                    ElementTheme = param;
                    await _themeSelectorService.SetThemeAsync(param);
                }
            });
    }

    private static string GetVersionDescription()
    {
        Version version;

        if (RuntimeHelper.IsMSIX)
        {
            var packageVersion = Package.Current.Id.Version;
            version = new(packageVersion.Major, packageVersion.Minor, packageVersion.Build, packageVersion.Revision);
        }
        else
        {
            version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);
        }

        return $"Dream Unreal Manager - {version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
    }

    private bool _showBackground;
    public bool ShowBackground
    {
        get
        {
            _showBackground = BackgroundSettingsService.Instance.BackgroundOpacity == 0;
            return _showBackground;
        }
        set
        {
            if (_showBackground != value)
            {
                _showBackground = value;
                BackgroundSettingsService.Instance.BackgroundOpacity = value ? 0 : 0.5;
                OnPropertyChanged(nameof(ShowBackground));
            }
        }
    }
}
