using CommunityToolkit.Mvvm.ComponentModel;
using DreamUnrealManager.Services;

namespace DreamUnrealManager.ViewModels;

public partial class MainViewModel : ObservableRecipient
{
    public MainViewModel()
    {
    }
    
    public AcrylicSettingsService AcrylicSettings
    {
        get;
    } = AcrylicSettingsService.Instance;
}
