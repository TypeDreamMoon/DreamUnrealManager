// Services/ProgressLogDialog.cs
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Threading;

namespace DreamUnrealManager.Services
{
    public sealed class ProgressLogDialog
    {
        private readonly ContentDialog _dlg;
        private readonly ProgressBar _bar;
        private readonly TextBox _tb;
        private readonly Button _primaryBtn;

        private readonly CancellationTokenSource _cts;
        public CancellationToken Token => _cts.Token;

        public ProgressLogDialog(string title, XamlRoot xamlRoot)
        {
            _cts = new CancellationTokenSource();

            _tb = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Consolas"),
                MinHeight = 180
            };

            _bar = new ProgressBar
            {
                IsIndeterminate = true,
                Minimum = 0,
                Maximum = 100,
                Margin = new Thickness(0, 0, 0, 8)
            };

            var copyBtn = new Button { Content = "复制日志", Margin = new Thickness(0, 6, 0, 0) };
            copyBtn.Click += (_, __) =>
            {
                try { Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(new Windows.ApplicationModel.DataTransfer.DataPackage { RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy, }); var dp = new Windows.ApplicationModel.DataTransfer.DataPackage(); dp.SetText(_tb.Text); Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp); }
                catch { }
            };

            var stack = new StackPanel { Spacing = 8 };
            stack.Children.Add(_bar);
            stack.Children.Add(_tb);
            stack.Children.Add(copyBtn);

            _dlg = new ContentDialog
            {
                Title = title,
                Content = stack,
                PrimaryButtonText = "取消",
                CloseButtonText = "关闭",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = xamlRoot
            };

            _dlg.PrimaryButtonClick += (_, e) =>
            {
                e.Cancel = true;         // 不直接关闭，走取消流程
                TryCancel();
            };
        }

        public void TryCancel()
        {
            try { _cts.Cancel(); } catch { }
            AppendLine("[取消请求已发出]");
            SetIndeterminate(true);
        }

        public async System.Threading.Tasks.Task ShowAsync() => await _dlg.ShowAsync();

        public void Complete(bool success)
        {
            SetProgress(100);
            SetIndeterminate(false);
            AppendLine(success ? "[完成]" : "[失败]");
            // 取消按钮变成不可用
            _dlg.PrimaryButtonText = null;
        }

        public void AppendLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            if (_tb.DispatcherQueue?.HasThreadAccess == true)
            {
                _tb.Text += ( _tb.Text.Length > 0 ? Environment.NewLine : string.Empty) + line;
            }
            else
            {
                _tb.DispatcherQueue.TryEnqueue(() => AppendLine(line));
            }
        }

        public void SetProgress(int v)
        {
            if (_bar.DispatcherQueue?.HasThreadAccess == true)
            {
                _bar.IsIndeterminate = false;
                _bar.Value = Math.Clamp(v, 0, 100);
            }
            else
            {
                _bar.DispatcherQueue.TryEnqueue(() => SetProgress(v));
            }
        }

        public void SetIndeterminate(bool b)
        {
            if (_bar.DispatcherQueue?.HasThreadAccess == true)
            {
                _bar.IsIndeterminate = b;
            }
            else
            {
                _bar.DispatcherQueue.TryEnqueue(() => SetIndeterminate(b));
            }
        }
    }
}
