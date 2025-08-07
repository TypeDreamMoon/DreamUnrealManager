using DreamUnrealManager.ViewModels;

using Microsoft.UI.Xaml.Controls;

namespace DreamUnrealManager.Views;

public sealed partial class PluginsBuildPage : Page
{
    public PluginsBuildViewModel ViewModel
    {
        get;
    }

    public PluginsBuildPage()
    {
        ViewModel = App.GetService<PluginsBuildViewModel>();
        InitializeComponent();
    }
}
