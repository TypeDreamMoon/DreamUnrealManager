using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DreamUnrealManager.Contracts.Services;
using DreamUnrealManager.Models;
using DreamUnrealManager.Services;
using DreamUnrealManager.Views;
using Microsoft.UI.Xaml.Navigation;

namespace DreamUnrealManager.ViewModels;

public partial class ShellViewModel : ObservableRecipient
{
    [ObservableProperty] private bool isBackEnabled;

    [ObservableProperty] private object? selected;

    [ObservableProperty] private string commandText;

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