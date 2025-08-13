using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Diagnostics;
using Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;
using DreamUnrealManager.Contracts.Services;
using DreamUnrealManager.Services;
using DreamUnrealManager.ViewModels;

namespace DreamUnrealManager.Views
{
    public sealed partial class UnrealLauncherPage : Page
    {
        private readonly IEditorLaunchService _editorLaunchService =
            new EditorLaunchService(new DialogService());
        
        public UnrealLauncherViewModel ViewModel => (UnrealLauncherViewModel)DataContext;

        public UnrealLauncherPage()
        {
            InitializeComponent();
            Loaded += UnrealLauncherPage_Loaded;
        }

        private async void UnrealLauncherPage_Loaded(object sender, RoutedEventArgs e)
        {
            await ViewModel.LoadAsync();
            UpdateEmptyState();
            ViewModel.Engines.CollectionChanged += (_, __) => UpdateEmptyState();
            ViewModel.PropertyChanged += (_, __) => UpdateEmptyState();
        }

        private void UpdateEmptyState()
        {
            if (EmptyState == null) return;
            var empty = ViewModel.FilteredEngines.Count == 0;
            EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                ViewModel.ApplyFilters();
                UpdateEmptyState();
            }
        }

        private void ClearFilters_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.SearchText = string.Empty;
            ViewModel.SelectedSortOption = ViewModel.SortOptions[0];
            ViewModel.ApplyFilters();
        }

        private async void AddEngine_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FolderPicker();
                picker.FileTypeFilter.Add("*");
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                var folder = await picker.PickSingleFolderAsync();
                if (folder == null) return;

                await ViewModel.AddEngineAsync(folder.Path);
            }
            catch (Exception ex)
            {
                await ViewModel.ToastAsync($"添加失败：{ex.Message}");
            }
        }

        private async void RefreshAll_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.RefreshAllAsync();
        }

        private void OpenStore_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var path = ViewModel.GetStorePath();
                Process.Start("explorer.exe", path);
            }
            catch
            {
            }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string path && !string.IsNullOrEmpty(path))
            {
                try
                {
                    Process.Start("explorer.exe", path);
                }
                catch
                {
                }
            }
        }

        private void CopyPath_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string path && !string.IsNullOrEmpty(path))
            {
                try
                {
                    var dp = new DataPackage();
                    dp.SetText(path);
                    Clipboard.SetContent(dp);
                    _ = ViewModel.ToastAsync("已复制引擎路径");
                }
                catch
                {
                }
            }
        }

        private async void RefreshOne_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string id)
            {
                await ViewModel.RefreshOneAsync(id);
            }
        }

        private async void Remove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string id)
            {
                await ViewModel.RemoveAsync(id);
            }
        }

        private async void SetDefault_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string id)
            {
                await ViewModel.SetDefaultAsync(id);
            }
        }

        private async void OpenEditor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string enginePath && !string.IsNullOrWhiteSpace(enginePath))
            {
                await _editorLaunchService.LaunchEditorForEnginePathAsync(enginePath);
            }
        }
    }
}