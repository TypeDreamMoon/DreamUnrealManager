using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;

namespace DreamUnrealManager.Services
{
    public sealed class DialogService : IDialogService
    {
        public async Task ShowMessageAsync(string title, string content)
        {
            var dlg = new ContentDialog
            {
                Title = title,
                Content = content,
                CloseButtonText = "确定",
                XamlRoot = App.MainWindow.Content.XamlRoot
            };
            await dlg.ShowAsync();
        }

        public async Task<bool> ShowConfirmAsync(string title, string content)
        {
            var dlg = new ContentDialog
            {
                Title = title,
                Content = content,
                PrimaryButtonText = "确定",
                CloseButtonText = "取消",
                XamlRoot = App.MainWindow.Content.XamlRoot
            };
            var result = await dlg.ShowAsync();
            return result == ContentDialogResult.Primary;
        }
    }
}