using System;
using System.Collections.Generic;
using System.Linq;
using DreamUnrealManager.Contracts.Services;
using DreamUnrealManager.Models;

namespace DreamUnrealManager.Services
{
    public sealed class ProjectFilterOptions
    {
        public string SearchText
        {
            get;
            set;
        } = "";

        public string EngineFilter
        {
            get;
            set;
        } = "ALL_ENGINES";

        public string SortOrder
        {
            get;
            set;
        } = "LastUsed"; // Name | Engine | Size | Modified | LastUsed

        public bool OnlyFavorites
        {
            get;
            set;
        } = false; // 只看收藏

        public bool FavoriteFirst
        {
            get;
            set;
        } = true; // 收藏置顶
    }

    // ⬇️ 实现接口
    public sealed class ProjectFilterService : IProjectFilterService
    {
        public IEnumerable<ProjectInfo> FilterAndSort(IEnumerable<ProjectInfo> projects, ProjectFilterOptions opt)
        {
            if (projects == null) return Enumerable.Empty<ProjectInfo>();
            opt ??= new ProjectFilterOptions();

            var q = projects;

            // 搜索
            if (!string.IsNullOrWhiteSpace(opt.SearchText))
            {
                var s = opt.SearchText.Trim().ToLowerInvariant();
                q = q.Where(p =>
                    (p.DisplayName?.ToLowerInvariant().Contains(s) ?? false) ||
                    (p.DescriptionDisplay?.ToLowerInvariant().Contains(s) ?? false) ||
                    (p.EngineAssociation?.ToLowerInvariant().Contains(s) ?? false) ||
                    (p.ProjectDirectory?.ToLowerInvariant().Contains(s) ?? false));
            }

            // 引擎筛选
            if (!string.Equals(opt.EngineFilter, "ALL_ENGINES", StringComparison.OrdinalIgnoreCase))
            {
                q = q.Where(p => string.Equals(p.EngineAssociation, opt.EngineFilter, StringComparison.OrdinalIgnoreCase));
            }

            // 只看收藏
            if (opt.OnlyFavorites)
            {
                q = q.Where(p => p.IsFavorite);
            }

            // 排序（支持收藏置顶）
            IOrderedEnumerable<ProjectInfo> ordered;
            if (opt.FavoriteFirst)
            {
                ordered = ThenSort(q.OrderByDescending(p => p.IsFavorite), opt.SortOrder);
            }
            else
            {
                ordered = FirstSort(q, opt.SortOrder);
            }

            return ordered;
        }

        private static IOrderedEnumerable<ProjectInfo> FirstSort(IEnumerable<ProjectInfo> seq, string sort)
        {
            return sort switch
            {
                "Name" => seq.OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase),
                "Engine" => seq.OrderBy(p => p.EngineAssociation, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase),
                "Size" => seq.OrderByDescending(p => p.ProjectSize)
                    .ThenBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase),
                "Modified" => seq.OrderByDescending(p => p.LastModified)
                    .ThenBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase),
                _ => seq.OrderByDescending(p => p.LastUsed ?? DateTime.MinValue)
                    .ThenBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            };
        }

        private static IOrderedEnumerable<ProjectInfo> ThenSort(IOrderedEnumerable<ProjectInfo> seq, string sort)
        {
            return sort switch
            {
                "Name" => seq.ThenBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase),
                "Engine" => seq.ThenBy(p => p.EngineAssociation, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase),
                "Size" => seq.ThenByDescending(p => p.ProjectSize)
                    .ThenBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase),
                "Modified" => seq.ThenByDescending(p => p.LastModified)
                    .ThenBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase),
                _ => seq.ThenByDescending(p => p.LastUsed ?? DateTime.MinValue)
                    .ThenBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            };
        }
    }
}