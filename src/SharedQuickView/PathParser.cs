using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace IpPathQuickOpen;

internal sealed record PathTransform(string SourceHost, string ResultPath);

internal static partial class PathParser
{
    public static bool IsValidTargetIp(string? value)
    {
        return IPAddress.TryParse(value?.Trim(), out var address)
               && address.AddressFamily == AddressFamily.InterNetwork;
    }

    public static IReadOnlyList<string> ParseAndReplace(string? input, string targetIp)
    {
        return ParseTransforms(input, targetIp).Select(item => item.ResultPath).ToArray();
    }

    public static IReadOnlyList<PathTransform> ParseTransforms(string? input, string targetIp)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Array.Empty<PathTransform>();
        }

        var normalized = NormalizeInput(input);
        var results = new List<PathTransform>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in normalized.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line == "\\")
            {
                continue;
            }

            // 文件夹名称不能包含正斜杠，因此也兼容从网页复制出的 //IP/share/path。
            line = line.Replace('/', '\\');
            var match = UncPathRegex().Match(line);
            if (!match.Success)
            {
                continue;
            }

            var tail = CleanTail(match.Groups["tail"].Value);
            if (tail.Length < 2)
            {
                continue;
            }

            var replaced = $@"\\{targetIp}{tail}";
            if (seen.Add(replaced))
            {
                results.Add(new PathTransform(match.Groups["host"].Value, replaced));
            }
        }

        return results;
    }

    private static string NormalizeInput(string input)
    {
        var value = input
            .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase);

        value = WebUtility.HtmlDecode(value)
            .Replace('\u00A0', ' ')
            .Replace('＼', '\\')
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');

        return value;
    }

    private static string CleanTail(string tail)
    {
        tail = tail.Trim();

        // 清理由聊天软件或表格复制时包在路径外侧的引号。
        while (tail.Length > 0 && tail[^1] is '"' or '\'' or '”' or '’')
        {
            tail = tail[..^1].TrimEnd();
        }

        // 连续分隔符统一为一个，但保留路径开头的单个分隔符。
        tail = RepeatedSlashRegex().Replace(tail, "\\");
        return tail;
    }

    [GeneratedRegex(@"\\{2,}(?<host>[^\\\s]+)(?<tail>\\.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex UncPathRegex();

    [GeneratedRegex(@"\\{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex RepeatedSlashRegex();
}
