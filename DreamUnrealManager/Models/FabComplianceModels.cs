using System;
using System.Collections.Generic;
using System.Linq;

namespace DreamUnrealManager.Models
{
    /// <summary>
    /// Fab 上架合规检查项的结果状态。
    /// </summary>
    public enum ComplianceStatus
    {
        /// <summary>通过。</summary>
        Pass,

        /// <summary>未通过（必须修复才能过审）。</summary>
        Fail,

        /// <summary>警告（强烈建议修复）。</summary>
        Warning,

        /// <summary>推荐项（缺失但并非强制）。</summary>
        Recommended,

        /// <summary>需人工确认（无法从本地文件自动判断）。</summary>
        Manual,

        /// <summary>信息/不适用（例如纯代码插件没有 Content 目录）。</summary>
        Info
    }

    /// <summary>
    /// 审核清单的分组，对应 Fab Technical Review Checklist 的各个板块。
    /// </summary>
    public enum ComplianceCategory
    {
        CodePlugin,
        Content,
        ProductListing,
        Documentation,
        Quality,
        Legal
    }

    /// <summary>
    /// 可自动修复的类型（仅限简单且安全、可被 Git 回滚的操作）。
    /// </summary>
    public enum AutoFixKind
    {
        None,

        /// <summary>删除 Binaries/Build/Intermediate/Saved/DerivedDataCache 等生成目录。</summary>
        CleanGeneratedFolders,

        /// <summary>删除空文件夹。</summary>
        RemoveEmptyFolders,

        /// <summary>为缺少版权声明的源码/头文件顶部添加版权注释。</summary>
        AddCopyrightNotice,

        /// <summary>在 .uplugin 中禁用 ModelingToolsEditorMode 依赖。</summary>
        DisableModelingTools
    }

    /// <summary>
    /// 单条合规检查项。
    /// </summary>
    public class ComplianceCheckItem
    {
        /// <summary>稳定的检查项 Id，用于定位（如 "C5"）。</summary>
        public string Id { get; set; } = string.Empty;

        public ComplianceCategory Category { get; set; }

        /// <summary>中文标题。</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>官方审核要求原文（英文）。</summary>
        public string Requirement { get; set; } = string.Empty;

        public ComplianceStatus Status { get; set; } = ComplianceStatus.Manual;

        /// <summary>检查结果说明。</summary>
        public string Detail { get; set; } = string.Empty;

        /// <summary>修复建议。</summary>
        public string Recommendation { get; set; } = string.Empty;

        /// <summary>是否为自动检查项（否则为人工确认项）。</summary>
        public bool IsAutomated { get; set; }

        /// <summary>违规的文件 / 目录 / 模块列表。</summary>
        public List<string> Offenders { get; set; } = new();

        /// <summary>人工检查项的勾选状态。</summary>
        public bool ManualChecked { get; set; }

        /// <summary>该项支持的自动修复类型（None 表示不支持）。</summary>
        public AutoFixKind AutoFix { get; set; } = AutoFixKind.None;
    }

    /// <summary>
    /// 一次完整合规检查的报告。
    /// </summary>
    public class ComplianceReport
    {
        public string PluginPath { get; set; } = string.Empty;

        public string PluginFolder { get; set; } = string.Empty;

        public string PluginName { get; set; } = string.Empty;

        public string PluginVersion { get; set; } = string.Empty;

        /// <summary>发布者（.uplugin 的 CreatedBy），用于自动添加版权声明。</summary>
        public string Publisher { get; set; } = string.Empty;

        public string EngineVersion { get; set; } = string.Empty;

        public DateTime GeneratedAt { get; set; } = DateTime.Now;

        public List<ComplianceCheckItem> Items { get; set; } = new();

        public int PassCount => Items.Count(i => i.Status == ComplianceStatus.Pass);

        public int FailCount => Items.Count(i => i.Status == ComplianceStatus.Fail);

        public int WarningCount => Items.Count(i => i.Status == ComplianceStatus.Warning);

        public int RecommendedCount => Items.Count(i => i.Status == ComplianceStatus.Recommended);

        public int ManualCount => Items.Count(i => i.Status == ComplianceStatus.Manual);

        public int InfoCount => Items.Count(i => i.Status == ComplianceStatus.Info);

        public int AutomatedCount => Items.Count(i => i.IsAutomated);

        /// <summary>
        /// 综合结论：有 Fail 视为“不通过”，仅有 Warning 视为“基本通过”，否则“自动检查通过”。
        /// </summary>
        public string GetVerdict()
        {
            if (FailCount > 0)
            {
                return $"不通过 · 有 {FailCount} 项必须修复";
            }

            if (WarningCount > 0)
            {
                return $"基本通过 · 建议处理 {WarningCount} 项警告";
            }

            return $"自动检查通过 · 仍需人工确认 {ManualCount} 项";
        }
    }
}
