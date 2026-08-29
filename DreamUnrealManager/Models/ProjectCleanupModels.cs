using System;
using System.Collections.Generic;

namespace DreamUnrealManager.Models
{
    /// <summary>可清理的生成目录类别。</summary>
    public enum CleanupCategory
    {
        /// <summary>编译产物 Binaries（项目与各插件）。</summary>
        Binaries,

        /// <summary>中间产物 Intermediate（项目与各插件）。</summary>
        Intermediate,

        /// <summary>本地派生数据缓存 DerivedDataCache。</summary>
        DerivedDataCache,

        /// <summary>Saved 下的日志、崩溃、自动保存等；不含 Saved/Config。</summary>
        Saved,

        /// <summary>IDE 生成的 .vs / .idea 缓存目录。</summary>
        IdeCache
    }

    /// <summary>一个待清理目录的扫描结果。</summary>
    public sealed class CleanupEntry
    {
        public required CleanupCategory Category { get; init; }

        /// <summary>绝对路径。</summary>
        public required string FullPath { get; init; }

        /// <summary>相对项目根目录的路径，用于展示。</summary>
        public required string RelativePath { get; init; }

        public long SizeBytes { get; set; }

        public int FileCount { get; set; }
    }

    /// <summary>某个类别的汇总。</summary>
    public sealed class CleanupCategorySummary
    {
        public required CleanupCategory Category { get; init; }

        public required string DisplayName { get; init; }

        /// <summary>这个类别做什么用、删掉之后会怎样。</summary>
        public required string Description { get; init; }

        public List<CleanupEntry> Entries { get; } = new();

        public long TotalBytes => Entries.Sum(e => e.SizeBytes);

        public int TotalFiles => Entries.Sum(e => e.FileCount);

        public bool HasContent => Entries.Count > 0;
    }

    public sealed class CleanupScanResult
    {
        public required string ProjectDirectory { get; init; }

        public List<CleanupCategorySummary> Categories { get; } = new();

        public long TotalBytes => Categories.Sum(c => c.TotalBytes);

        /// <summary>扫描过程中的问题（无权限目录等），不影响其余结果。</summary>
        public List<string> Warnings { get; } = new();
    }

    public sealed class CleanupResult
    {
        public long FreedBytes { get; set; }

        public int DeletedDirectories { get; set; }

        public List<string> Failures { get; } = new();

        public bool Success => Failures.Count == 0;
    }

    public static class ByteSizeFormatter
    {
        private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB" };

        public static string Format(long bytes)
        {
            if (bytes <= 0)
            {
                return "0 B";
            }

            double value = bytes;
            var unit = 0;
            while (value >= 1024 && unit < Units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return unit == 0
                ? $"{bytes} {Units[unit]}"
                : $"{value:0.##} {Units[unit]}";
        }
    }
}
