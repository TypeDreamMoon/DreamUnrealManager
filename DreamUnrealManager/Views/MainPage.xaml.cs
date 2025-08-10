using DreamUnrealManager.Contracts.Services;
using DreamUnrealManager.Helpers;
using DreamUnrealManager.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DreamUnrealManager.Views;

public sealed partial class MainPage : Page
{
    public MainViewModel ViewModel
    {
        get;
    }

    public MainPage()
    {
        ViewModel = App.GetService<MainViewModel>();
        InitializeComponent();
    }

    private void QuickBuildingButton_OnClick(object sender, RoutedEventArgs e)
    {
        App.GetService<INavigationService>().Frame.Navigate(typeof(PluginsBuildPage));
    }

    private void QuickLaunchButton_OnClick(object sender, RoutedEventArgs e)
    {
        App.GetService<INavigationService>().Frame.Navigate(typeof(LauncherPage));
    }
}