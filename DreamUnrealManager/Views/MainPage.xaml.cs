using DreamUnrealManager.ViewModels;

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
}
