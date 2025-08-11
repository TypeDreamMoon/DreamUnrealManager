using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2;
using System;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Microsoft.UI.Xaml;

namespace DreamUnrealManager.Views
{
    public sealed partial class FabPage : Page
    {
        public FabPage()
        {
            this.InitializeComponent();
            Loaded += FabPage_Loaded;
        }

        // 在页面加载时初始化 WebView2
        private async void FabPage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // 等待 WebView2 初始化完成
                await FabWebView.EnsureCoreWebView2Async();
                // 加载 Web 页面（可以是本地 HTML 文件或远程 URL）
                FabWebView.Source = new Uri("https://fab.com/");
            }
            catch (Exception ex)
            {
                // 捕获 WebView2 初始化或加载错误
                Console.WriteLine($"WebView2 初始化失败: {ex.Message}");
            }
        }
    }
}