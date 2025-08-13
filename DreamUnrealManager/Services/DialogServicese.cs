using System.Threading.Tasks;
using DreamUnrealManager.Contracts.Services;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

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

        public async Task<bool> ShowWarningConfirmAsync(string title, string content, string primaryText = "继续", string closeText = "取消")
        {
            var stack = new StackPanel { Spacing = 8 };
            stack.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new FontIcon { Glyph = "\uE814", Foreground = new SolidColorBrush(Colors.Red) }, // Warning icon
                    new TextBlock { Text = "此操作具有风险，请谨慎继续。", Foreground = new SolidColorBrush(Colors.Red), FontWeight = FontWeights.SemiBold }
                }
            });
            stack.Children.Add(new TextBlock { Text = content, TextWrapping = TextWrapping.Wrap });

            var dlg = new ContentDialog
            {
                Title = title,
                Content = stack,
                PrimaryButtonText = primaryText,
                CloseButtonText = closeText,
                XamlRoot = App.MainWindow.Content.XamlRoot
            };
            var result = await dlg.ShowAsync();
            return result == ContentDialogResult.Primary;
        }

        public async Task<string?> ShowInputAsync(string title, string message, string? placeholder = null, string primaryText = "确认", string closeText = "取消")
        {
            var tb = new TextBox { PlaceholderText = placeholder ?? string.Empty };

            var content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    tb
                }
            };

            var dlg = new ContentDialog
            {
                Title = title,
                Content = content,
                PrimaryButtonText = primaryText,
                CloseButtonText = closeText,
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = App.MainWindow.Content.XamlRoot
            };

            var result = await dlg.ShowAsync();
            if (result == ContentDialogResult.Primary)
                return tb.Text;
            return null;
        }
    }
}