using System;
using System.Globalization;
using System.Linq;

namespace DreamUnrealManager.Helpers
{
    /// <summary>
    /// 语义化版本号，用于比较本机版本与 GitHub Release tag。
    ///
    /// 之前版本检查直接用字符串相等判断（githubRelease != appVersion），
    /// 只要 tag 带上 -beta/-rc 后缀、或者少写一个 v 前缀就会误报“有新版本”。
    /// 这里做真正的按段比较，且预发布版本排在同号正式版之前。
    /// </summary>
    public readonly struct SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
    {
        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }

        /// <summary>连字符之后的预发布标识，例如 beta.1；正式版为空字符串。</summary>
        public string PreRelease { get; }

        public SemanticVersion(int major, int minor, int patch, string? preRelease = null)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
            PreRelease = preRelease ?? string.Empty;
        }

        public bool IsPreRelease => PreRelease.Length > 0;

        /// <summary>
        /// 解析 v1.2.3 / 1.2.3 / v1.2.3-beta.1 / 1.2 / 1.2.3.4 等形式。
        /// 缺省的段按 0 处理；第四段（Revision）会被忽略，语义化版本只比较前三段。
        /// </summary>
        public static bool TryParse(string? text, out SemanticVersion version)
        {
            version = default;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var span = text.Trim();

            // 允许 v / V 前缀（GitHub tag 惯例），也允许 release- 之类的常见前缀被剥掉。
            if (span.Length > 0 && (span[0] == 'v' || span[0] == 'V'))
            {
                span = span[1..];
            }

            // 构建元数据（+ 之后）不参与比较，直接丢弃。
            var plusIndex = span.IndexOf('+');
            if (plusIndex >= 0)
            {
                span = span[..plusIndex];
            }

            string preRelease = string.Empty;
            var dashIndex = span.IndexOf('-');
            if (dashIndex >= 0)
            {
                preRelease = span[(dashIndex + 1)..];
                span = span[..dashIndex];
            }

            var parts = span.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return false;
            }

            var numbers = new int[3];
            for (var i = 0; i < 3; i++)
            {
                if (i >= parts.Length)
                {
                    numbers[i] = 0;
                    continue;
                }

                if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                {
                    return false;
                }

                numbers[i] = value;
            }

            version = new SemanticVersion(numbers[0], numbers[1], numbers[2], preRelease);
            return true;
        }

        public int CompareTo(SemanticVersion other)
        {
            var result = Major.CompareTo(other.Major);
            if (result != 0) return result;

            result = Minor.CompareTo(other.Minor);
            if (result != 0) return result;

            result = Patch.CompareTo(other.Patch);
            if (result != 0) return result;

            // 主版本号相同时，有预发布标识的一方更小：1.0.0-rc.1 < 1.0.0。
            if (IsPreRelease && !other.IsPreRelease) return -1;
            if (!IsPreRelease && other.IsPreRelease) return 1;

            return ComparePreRelease(PreRelease, other.PreRelease);
        }

        /// <summary>按 semver 规则逐段比较预发布标识：纯数字段按数值比，其余按序数字符串比。</summary>
        private static int ComparePreRelease(string left, string right)
        {
            if (left.Length == 0 && right.Length == 0) return 0;

            var leftParts = left.Split('.');
            var rightParts = right.Split('.');
            var count = Math.Max(leftParts.Length, rightParts.Length);

            for (var i = 0; i < count; i++)
            {
                // 段数少的一方更小：beta < beta.1。
                if (i >= leftParts.Length) return -1;
                if (i >= rightParts.Length) return 1;

                var l = leftParts[i];
                var r = rightParts[i];

                var lIsNum = int.TryParse(l, NumberStyles.None, CultureInfo.InvariantCulture, out var lNum);
                var rIsNum = int.TryParse(r, NumberStyles.None, CultureInfo.InvariantCulture, out var rNum);

                if (lIsNum && rIsNum)
                {
                    var cmp = lNum.CompareTo(rNum);
                    if (cmp != 0) return cmp;
                }
                else if (lIsNum != rIsNum)
                {
                    // 数字段永远小于非数字段。
                    return lIsNum ? -1 : 1;
                }
                else
                {
                    var cmp = string.CompareOrdinal(l, r);
                    if (cmp != 0) return Math.Sign(cmp);
                }
            }

            return 0;
        }

        public bool Equals(SemanticVersion other) => CompareTo(other) == 0;

        public override bool Equals(object? obj) => obj is SemanticVersion other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, PreRelease);

        public override string ToString() =>
            IsPreRelease ? $"{Major}.{Minor}.{Patch}-{PreRelease}" : $"{Major}.{Minor}.{Patch}";

        public static bool operator <(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) < 0;
        public static bool operator >(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) > 0;
        public static bool operator <=(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) <= 0;
        public static bool operator >=(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) >= 0;
        public static bool operator ==(SemanticVersion a, SemanticVersion b) => a.Equals(b);
        public static bool operator !=(SemanticVersion a, SemanticVersion b) => !a.Equals(b);
    }
}
