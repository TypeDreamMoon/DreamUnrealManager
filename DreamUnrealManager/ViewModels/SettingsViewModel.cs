using System.ComponentModel;
using System.Reflection;
using System.Windows.Input;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DreamUnrealManager.Contracts.Services;
using DreamUnrealManager.Helpers;

using Microsoft.UI.Xaml;

using Windows.ApplicationModel;
using DreamUnrealManager.Services;
using Microsoft.Extensions.Hosting;

namespace DreamUnrealManager.ViewModels;

public partial class SettingsViewModel : ObservableRecipient, INotifyPropertyChanged
{
    PropertyChangedEventHandler PropertyChanged;
    
    protected void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private readonly IThemeSelectorService _themeSelectorService;

    [ObservableProperty]
    private ElementTheme _elementTheme;

    [ObservableProperty]
    private string _versionDescription;

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
            version = Assembly.GetExecutingAssembly().GetName().Version!;
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
