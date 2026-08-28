using Microsoft.Win32;

namespace IpPathQuickOpen;

internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "共享速览";
    private const string LegacyValueName = "IP路径快开";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var current = key?.GetValue(ValueName) as string;
            var legacy = key?.GetValue(LegacyValueName) as string;
            return !string.IsNullOrWhiteSpace(current) || !string.IsNullOrWhiteSpace(legacy);
        }
        catch
        {
            return false;
        }
    }

    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (enabled)
            {
                key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            key.DeleteValue(LegacyValueName, throwOnMissingValue: false);

            return true;
        }
        catch
        {
            return false;
        }
    }
}
