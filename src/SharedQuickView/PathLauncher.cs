using System.IO;
using System.Runtime.InteropServices;

namespace IpPathQuickOpen;

internal static class PathLauncher
{
    public static void Open(string uncPath)
    {
        if (string.IsNullOrWhiteSpace(uncPath) || !uncPath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            throw new ArgumentException("不是有效的 UNC 共享路径。", nameof(uncPath));
        }

        // 直接对完整 UNC 目录执行 Shell 的“打开”动作。
        // SHOpenFolderAndSelectItems 在 itemCount=0 时会打开父目录并选中末级项目，
        // 因此不能用于“进入最后一级文件夹”的需求。
        var result = ShellExecute(IntPtr.Zero, "open", uncPath, null, null, ShowNormal);
        var resultCode = result.ToInt64();
        if (resultCode <= 32)
        {
            throw new IOException($"Windows 无法打开这个共享目录（Shell 错误 {resultCode}）：{uncPath}");
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
