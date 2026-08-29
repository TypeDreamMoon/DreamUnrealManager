using System.Diagnostics;
using System.Text;

namespace DreamUnrealManager.Helpers
{
    public static class ProcessHelper
    {
        /// <summary>
        /// 重定向的输出流不带 BOM，解码方式必须由我们指定，否则 .NET 会按控制台代码页解码
        /// （中文系统上是 GBK），而 UE5 的 UnrealBuildTool / AutomationTool 输出的是 UTF-8，
        /// 结果就是构建日志里的中文全是乱码。
        ///
        /// 用 UTF8Encoding(throwOnInvalidBytes: false)：遇到非 UTF-8 字节时替换为 U+FFFD 而不是抛异常，
        /// 保证少数按 OEM 代码页输出的 .bat 行不会中断整个日志读取。
        /// </summary>
        private static readonly UTF8Encoding Utf8Lenient = new(encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: false);

        /// <summary>为已开启重定向的 ProcessStartInfo 指定 UTF-8 输出解码。</summary>
        public static ProcessStartInfo UseUtf8Output(this ProcessStartInfo startInfo)
        {
            if (startInfo.RedirectStandardOutput)
            {
                startInfo.StandardOutputEncoding = Utf8Lenient;
            }

            if (startInfo.RedirectStandardError)
            {
                startInfo.StandardErrorEncoding = Utf8Lenient;
            }

            return startInfo;
        }
    }
}
