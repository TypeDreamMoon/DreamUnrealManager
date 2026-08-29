using System.Threading;
using System.Threading.Tasks;

namespace DreamUnrealManager.Contracts.Services
{
    public enum UpdateCheckStatus
    {
        /// <summary>已是最新。</summary>
        UpToDate,

        /// <summary>远端有更新的版本。</summary>
        UpdateAvailable,

        /// <summary>本机版本比远端还新（本地开发构建的常见情况）。</summary>
        Ahead,

        /// <summary>检查失败（无网络、限流、tag 无法解析等）。</summary>
        Failed
    }

    /// <summary>
    /// 版本检查结果。把“检查失败”与“有新版本”彻底分成两种状态，
    /// 避免此前那种离线时把错误信息当成版本号、从而永远提示有更新的问题。
    /// </summary>
    public sealed class UpdateCheckResult
    {
        public required UpdateCheckStatus Status { get; init; }

        public required string CurrentVersion { get; init; }

        /// <summary>远端最新版本；检查失败时为 null。</summary>
        public string? LatestVersion { get; init; }

        /// <summary>失败原因；仅在 Status 为 Failed 时有值。</summary>
        public string? Error { get; init; }

        /// <summary>Release 页面地址，供 UI 跳转。</summary>
        public string? ReleaseUrl { get; init; }

        public bool HasUpdate => Status == UpdateCheckStatus.UpdateAvailable;
    }

    public interface IVersionCheckService
    {
        Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);
    }
}
