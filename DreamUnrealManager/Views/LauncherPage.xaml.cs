using DreamUnrealManager.ViewModels;

using Microsoft.UI.Xaml.Controls;

namespace DreamUnrealManager.Views;

public sealed partial class LauncherPage : Page
{
    public LauncherViewModel ViewModel
    {
        get;
    }

    public LauncherPage()
    {
        ViewModel = App.GetService<LauncherViewModel>();
        InitializeComponent();
    }
}
