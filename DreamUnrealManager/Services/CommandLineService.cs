using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DreamUnrealManager.Services
{
    /// <summary>解析命令行得到的意图。</summary>
    public sealed class CommandLineRequest
    {
        /// <summary>要打开的 .uproject 绝对路径；没有则为 null。</summary>
        public string? ProjectPath { get; init; }

        /// <summary>是否要求直接启动编辑器（而不是仅仅在列表里选中）。</summary>
        public bool LaunchImmediately { get; init; }

        public bool HasWork => !string.IsNullOrEmpty(ProjectPath);
    }

    /// <summary>
    /// 命令行解析。
    ///
    /// 此前程序完全不看命令行参数，既没法把它设成 .uproject 的打开方式，
    /// 也没法从脚本里 dum.exe MyProject.uproject 直接拉起。
    ///
    /// 解析逻辑保持纯函数（不碰 UI、不碰全局状态），便于单测覆盖引号、
    /// 相对路径、扩展名校验这些容易出错的边角。
    /// </summary>
    public static class CommandLineService
    {
        private const string UProjectExtension = ".uproject";

        /// <summary>
        /// 解析参数。约定：
        ///   dum.exe &lt;path.uproject&gt;            在列表中定位该项目
        ///   dum.exe --launch &lt;path.uproject&gt;   直接启动编辑器
        /// 无法识别的参数一律忽略——命令行来源不可控，不能因为多了个参数就拒绝启动。
        /// </summary>
        /// <param name="args">不含 exe 自身的参数数组。</param>
        /// <param name="fileExists">文件存在性判断，测试可注入。</param>
        public static CommandLineRequest Parse(IReadOnlyList<string>? args, Func<string, bool>? fileExists = null)
        {
            fileExists ??= File.Exists;

            if (args == null || args.Count == 0)
            {
                return new CommandLineRequest();
            }

            var launch = false;
            string? projectPath = null;

            foreach (var raw in args)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                // 外部调用方（explorer、脚本）常把路径整段带引号传进来。
                var arg = raw.Trim().Trim('"');

                if (arg.Equals("--launch", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("-l", StringComparison.OrdinalIgnoreCase))
                {
                    launch = true;
                    continue;
                }

                if (arg.StartsWith('-'))
                {
                    // 未知开关：忽略而不是报错。
                    continue;
                }

                if (projectPath != null)
                {
                    continue;
                }

                if (!arg.EndsWith(UProjectExtension, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string full;
                try
                {
                    full = Path.GetFullPath(arg);
                }
                catch
                {
                    // 路径里有非法字符等，直接跳过。
                    continue;
                }

                if (fileExists(full))
                {
                    projectPath = full;
                }
            }

            return new CommandLineRequest
            {
                ProjectPath = projectPath,
                LaunchImmediately = launch
            };
        }

        /// <summary>
        /// 从一整行命令行字符串里切出参数（AppInstance 重定向过来的是原始字符串）。
        /// 只处理双引号包裹，足够覆盖 explorer 与常见 shell 的传参形式。
        /// </summary>
        public static IReadOnlyList<string> SplitArguments(string? commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine))
            {
                return Array.Empty<string>();
            }

            var result = new List<string>();
            var current = new System.Text.StringBuilder();
            var inQuotes = false;

            foreach (var c in commandLine)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (char.IsWhiteSpace(c) && !inQuotes)
                {
                    if (current.Length > 0)
                    {
                        result.Add(current.ToString());
                        current.Clear();
                    }

                    continue;
                }

                current.Append(c);
            }

            if (current.Length > 0)
            {
                result.Add(current.ToString());
            }

            return result;
        }

        /// <summary>取本进程的命令行参数（已剔除 exe 自身）。</summary>
        public static IReadOnlyList<string> GetCurrentArguments()
        {
            var all = Environment.GetCommandLineArgs();
            return all.Length <= 1 ? Array.Empty<string>() : all.Skip(1).ToArray();
        }
    }
}
