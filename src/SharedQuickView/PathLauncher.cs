using System.IO;
using System.Runtime.InteropServices;

namespace IpPathQuickOpen;

internal static class PathLauncher
{
    public static void Open(OpenTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        switch (target.Kind)
        {
            case OpenTargetKind.SharedPath:
                Open(target.Value);
                return;
            case OpenTargetKind.WebUrl:
                if (!Uri.TryCreate(target.Value, UriKind.Absolute, out var uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    throw new ArgumentException("不是有效的 HTTP/HTTPS 网址。", nameof(target));
                }
                break;
            case OpenTargetKind.LocalPath:
                if (target.Value.Length < 3
                    || !char.IsAsciiLetter(target.Value[0])
                    || target.Value[1] != ':'
                    || target.Value[2] != '\\')
                {
                    throw new ArgumentException("不是有效的本地磁盘路径。", nameof(target));
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(target));
        }

        ShellOpen(target.Value, target.Kind == OpenTargetKind.WebUrl ? "网址" : "本地路径");
    }

    public static void Open(string uncPath)
    {
        if (string.IsNullOrWhiteSpace(uncPath) || !uncPath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            throw new ArgumentException("不是有效的 UNC 共享路径。", nameof(uncPath));
        }

        // 直接对完整 UNC 目录执行 Shell 的“打开”动作。
        // SHOpenFolderAndSelectItems 在 itemCount=0 时会打开父目录并选中末级项目，
        // 因此不能用于“进入最后一级文件夹”的需求。
        ShellOpen(uncPath, "共享目录");
    }

    private static void ShellOpen(string target, string targetType)
    {
        var result = ShellExecute(IntPtr.Zero, "open", target, null, null, ShowNormal);
        var resultCode = result.ToInt64();
        if (resultCode <= 32)
        {
            throw new IOException($"Windows 无法打开这个{targetType}（Shell 错误 {resultCode}）：{target}");
        }
    }

    private const int ShowNormal = 1;

    [DllImport("shell32.dll", EntryPoint = "ShellExecuteW", CharSet = CharSet.Unicode)]
    private static extern IntPtr ShellExecute(
        IntPtr windowHandle,
        string operation,
        string file,
        string? parameters,
        string? directory,
        int showCommand);
}
