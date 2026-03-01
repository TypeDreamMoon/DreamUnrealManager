using CommunityToolkit.Mvvm.ComponentModel;
using DreamUnrealManager.Contracts.Services;
using DreamUnrealManager.Services;
using DreamUnrealManager.Views;
using Microsoft.UI.Xaml.Navigation;

namespace DreamUnrealManager.ViewModels;

public partial class ShellViewModel : ObservableRecipient
{
    private bool _isBackEnabled;
    public bool IsBackEnabled
    {
        get => _isBackEnabled;
        set => SetProperty(ref _isBackEnabled, value);
    }

    private object? _selected;
    public object? Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }

    private string _commandText = string.Empty;
    public string CommandText
    {
        get => _commandText;
        set => SetProperty(ref _commandText, value);
    }

    public INavigationService NavigationService
    {
        get;
    }

    public INavigationViewService NavigationViewService
    {
        get;
    }

    public ShellViewModel(INavigationService navigationService, INavigationViewService navigationViewService)
    {
        NavigationService = navigationService;
        NavigationService.Navigated += OnNavigated;
        NavigationViewService = navigationViewService;
    }

    private void OnNavigated(object sender, NavigationEventArgs e)
    {
        IsBackEnabled = NavigationService.CanGoBack;

        if (e.SourcePageType == typeof(SettingsPage))
        {
            Selected = NavigationViewService.SettingsItem;
            return;
        }

        var selectedItem = NavigationViewService.GetSelectedItem(e.SourcePageType);
        if (selectedItem != null)
        {
            Selected = selectedItem;
        }
    }

    public AcrylicSettingsService AcrylicSettings
    {
        get;
    } = AcrylicSettingsService.Instance;

    public BackgroundSettingsService BackgroundSettings
    {
        get;
    } = BackgroundSettingsService.Instance;
}
