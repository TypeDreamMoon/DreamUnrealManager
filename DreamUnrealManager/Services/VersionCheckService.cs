using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DreamUnrealManager.Contracts.Services;
using DreamUnrealManager.Helpers;

namespace DreamUnrealManager.Services
{
    /// <summary>
    /// 通过 GitHub Releases API 检查新版本。
    ///
    /// 关键修正：网络失败时返回 Failed 状态而不是把错误信息当版本号返回，
    /// 否则调用方的 latest != current 恒为真，离线用户会永远看到“发现新版本”。
    /// </summary>
    public sealed class VersionCheckService : IVersionCheckService
    {
        private const string Owner = "TypeDreamMoon";
        private const string Repo = "DreamUnrealManager";
        private const string ReleasesPage = "https://github.com/TypeDreamMoon/DreamUnrealManager/releases/latest";

        // 静态复用：每次检查都 new HttpClient 会耗尽 socket，且丢失连接池与 DNS 刷新。
        private static readonly HttpClient Client = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            // GitHub API 强制要求 User-Agent，缺失会直接 403。
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DreamUnrealManager");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }

        public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            var currentText = App.AppVersion;

            if (!SemanticVersion.TryParse(currentText, out var current))
            {
                AppLogService.Warn($"无法解析本机版本号: {currentText}", "Update");
                return new UpdateCheckResult
                {
                    Status = UpdateCheckStatus.Failed,
                    CurrentVersion = currentText,
                    Error = "无法解析本机版本号",
                    ReleaseUrl = ReleasesPage
                };
            }

            try
            {
                var url = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
                using var response = await Client.GetAsync(url, cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    // 限流（403 + rate limit）在没配 token 时很常见，单独给一句人话提示。
                    var reason = response.StatusCode == HttpStatusCode.Forbidden
                        ? "GitHub API 请求频率受限，请稍后再试"
                        : $"GitHub 返回 {(int)response.StatusCode} {response.ReasonPhrase}";

                    AppLogService.Warn($"检查更新失败: {reason}", "Update");
                    return Failed(currentText, reason);
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var document = JsonDocument.Parse(json);

                if (!document.RootElement.TryGetProperty("tag_name", out var tagElement) ||
                    tagElement.GetString() is not { Length: > 0 } tag)
                {
                    return Failed(currentText, "响应中没有 tag_name 字段");
                }

                if (!SemanticVersion.TryParse(tag, out var latest))
                {
                    AppLogService.Warn($"无法解析远端 tag: {tag}", "Update");
                    return Failed(currentText, $"无法解析远端版本号 {tag}");
                }

                var htmlUrl = document.RootElement.TryGetProperty("html_url", out var urlElement)
                    ? urlElement.GetString()
                    : null;

                var status = latest.CompareTo(current) switch
                {
                    > 0 => UpdateCheckStatus.UpdateAvailable,
                    < 0 => UpdateCheckStatus.Ahead,
                    _ => UpdateCheckStatus.UpToDate
                };

                AppLogService.Info($"检查更新完成: 本机 {current}，远端 {latest}，结果 {status}", "Update");

                return new UpdateCheckResult
                {
                    Status = status,
                    CurrentVersion = current.ToString(),
                    LatestVersion = latest.ToString(),
                    ReleaseUrl = string.IsNullOrWhiteSpace(htmlUrl) ? ReleasesPage : htmlUrl
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException)
            {
                // HttpClient 超时也走 TaskCanceledException，这里与用户取消区分开。
                return Failed(currentText, "请求超时");
            }
            catch (Exception ex)
            {
                AppLogService.Error("检查更新异常", ex, "Update");
                return Failed(currentText, ex.Message);
            }
        }

        private static UpdateCheckResult Failed(string currentVersion, string error) => new()
        {
            Status = UpdateCheckStatus.Failed,
            CurrentVersion = currentVersion,
            Error = error,
            ReleaseUrl = ReleasesPage
        };
    }
}
