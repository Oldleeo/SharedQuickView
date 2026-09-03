using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace IpPathQuickOpen;

internal sealed record PathTransform(string SourceHost, string ResultPath);

internal enum OpenTargetKind
{
    SharedPath,
    WebUrl,
    LocalPath
}

internal sealed record OpenTarget(OpenTargetKind Kind, string Value, string? SourceHost = null);

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

            // 先排除所有带协议的网址，避免把 https://IP/path 误当成 UNC。
            line = AnyUrlRegex().Replace(line, " ").Trim();

            // 文件夹名称不能包含正斜杠，因此也兼容独立复制的 //IP/share/path。
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

    public static IReadOnlyList<OpenTarget> ParseOpenTargets(
        string? input,
        string targetIp,
        bool recognizeWebUrls,
        bool recognizeLocalPaths)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Array.Empty<OpenTarget>();
        }

        var results = new List<OpenTarget>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var transform in ParseTransforms(input, targetIp))
        {
            AddTarget(new OpenTarget(OpenTargetKind.SharedPath, transform.ResultPath, transform.SourceHost));
        }

        var normalized = NormalizeInput(input);
        if (recognizeWebUrls)
        {
            foreach (Match match in WebUrlRegex().Matches(normalized))
            {
                var value = CleanDirectTarget(match.Value);
                if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
                    && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    AddTarget(new OpenTarget(OpenTargetKind.WebUrl, value));
                }
            }
        }

        if (recognizeLocalPaths)
        {
            foreach (var rawLine in normalized.Split('\n'))
            {
                // 网址中偶尔也会出现 C:/ 字样，本地路径识别前先移除网址。
                var line = AnyUrlRegex().Replace(rawLine, " ");
                foreach (Match match in LocalPathRegex().Matches(line))
                {
                    var value = CleanDirectTarget(match.Value).Replace('/', '\\');
                    if (IsFullyQualifiedLocalPath(value))
                    {
                        AddTarget(new OpenTarget(OpenTargetKind.LocalPath, value));
                    }
                }
            }
        }

        return results;

        void AddTarget(OpenTarget target)
        {
            if (seen.Add(target.Value))
            {
                results.Add(target);
            }
        }
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

    private static string CleanDirectTarget(string value)
    {
        value = value.Trim();
        while (value.Length > 0 && value[^1] is '"' or '\'' or '”' or '’' or '》' or '】'
                   or ')' or '）' or ',' or '，' or ';' or '；' or '!' or '！' or '。')
        {
            value = value[..^1].TrimEnd();
        }

        return value;
    }

    private static bool IsFullyQualifiedLocalPath(string value)
    {
        return value.Length >= 3
               && char.IsAsciiLetter(value[0])
               && value[1] == ':'
               && value[2] == '\\';
    }

    [GeneratedRegex(@"\\{2,}(?<host>[^\\\s]+)(?<tail>\\.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex UncPathRegex();

    [GeneratedRegex(@"\\{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex RepeatedSlashRegex();

    [GeneratedRegex(@"(?i)\b[a-z][a-z0-9+.-]{1,31}://[^\s<>“”‘’""']+", RegexOptions.CultureInvariant)]
    private static partial Regex AnyUrlRegex();

    [GeneratedRegex(@"(?i)\bhttps?://[^\s<>“”‘’""']+", RegexOptions.CultureInvariant)]
    private static partial Regex WebUrlRegex();

    [GeneratedRegex(@"(?<![a-zA-Z0-9])[a-zA-Z]:[\\/][^\r\n]*", RegexOptions.CultureInvariant)]
    private static partial Regex LocalPathRegex();
}
