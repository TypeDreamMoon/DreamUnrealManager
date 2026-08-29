using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DreamUnrealManager.Models;

namespace DreamUnrealManager.Contracts.Services
{
    public interface IProjectCleanupService
    {
        /// <summary>扫描项目下可清理的生成目录，返回各类别的占用大小。不做任何删除。</summary>
        Task<CleanupScanResult> ScanAsync(string projectDirectory, CancellationToken cancellationToken = default);

        /// <summary>删除指定类别的生成目录。</summary>
        Task<CleanupResult> CleanAsync(
            CleanupScanResult scan,
            IReadOnlyCollection<CleanupCategory> categories,
            IProgress<string>? log = null,
            CancellationToken cancellationToken = default);
    }
}
