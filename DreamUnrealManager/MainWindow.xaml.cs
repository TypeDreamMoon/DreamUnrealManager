using DreamUnrealManager.Helpers;
using DreamUnrealManager.Services;
using Microsoft.UI.Windowing;
using Windows.UI.ViewManagement;

namespace DreamUnrealManager;

public sealed partial class MainWindow : WindowEx
{
    public MainWindow()
    {
        InitializeComponent();

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets/WindowIcon.ico"));
        AppWindow.Closing += AppWindow_Closing;
        Content = null;
        Title = "Dream Unreal Manager";
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        try
        {
            var closeToBackground = SettingsService.Get("App.CloseToBackground", false);
            if (!closeToBackground)
            {
                return;
            }

            args.Cancel = true;
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Minimize();
            }
        }
        catch
        {
            // ignore and let window close if reading settings fails
        }
    }
}
