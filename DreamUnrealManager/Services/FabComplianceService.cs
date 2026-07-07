using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DreamUnrealManager.Helpers;
using DreamUnrealManager.Models;

namespace DreamUnrealManager.Services
{
    /// <summary>
    /// 依据 Fab (Epic) Technical Review Checklist 对本地插件进行自动 + 人工合规检查。
    /// </summary>
    public class FabComplianceService
    {
        // 打包分发前必须移除的生成目录。
        private static readonly HashSet<string> GeneratedFolders = new(StringComparer.OrdinalIgnoreCase)
        {
            "Binaries", "Build", "Intermediate", "Saved", "DerivedDataCache"
        };

        // 扫描时忽略的目录（不参与路径/命名/版权检查，也不作为违规项）。
        private static readonly HashSet<string> IgnoredFolders = new(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".vs", ".svn", ".idea", "__pycache__", "node_modules"
        };

        // 插件根目录下被视为“标准”的文件夹，其余自定义文件夹需要 FilterPlugin.ini 才能随包分发。
        private static readonly HashSet<string> StandardFolders = new(StringComparer.OrdinalIgnoreCase)
        {
            "Source", "Content", "Config", "Resources", "Shaders"
        };

        private static readonly string[] CodeFileExtensions =
        {
            ".h", ".hpp", ".cpp", ".c", ".inl", ".cs", ".cc", ".hh"
        };

        private const int MaxPluginPathLength = 170;
        private const int MaxAssetPathLength = 140;
        private const int MaxOffendersShown = 20;

        public Task<ComplianceReport> AnalyzeAsync(string upluginPath, CancellationToken cancellationToken = default)
        {
            return Task.Run(() => Analyze(upluginPath, cancellationToken), cancellationToken);
        }

        private ComplianceReport Analyze(string upluginPath, CancellationToken cancellationToken)
        {
            var report = new ComplianceReport
            {
                PluginPath = upluginPath,
                GeneratedAt = DateTime.Now
            };

            if (string.IsNullOrWhiteSpace(upluginPath) || !File.Exists(upluginPath))
            {
                report.Items.Add(new ComplianceCheckItem
                {
                    Id = "ERR",
                    Category = ComplianceCategory.CodePlugin,
                    Title = "无法读取 .uplugin 文件",
                    Status = ComplianceStatus.Fail,
                    IsAutomated = true,
                    Detail = $"文件不存在: {upluginPath}"
                });
                return report;
            }

            var root = Path.GetDirectoryName(upluginPath) ?? string.Empty;
            report.PluginFolder = root;
            var pluginFolderName = Path.GetFileName(root);

            // 拒绝位于磁盘/卷根目录的 .uplugin：否则会扫描整个盘，且“一键清理”可能跨项目删除生成目录。
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(pluginFolderName)
                || string.Equals(Path.GetPathRoot(root), root, StringComparison.OrdinalIgnoreCase))
            {
                report.Items.Add(new ComplianceCheckItem
                {
                    Id = "ERR",
                    Category = ComplianceCategory.CodePlugin,
                    Title = ".uplugin 位置无效",
                    Status = ComplianceStatus.Fail,
                    IsAutomated = true,
                    Detail = "插件必须位于独立的命名文件夹中，而不能直接放在磁盘根目录。请将 .uplugin 放入其插件文件夹后重试。"
                });
                return report;
            }

            JsonElement rootEl;
            JsonDocument? doc = null;
            try
            {
                var json = File.ReadAllText(upluginPath, Encoding.UTF8);
                doc = JsonDocument.Parse(json, new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });
                rootEl = doc.RootElement;
            }
            catch (Exception ex)
            {
                report.Items.Add(new ComplianceCheckItem
                {
                    Id = "ERR",
                    Category = ComplianceCategory.CodePlugin,
                    Title = ".uplugin 解析失败",
                    Status = ComplianceStatus.Fail,
                    IsAutomated = true,
                    Detail = ex.Message
                });
                return report;
            }

            using (doc)
            {
                report.PluginName = GetStringProp(rootEl, "FriendlyName")
                                    ?? Path.GetFileNameWithoutExtension(upluginPath);
                report.PluginVersion = GetStringProp(rootEl, "VersionName") ?? string.Empty;
                report.Publisher = GetStringProp(rootEl, "CreatedBy") ?? string.Empty;
                report.EngineVersion = GetStringProp(rootEl, "EngineVersion") ?? string.Empty;

                // 预先枚举一次可分发文件，供多个检查复用。
                List<string> distributableFiles;
                try
                {
                    distributableFiles = EnumerateDistributableFiles(root).ToList();
                }
                catch
                {
                    distributableFiles = new List<string>();
                }

                cancellationToken.ThrowIfCancellationRequested();

                // ---------- 代码插件 (.uplugin) ----------
                report.Items.Add(CheckEngineVersion(rootEl));
                report.Items.Add(CheckModulePlatforms(rootEl));
                report.Items.Add(CheckFabUrl(rootEl));
                report.Items.Add(CheckCopyright(root, distributableFiles));
                report.Items.Add(CheckGeneratedFolders(root));
                report.Items.Add(CheckFilterPlugin(root));
                report.Items.Add(CheckPluginPathLength(root, pluginFolderName, distributableFiles));
                report.Items.Add(BuildManualItem("C8", ComplianceCategory.CodePlugin,
                    "插件编译无错误或严重警告",
                    "Plugin generates no errors or consequential warnings.",
                    "需要对目标引擎实际打包构建来验证。可使用『插件构建』页面进行单/多引擎构建并查看错误与警告统计。"));

                cancellationToken.ThrowIfCancellationRequested();

                // ---------- 内容与文件 ----------
                report.Items.Add(CheckContentPackFolder(root, report.PluginName));
                report.Items.Add(CheckAssetPathLength(root, pluginFolderName));
                report.Items.Add(CheckEmptyFolders(root));
                report.Items.Add(CheckNaming(root, pluginFolderName, distributableFiles));
                report.Items.Add(CheckEnabledPlugins(rootEl));
                report.Items.Add(BuildManualItem("F2", ComplianceCategory.Content,
                    "各类资产位于对应的文件夹内",
                    "All asset types are inside of their respective folders.",
                    "在编辑器中确认 Blueprint / Material / Texture 等资产按类型归入子文件夹。"));
                report.Items.Add(BuildManualItem("F5", ComplianceCategory.Content,
                    "已清理所有重定向器 (Redirectors)",
                    "All Redirectors are cleaned up.",
                    "在内容浏览器中右键 Content 目录 → Fix Up Redirectors in Folder。"));

                // ---------- 商品页信息（人工） ----------
                report.Items.Add(BuildManualItem("PL1", ComplianceCategory.ProductListing,
                    "所有文本准确且与资产相关",
                    "All text must be accurate and relevant to the asset."));
                report.Items.Add(BuildManualItem("PL2", ComplianceCategory.ProductListing,
                    "所有文本字段包含英文版本",
                    "All text fields must contain an English version."));
                report.Items.Add(BuildManualItem("PL3", ComplianceCategory.ProductListing,
                    "媒体（截图/视频）准确展示功能或内容",
                    "Media must accurately display the relevant functionality or contents of the project."));
                report.Items.Add(BuildManualItem("PL4", ComplianceCategory.ProductListing,
                    "技术信息 (Technical Information) 字段全部填写完整",
                    "All Technical Information fields must be filled out in their entirety."));
                report.Items.Add(BuildManualItem("PL5", ComplianceCategory.ProductListing,
                    "技术信息中标明依赖、前置条件或其它使用要求",
                    "Technical Information text must identify dependencies (if any), prerequisites, or other requirements for use of the asset."));
                report.Items.Add(BuildManualItem("PL6", ComplianceCategory.ProductListing,
                    "每个项目文件链接仅包含一个 UE 项目或插件且结构正确",
                    "Each Project File Link hosts only one UE Project or Plugin folder with the proper folder structure."));
                report.Items.Add(BuildManualItem("PL7", ComplianceCategory.ProductListing,
                    "提供的项目与所列支持引擎版本一致",
                    "Project(s) provided match the Supported Engine Versions listed."));
                report.Items.Add(BuildManualItem("PL8", ComplianceCategory.ProductListing,
                    "分发方式 (Distribution Method) 与内容和功能相匹配",
                    "Distribution Method is appropriate for the content and functionality of the product."));

                // ---------- 文档 / 质量 / 法务（人工） ----------
                report.Items.Add(BuildManualItem("DOC1", ComplianceCategory.Documentation,
                    "如有需要，提供链接或编辑器内文档/教程",
                    "If needed, the Publisher provides either linked or in-editor documentation/tutorials."));
                report.Items.Add(BuildManualItem("Q1", ComplianceCategory.Quality,
                    "所有资产完整且按预期工作",
                    "All assets are complete and function as intended."));
                report.Items.Add(BuildManualItem("L1", ComplianceCategory.Legal,
                    "内容不含冒犯、低俗或诽谤性信息",
                    "Products must not be offensive, vulgar, or slanderous toward any person, group, organization, product, or in any way defaming of Epic Games, Unreal, or Fab."));
                report.Items.Add(BuildManualItem("L2", ComplianceCategory.Legal,
                    "未再分发 Megascans 内容",
                    "Megascans content cannot be re-distributed."));
                report.Items.Add(BuildManualItem("L3", ComplianceCategory.Legal,
                    "Epic 示例内容/源码仅用于展示或示例",
                    "Substantial portions of sample content or source code from Epic Games is used for display/example only."));
            }

            return report;
        }

        #region 代码插件检查

        private ComplianceCheckItem CheckEngineVersion(JsonElement rootEl)
        {
            var item = new ComplianceCheckItem
            {
                Id = "C1",
                Category = ComplianceCategory.CodePlugin,
                Title = "指定 EngineVersion（引擎版本）",
                Requirement = ".uplugin has \"EngineVersion\" key with a value of the major engine version the plugin is meant to be installed to.",
                IsAutomated = true
            };

            var value = GetStringProp(rootEl, "EngineVersion");
            if (string.IsNullOrWhiteSpace(value))
            {
                item.Status = ComplianceStatus.Fail;
                item.Detail = "未找到 EngineVersion 键。";
                item.Recommendation = "在 .uplugin 中添加，例如 \"EngineVersion\": \"5.3.0\"。";
            }
            else if (Regex.IsMatch(value, @"^\d+\.\d+(\.\d+)?$"))
            {
                item.Status = ComplianceStatus.Pass;
                item.Detail = $"EngineVersion = {value}";
            }
            else
            {
                item.Status = ComplianceStatus.Warning;
                item.Detail = $"EngineVersion 值格式异常: {value}";
                item.Recommendation = "值应形如 5.3.0（主版本.次版本.修订号）。";
            }

            return item;
        }

        private ComplianceCheckItem CheckModulePlatforms(JsonElement rootEl)
        {
            var item = new ComplianceCheckItem
            {
                Id = "C2",
                Category = ComplianceCategory.CodePlugin,
                Title = "每个模块声明平台白/黑名单",
                Requirement = ".uplugin has \"PlatformAllowList\" / \"PlatformDenyList\" key in every module that match Supported Target Platforms.",
                IsAutomated = true
            };

            if (!TryGetPropertyCI(rootEl, "Modules", out var modules) || modules.ValueKind != JsonValueKind.Array)
            {
                item.Status = ComplianceStatus.Info;
                item.Detail = "该插件未声明任何模块（可能为纯内容插件）。";
                return item;
            }

            var missing = new List<string>();
            var moduleCount = 0;
            foreach (var module in modules.EnumerateArray())
            {
                if (module.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                moduleCount++;
                var name = GetStringProp(module, "Name") ?? $"模块#{moduleCount}";
                var hasList = HasNonEmptyArray(module, "PlatformAllowList")
                              || HasNonEmptyArray(module, "PlatformDenyList")
                              || HasNonEmptyArray(module, "WhitelistPlatforms")
                              || HasNonEmptyArray(module, "BlacklistPlatforms");
                if (!hasList)
                {
                    missing.Add(name);
                }
            }

            if (moduleCount == 0)
            {
                item.Status = ComplianceStatus.Info;
                item.Detail = "该插件未声明任何模块。";
            }
            else if (missing.Count == 0)
            {
                item.Status = ComplianceStatus.Pass;
                item.Detail = $"全部 {moduleCount} 个模块均已声明平台白/黑名单。";
            }
            else
            {
                item.Status = ComplianceStatus.Warning;
                item.Detail = $"{missing.Count}/{moduleCount} 个模块未声明 PlatformAllowList/PlatformDenyList。";
                item.Offenders = missing;
                item.Recommendation = "为每个模块添加与“支持的目标平台”一致的名单，例如 \"PlatformAllowList\": [ \"Win64\" ]。";
            }

            return item;
        }

        private ComplianceCheckItem CheckFabUrl(JsonElement rootEl)
        {
            var item = new ComplianceCheckItem
            {
                Id = "C3",
                Category = ComplianceCategory.CodePlugin,
                Title = "包含 FabURL（含商品 Listing ID）",
                Requirement = ".uplugin has \"FabURL\" key with a value that includes the product's Listing ID. (Recommended)",
                IsAutomated = true
            };

            var value = GetStringProp(rootEl, "FabURL");
            if (string.IsNullOrWhiteSpace(value))
            {
                item.Status = ComplianceStatus.Recommended;
                item.Detail = "未找到 FabURL 键（推荐添加）。";
                item.Recommendation = "示例: \"FabURL\": \"com.epicgames.launcher://ue/Fab/product/<listing-id>\"。";
            }
            else if (Regex.Match(value, @"product/([^/?#\s]+)", RegexOptions.IgnoreCase) is { Success: true } m
                     && m.Groups[1].Value.Length > 0)
            {
                item.Status = ComplianceStatus.Pass;
                item.Detail = $"FabURL = {value}（Listing ID: {m.Groups[1].Value}）";
            }
            else
            {
                item.Status = ComplianceStatus.Warning;
                item.Detail = $"FabURL 已设置但未包含产品 Listing ID: {value}";
                item.Recommendation = "确保 URL 中包含 Fab/product/<listing-id>。";
            }

            return item;
        }

        private ComplianceCheckItem CheckCopyright(string root, List<string> distributableFiles)
        {
            var item = new ComplianceCheckItem
            {
                Id = "C4",
                Category = ComplianceCategory.CodePlugin,
                Title = "源码/头文件包含版权声明",
                Requirement = "All source and header files contain a commented copyright notice with Publisher name and year of publishing.",
                IsAutomated = true
            };

            var codeFiles = distributableFiles
                .Where(f => CodeFileExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();

            if (codeFiles.Count == 0)
            {
                item.Status = ComplianceStatus.Info;
                item.Detail = "未发现源码文件。";
                return item;
            }

            var missing = new List<string>();
            var unreadable = new List<string>();
            foreach (var f in codeFiles)
            {
                var result = HasCopyrightNotice(f);
                if (result == null)
                {
                    unreadable.Add(Rel(root, f));
                }
                else if (result == false)
                {
                    missing.Add(Rel(root, f));
                }
            }

            if (missing.Count == 0 && unreadable.Count == 0)
            {
                item.Status = ComplianceStatus.Pass;
                item.Detail = $"{codeFiles.Count} 个源码文件顶部均包含版权声明注释。";
            }
            else if (missing.Count > 0)
            {
                item.Status = ComplianceStatus.Fail;
                item.Detail = $"{missing.Count}/{codeFiles.Count} 个源码文件顶部缺少版权声明注释。"
                              + (unreadable.Count > 0 ? $" 另有 {unreadable.Count} 个文件无法读取以验证。" : string.Empty);
                item.Offenders = missing.Concat(unreadable.Select(u => $"{u}（无法读取）")).ToList();
                item.Recommendation = "在每个源文件/头文件顶部添加注释，例如: // Copyright <发布者> <年份>. All Rights Reserved.";
                item.AutoFix = AutoFixKind.AddCopyrightNotice;
            }
            else
            {
                item.Status = ComplianceStatus.Warning;
                item.Detail = $"{unreadable.Count} 个源码文件无法读取以验证版权声明。";
                item.Offenders = unreadable;
                item.Recommendation = "请检查文件是否被占用或权限受限后重试。";
            }

            return item;
        }

        private ComplianceCheckItem CheckGeneratedFolders(string root)
        {
            var item = new ComplianceCheckItem
            {
                Id = "C5",
                Category = ComplianceCategory.CodePlugin,
                Title = "不含 Binaries/Build/Intermediate/Saved 等生成目录",
                Requirement = "Plugin folder contains no unused or local folders (such as Binaries, Build, Intermediate, or Saved).",
                IsAutomated = true
            };

            var found = FindGeneratedFolders(root).Select(d => Rel(root, d)).OrderBy(x => x).ToList();
            if (found.Count == 0)
            {
                item.Status = ComplianceStatus.Pass;
                item.Detail = "未发现生成/本地目录。";
            }
            else
            {
                item.Status = ComplianceStatus.Fail;
                item.Detail = $"发现 {found.Count} 个需要移除的生成目录。";
                item.Offenders = found;
                item.Recommendation = "打包分发前请删除这些目录。可点击右侧“自动修复”。";
                item.AutoFix = AutoFixKind.CleanGeneratedFolders;
            }

            return item;
        }

        private ComplianceCheckItem CheckFilterPlugin(string root)
        {
            var item = new ComplianceCheckItem
            {
                Id = "C6",
                Category = ComplianceCategory.CodePlugin,
                Title = "FilterPlugin.ini 过滤自定义文件夹",
                Requirement = "FilterPlugin.ini filters in custom folders the publisher intends to distribute (Docs or similar).",
                IsAutomated = true
            };

            var filterPath = Path.Combine(root, "Config", "FilterPlugin.ini");
            var customFolders = new List<string>();
            try
            {
                customFolders = Directory.GetDirectories(root)
                    .Select(Path.GetFileName)
                    .Where(name => !string.IsNullOrEmpty(name)
                                   && !StandardFolders.Contains(name!)
                                   && !GeneratedFolders.Contains(name!)
                                   && !IgnoredFolders.Contains(name!))
                    .Select(name => name!)
                    .ToList();
            }
            catch
            {
                // 忽略枚举失败
            }

            if (File.Exists(filterPath))
            {
                if (customFolders.Count == 0)
                {
                    item.Status = ComplianceStatus.Pass;
                    item.Detail = "已存在 Config/FilterPlugin.ini。";
                }
                else
                {
                    string iniText;
                    try
                    {
                        iniText = File.ReadAllText(filterPath);
                    }
                    catch
                    {
                        iniText = string.Empty;
                    }

                    var unreferenced = customFolders
                        .Where(folder => iniText.IndexOf(folder, StringComparison.OrdinalIgnoreCase) < 0)
                        .ToList();
                    if (unreferenced.Count == 0)
                    {
                        item.Status = ComplianceStatus.Pass;
                        item.Detail = "FilterPlugin.ini 已引用检测到的自定义文件夹。";
                    }
                    else
                    {
                        item.Status = ComplianceStatus.Warning;
                        item.Detail = "FilterPlugin.ini 存在，但以下自定义文件夹未被引用，可能不会随包分发。";
                        item.Offenders = unreferenced;
                        item.Recommendation = "在 Config/FilterPlugin.ini 中通过 +AdditionalFileDirectories 声明这些目录。";
                    }
                }
            }
            else if (customFolders.Count > 0)
            {
                item.Status = ComplianceStatus.Warning;
                item.Detail = "检测到自定义文件夹但缺少 FilterPlugin.ini，这些文件夹可能不会随包分发。";
                item.Offenders = customFolders;
                item.Recommendation = "在 Config/FilterPlugin.ini 中通过 +AdditionalFileDirectories 声明要分发的目录（如 Docs）。";
            }
            else
            {
                item.Status = ComplianceStatus.Info;
                item.Detail = "未检测到需要额外过滤的自定义文件夹（仅在分发 Docs 等目录时才需要）。";
            }

            return item;
        }

        private ComplianceCheckItem CheckPluginPathLength(string root, string pluginFolderName, List<string> distributableFiles)
        {
            var item = new ComplianceCheckItem
            {
                Id = "C7",
                Category = ComplianceCategory.CodePlugin,
                Title = $"文件路径长度 ≤ {MaxPluginPathLength} 字符",
                Requirement = "All file paths, starting with the overarching plugin folder, are 170 characters or less.",
                IsAutomated = true
            };

            var offenders = new List<(string Path, int Len)>();
            var maxLen = 0;
            foreach (var file in distributableFiles)
            {
                var rel = pluginFolderName + "/" + Rel(root, file);
                maxLen = Math.Max(maxLen, rel.Length);
                if (rel.Length > MaxPluginPathLength)
                {
                    offenders.Add((rel, rel.Length));
                }
            }

            if (offenders.Count == 0)
            {
                item.Status = ComplianceStatus.Pass;
                item.Detail = distributableFiles.Count > 0
                    ? $"最长路径 {maxLen} 字符，均在限制内。"
                    : "未发现可分发文件。";
            }
            else
            {
                item.Status = ComplianceStatus.Fail;
                item.Detail = $"{offenders.Count} 个文件路径超过 {MaxPluginPathLength} 字符。";
                item.Offenders = offenders
                    .OrderByDescending(o => o.Len)
                    .Select(o => $"[{o.Len}] {o.Path}")
                    .ToList();
                item.Recommendation = "缩短文件夹层级或文件名以降低完整路径长度。";
            }

            return item;
        }

        #endregion

        #region 内容与文件检查

        private ComplianceCheckItem CheckContentPackFolder(string root, string pluginName)
        {
            var item = new ComplianceCheckItem
            {
                Id = "F1",
                Category = ComplianceCategory.Content,
                Title = "Content 下为单一 Pack 文件夹",
                Requirement = "Content folder contains a single Pack Folder named after the project (unless otherwise approved by Epic).",
                IsAutomated = true
            };

            var contentDir = Path.Combine(root, "Content");
            if (!Directory.Exists(contentDir))
            {
                item.Status = ComplianceStatus.Info;
                item.Detail = "无 Content 目录（纯代码插件时可忽略）。";
                return item;
            }

            string[] topDirs;
            string[] topFiles;
            try
            {
                topDirs = Directory.GetDirectories(contentDir);
                topFiles = Directory.GetFiles(contentDir);
            }
            catch (Exception ex)
            {
                item.Status = ComplianceStatus.Info;
                item.Detail = $"无法枚举 Content 目录: {ex.Message}";
                return item;
            }

            if (topFiles.Length > 0 || topDirs.Length != 1)
            {
                item.Status = ComplianceStatus.Warning;
                item.Detail = "Content 下应仅包含一个以插件命名的 Pack 文件夹。";
                item.Offenders = topDirs.Select(d => Path.GetFileName(d) + "/")
                    .Concat(topFiles.Select(Path.GetFileName))
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Select(n => n!)
                    .ToList();
                item.Recommendation = "将所有资产收纳进一个名为插件名的子文件夹内（除非已获 Epic 批准）。";
            }
            else
            {
                var packName = Path.GetFileName(topDirs[0]);
                if (NameLooseEquals(packName, pluginName))
                {
                    item.Status = ComplianceStatus.Pass;
                    item.Detail = $"Pack 文件夹: {packName}";
                }
                else
                {
                    item.Status = ComplianceStatus.Warning;
                    item.Detail = $"Pack 文件夹名 “{packName}” 与插件名 “{pluginName}” 不一致。";
                    item.Recommendation = "将 Pack 文件夹重命名为与插件一致（或在商品页说明）。";
                }
            }

            return item;
        }

        private ComplianceCheckItem CheckAssetPathLength(string root, string pluginFolderName)
        {
            var item = new ComplianceCheckItem
            {
                Id = "F4",
                Category = ComplianceCategory.Content,
                Title = $"资产文件路径长度 < {MaxAssetPathLength} 字符",
                Requirement = "File paths for assets are under 140 characters.",
                IsAutomated = true
            };

            var contentDir = Path.Combine(root, "Content");
            if (!Directory.Exists(contentDir))
            {
                item.Status = ComplianceStatus.Info;
                item.Detail = "无 Content 目录。";
                return item;
            }

            var offenders = new List<(string Path, int Len)>();
            foreach (var file in EnumerateDistributableFiles(contentDir))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext != ".uasset" && ext != ".umap")
                {
                    continue;
                }

                var rel = pluginFolderName + "/" + Rel(root, file);
                if (rel.Length >= MaxAssetPathLength)
                {
                    offenders.Add((rel, rel.Length));
                }
            }

            if (offenders.Count == 0)
            {
                item.Status = ComplianceStatus.Pass;
                item.Detail = "资产路径长度均在限制内。";
            }
            else
            {
                item.Status = ComplianceStatus.Warning;
                item.Detail = $"{offenders.Count} 个资产路径达到或超过 {MaxAssetPathLength} 字符。";
                item.Offenders = offenders
                    .OrderByDescending(o => o.Len)
                    .Select(o => $"[{o.Len}] {o.Path}")
                    .ToList();
                item.Recommendation = "缩短资产名称或目录层级。";
            }

            return item;
        }

        private ComplianceCheckItem CheckEmptyFolders(string root)
        {
            var item = new ComplianceCheckItem
            {
                Id = "F3",
                Category = ComplianceCategory.Content,
                Title = "不含空文件夹 / 无用目录",
                Requirement = "Project contains no unused folders or assets.",
                IsAutomated = true
            };

            var emptyDirs = new List<string>();
            try
            {
                foreach (var dir in EnumerateDistributableDirectories(root))
                {
                    bool hasEntry;
                    try
                    {
                        hasEntry = Directory.EnumerateFileSystemEntries(dir).Any();
                    }
                    catch
                    {
                        hasEntry = true;
                    }

                    if (!hasEntry)
                    {
                        emptyDirs.Add(Rel(root, dir));
                    }
                }
            }
            catch
            {
                // 忽略
            }

            if (emptyDirs.Count == 0)
            {
                item.Status = ComplianceStatus.Pass;
                item.Detail = "未发现空文件夹。";
            }
            else
            {
                item.Status = ComplianceStatus.Warning;
                item.Detail = $"发现 {emptyDirs.Count} 个空文件夹。";
                item.Offenders = emptyDirs.OrderBy(x => x).ToList();
                item.Recommendation = "移除空文件夹与未使用的资产（无法自动判断未使用资产，请人工复核）。";
                item.AutoFix = AutoFixKind.RemoveEmptyFolders;
            }

            return item;
        }

        private ComplianceCheckItem CheckNaming(string root, string pluginFolderName, List<string> distributableFiles)
        {
            var item = new ComplianceCheckItem
            {
                Id = "F6",
                Category = ComplianceCategory.Content,
                Title = "命名为英文/字母数字（无非 ASCII 字符）",
                Requirement = "Naming conventions are English, Alphanumeric, consistent throughout project, and accurately describe what the assets are.",
                IsAutomated = true
            };

            var offenders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in distributableFiles)
            {
                var name = Path.GetFileName(file);
                if (ContainsNonAscii(name))
                {
                    offenders.Add(Rel(root, file));
                }
            }

            foreach (var dir in EnumerateDistributableDirectories(root))
            {
                var name = Path.GetFileName(dir);
                if (ContainsNonAscii(name))
                {
                    offenders.Add(Rel(root, dir) + "/");
                }
            }

            if (offenders.Count == 0)
            {
                item.Status = ComplianceStatus.Pass;
                item.Detail = "未发现包含非 ASCII 字符的文件/文件夹名。";
            }
            else
            {
                item.Status = ComplianceStatus.Warning;
                item.Detail = $"发现 {offenders.Count} 个文件/文件夹名包含非 ASCII 字符。";
                item.Offenders = offenders.OrderBy(x => x).ToList();
                item.Recommendation = "使用英文与字母数字命名，避免中文、空格及特殊字符。";
            }

            return item;
        }

        private ComplianceCheckItem CheckEnabledPlugins(JsonElement rootEl)
        {
            var item = new ComplianceCheckItem
            {
                Id = "F7",
                Category = ComplianceCategory.Content,
                Title = "禁用未使用的插件依赖",
                Requirement = ".uplugin has unused plugins disabled. \"ModelingToolsEditorMode\" is enabled and not mentioned in the product page, it must be disabled or be mentioned in the description.",
                IsAutomated = true
            };

            var enabled = new List<string>();
            var modelingEnabled = false;
            if (TryGetPropertyCI(rootEl, "Plugins", out var plugins) && plugins.ValueKind == JsonValueKind.Array)
            {
                foreach (var dep in plugins.EnumerateArray())
                {
                    if (dep.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var name = GetStringProp(dep, "Name");
                    var isEnabled = TryGetPropertyCI(dep, "Enabled", out var en)
                                    && en.ValueKind == JsonValueKind.True;
                    if (isEnabled && !string.IsNullOrEmpty(name))
                    {
                        enabled.Add(name!);
                        if (string.Equals(name, "ModelingToolsEditorMode", StringComparison.OrdinalIgnoreCase))
                        {
                            modelingEnabled = true;
                        }
                    }
                }
            }

            if (modelingEnabled)
            {
                item.Status = ComplianceStatus.Warning;
                item.Detail = "ModelingToolsEditorMode 已启用。若商品描述未说明，必须将其禁用。";
                item.Offenders = enabled;
                item.Recommendation = "禁用 ModelingToolsEditorMode，或在商品描述中明确说明其用途。";
                item.AutoFix = AutoFixKind.DisableModelingTools;
            }
            else if (enabled.Count > 0)
            {
                item.Status = ComplianceStatus.Pass;
                item.Detail = $"已启用 {enabled.Count} 个插件依赖，请确认均为必需项。";
                item.Offenders = enabled;
            }
            else
            {
                item.Status = ComplianceStatus.Pass;
                item.Detail = "未声明启用额外插件依赖。";
            }

            return item;
        }

        #endregion

        #region 自动修复

        /// <summary>
        /// 删除插件目录下的 Binaries/Build/Intermediate/Saved/DerivedDataCache 目录。返回被删除的目录相对路径。
        /// </summary>
        public List<string> CleanGeneratedFolders(string pluginFolder)
        {
            var removed = new List<string>();
            if (string.IsNullOrWhiteSpace(pluginFolder) || !Directory.Exists(pluginFolder)
                || string.IsNullOrEmpty(Path.GetFileName(pluginFolder))
                || string.Equals(Path.GetPathRoot(pluginFolder), pluginFolder, StringComparison.OrdinalIgnoreCase))
            {
                return removed;
            }

            foreach (var dir in FindGeneratedFolders(pluginFolder))
            {
                try
                {
                    var rel = Rel(pluginFolder, dir);
                    Directory.Delete(dir, recursive: true);
                    removed.Add(rel);
                }
                catch
                {
                    // 忽略单个删除失败
                }
            }

            return removed;
        }

        /// <summary>
        /// 删除插件目录下的空文件夹（自底向上，连带删除因子目录被清空而变空的父目录）。返回被删除目录的相对路径。
        /// </summary>
        public List<string> RemoveEmptyFolders(string pluginFolder)
        {
            var removed = new List<string>();
            if (string.IsNullOrWhiteSpace(pluginFolder) || !Directory.Exists(pluginFolder))
            {
                return removed;
            }

            // 路径长的先处理（近似自底向上），使父目录在子目录删除后也能被判空并删除。
            var dirs = EnumerateDistributableDirectories(pluginFolder)
                .OrderByDescending(d => d.Length)
                .ToList();

            foreach (var dir in dirs)
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    {
                        var rel = Rel(pluginFolder, dir);
                        Directory.Delete(dir);
                        removed.Add(rel);
                    }
                }
                catch
                {
                    // 忽略单个删除失败
                }
            }

            return removed;
        }

        /// <summary>
        /// 为缺少版权声明的源码/头文件顶部添加版权注释。返回已修改的文件数。
        /// </summary>
        public int AddCopyrightNotices(string pluginFolder, string publisher, int year)
        {
            if (string.IsNullOrWhiteSpace(pluginFolder) || !Directory.Exists(pluginFolder))
            {
                return 0;
            }

            var owner = string.IsNullOrWhiteSpace(publisher) ? Path.GetFileName(pluginFolder) : publisher.Trim();
            var notice = $"// Copyright (C) {year} {owner}. All Rights Reserved.";

            var count = 0;
            foreach (var file in EnumerateDistributableFiles(pluginFolder))
            {
                if (!CodeFileExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                {
                    continue;
                }

                // 仅在“确认缺少”版权声明时添加；已存在或无法读取（null）都跳过，避免重复添加或误改。
                if (HasCopyrightNotice(file) != false)
                {
                    continue;
                }

                try
                {
                    var bytes = File.ReadAllBytes(file);
                    var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                    var original = File.ReadAllText(file); // 自动按 BOM 解码，返回不含 BOM 的文本

                    // 保留原文件的换行风格与 BOM，避免只加一行注释却改动整份文件的行尾/编码。
                    var newline = original.Contains("\r\n") ? "\r\n" : "\n";
                    File.WriteAllText(file, notice + newline + original, new UTF8Encoding(hasBom));
                    count++;
                }
                catch
                {
                    // 忽略单个写入失败
                }
            }

            return count;
        }

        /// <summary>
        /// 在 .uplugin 中将 ModelingToolsEditorMode 依赖的 Enabled 置为 false。返回是否有实际改动。
        /// </summary>
        public bool DisableModelingToolsEditorMode(string upluginPath)
        {
            if (string.IsNullOrWhiteSpace(upluginPath) || !File.Exists(upluginPath))
            {
                return false;
            }

            try
            {
                var text = File.ReadAllText(upluginPath);
                if (JsonNode.Parse(text) is not JsonObject rootObj || rootObj["Plugins"] is not JsonArray plugins)
                {
                    return false;
                }

                var changed = false;
                foreach (var node in plugins)
                {
                    if (node is not JsonObject dep)
                    {
                        continue;
                    }

                    string? name = null;
                    if (dep["Name"] is JsonValue nameValue)
                    {
                        nameValue.TryGetValue(out name);
                    }

                    if (string.Equals(name, "ModelingToolsEditorMode", StringComparison.OrdinalIgnoreCase))
                    {
                        dep["Enabled"] = false;
                        changed = true;
                    }
                }

                if (!changed)
                {
                    return false;
                }

                AtomicFile.WriteAllText(upluginPath,
                    rootObj.ToJsonString(new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        // 不转义中文/<>&+ 等字符，避免把未改动的 FriendlyName/Description 等字段变成 \uXXXX。
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    }));
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 判断给定插件目录是否真正处于 Git 管理下：要求该目录内确实存在被 Git 跟踪的文件，
        /// 而不仅仅是恰好嵌套在某个父仓库里（否则对已跟踪文件的改动才谈得上可回滚）。
        /// </summary>
        public async Task<bool> IsUnderGitAsync(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                return false;
            }

            var (ok, output) = await RunGitAsync(folder, "ls-files");
            return ok && !string.IsNullOrWhiteSpace(output);
        }

        /// <summary>
        /// 在指定目录运行 git 命令，带 5 秒超时并并发读取 stdout/stderr，避免挂起或管道死锁。
        /// </summary>
        private static async Task<(bool Ok, string Output)> RunGitAsync(string folder, string arguments)
        {
            Process? proc = null;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = folder,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                proc = Process.Start(psi);
                if (proc == null)
                {
                    return (false, string.Empty);
                }

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

                // 并发读取，避免只读 stdout 时 stderr 写满缓冲区导致死锁。
                var stdoutTask = proc.StandardOutput.ReadToEndAsync(cts.Token);
                var stderrTask = proc.StandardError.ReadToEndAsync(cts.Token);

                await proc.WaitForExitAsync(cts.Token);
                var stdout = await stdoutTask;
                await stderrTask;

                return (proc.ExitCode == 0, stdout);
            }
            catch
            {
                // 超时/被取消/git 未安装等：尝试结束进程并回退为“未管理”。
                try
                {
                    if (proc is { HasExited: false })
                    {
                        proc.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // 忽略
                }

                return (false, string.Empty);
            }
            finally
            {
                proc?.Dispose();
            }
        }

        #endregion

        #region 辅助方法

        private static ComplianceCheckItem BuildManualItem(string id, ComplianceCategory category, string title,
            string requirement, string recommendation = "")
        {
            return new ComplianceCheckItem
            {
                Id = id,
                Category = category,
                Title = title,
                Requirement = requirement,
                Status = ComplianceStatus.Manual,
                IsAutomated = false,
                Recommendation = recommendation
            };
        }

        /// <summary>
        /// 检查文件顶部注释区域是否包含版权声明。返回 null 表示读取失败（无法验证）。
        /// </summary>
        private static bool? HasCopyrightNotice(string file)
        {
            string[] lines;
            try
            {
                lines = ReadFirstLines(file, 30);
            }
            catch
            {
                // 读取失败（被占用/权限不足等）→ 交由上层标记为“无法验证”，而不是误判为通过。
                return null;
            }

            foreach (var raw in lines)
            {
                var trimmed = raw.TrimStart();
                var isComment = trimmed.StartsWith("//", StringComparison.Ordinal)
                                || trimmed.StartsWith("/*", StringComparison.Ordinal)
                                || trimmed.StartsWith("*", StringComparison.Ordinal);
                if (!isComment)
                {
                    continue;
                }

                if (trimmed.Contains("copyright", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("©"))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsNonAscii(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            foreach (var c in text)
            {
                if (c > 127)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool NameLooseEquals(string? a, string? b)
        {
            static string Normalize(string? s) =>
                new string((s ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

            return Normalize(a) == Normalize(b);
        }

        private static string Rel(string root, string full)
        {
            try
            {
                return Path.GetRelativePath(root, full).Replace('\\', '/');
            }
            catch
            {
                return full.Replace('\\', '/');
            }
        }

        private static string? GetStringProp(JsonElement obj, string name)
        {
            if (TryGetPropertyCI(obj, name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }

            return null;
        }

        private static bool TryGetPropertyCI(JsonElement obj, string name, out JsonElement value)
        {
            if (obj.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in obj.EnumerateObject())
                {
                    if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = prop.Value;
                        return true;
                    }
                }
            }

            value = default;
            return false;
        }

        private static bool IsReparsePoint(string path)
        {
            try
            {
                return (new DirectoryInfo(path).Attributes & FileAttributes.ReparsePoint) != 0;
            }
            catch
            {
                return false;
            }
        }

        private static string GetCanonicalPath(string path)
        {
            try
            {
                return Path.GetFullPath(path)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path;
            }
        }

        private static bool HasNonEmptyArray(JsonElement obj, string name)
        {
            return TryGetPropertyCI(obj, name, out var value)
                   && value.ValueKind == JsonValueKind.Array
                   && value.GetArrayLength() > 0;
        }

        private static string[] ReadFirstLines(string file, int maxLines)
        {
            var lines = new List<string>(maxLines);
            using var reader = new StreamReader(file, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            string? line;
            while (lines.Count < maxLines && (line = reader.ReadLine()) != null)
            {
                lines.Add(line);
            }

            return lines.ToArray();
        }

        private static IEnumerable<string> FindGeneratedFolders(string root)
        {
            var result = new List<string>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var dir = stack.Pop();
                if (!visited.Add(GetCanonicalPath(dir)))
                {
                    // 已访问过（可能是符号链接/联接造成的环），跳过以避免死循环。
                    continue;
                }

                string[] subDirs;
                try
                {
                    subDirs = Directory.GetDirectories(dir);
                }
                catch
                {
                    continue;
                }

                foreach (var sub in subDirs)
                {
                    var name = Path.GetFileName(sub);
                    if (GeneratedFolders.Contains(name))
                    {
                        result.Add(sub);
                        // 不再深入已判定为生成目录的子树。
                        continue;
                    }

                    if (IgnoredFolders.Contains(name) || IsReparsePoint(sub))
                    {
                        continue;
                    }

                    stack.Push(sub);
                }
            }

            return result;
        }

        private static IEnumerable<string> EnumerateDistributableFiles(string root)
        {
            foreach (var dir in EnumerateDistributableDirectoriesInclusive(root))
            {
                string[] files;
                try
                {
                    files = Directory.GetFiles(dir);
                }
                catch
                {
                    continue;
                }

                foreach (var f in files)
                {
                    yield return f;
                }
            }
        }

        private static IEnumerable<string> EnumerateDistributableDirectories(string root)
        {
            // 不含 root 自身，仅返回其可分发子目录。
            return EnumerateDistributableDirectoriesInclusive(root).Where(d =>
                !string.Equals(d, root, StringComparison.OrdinalIgnoreCase));
        }

        private static IEnumerable<string> EnumerateDistributableDirectoriesInclusive(string root)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var dir = stack.Pop();
                if (!visited.Add(GetCanonicalPath(dir)))
                {
                    // 已访问过（可能是符号链接/联接造成的环），跳过以避免死循环。
                    continue;
                }

                yield return dir;

                string[] subDirs;
                try
                {
                    subDirs = Directory.GetDirectories(dir);
                }
                catch
                {
                    continue;
                }

                foreach (var sub in subDirs)
                {
                    var name = Path.GetFileName(sub);
                    if (GeneratedFolders.Contains(name) || IgnoredFolders.Contains(name) || IsReparsePoint(sub))
                    {
                        continue;
                    }

                    stack.Push(sub);
                }
            }
        }

        #endregion
    }
}
