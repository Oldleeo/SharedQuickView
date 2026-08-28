using System.Diagnostics;

namespace IpPathQuickOpen;

internal static class ExternalLinks
{
    public const string OldleeProfile = "https://x.com/oldleeoo";

    public static void OpenOldleeProfile()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = OldleeProfile,
            UseShellExecute = true
        });
    }
}
