using System.Text.Json;
using System.IO;

namespace IpPathQuickOpen;

internal sealed class AppSettings
{
    public string TargetIp { get; set; } = "192.168.1.100";
    public bool AlwaysOnTop { get; set; } = true;
    public bool CompactMode { get; set; }
    public bool AutoStart { get; set; }
    public bool StartCompact { get; set; }
    public bool ClearAfterOpen { get; set; } = true;
    public bool ConfirmLargeBatch { get; set; } = true;
    public bool MinimizeAfterOpen { get; set; }
    public bool AutoOpenClipboard { get; set; }
    public bool AutoOpenSameTargetIp { get; set; }
    public bool RecognizeWebUrls { get; set; }
    public bool RecognizeLocalPaths { get; set; }
    public int Left { get; set; } = -1;
    public int Top { get; set; } = -1;
}

internal static class SettingsStore
{
    private static readonly string FolderPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Oldlee",
        "共享速览");

    private static readonly string FilePath = Path.Combine(FolderPath, "settings.json");
    private static readonly string LegacyFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Oldlee",
        "IP路径快开",
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath) && !File.Exists(LegacyFilePath))
            {
                return new AppSettings();
            }

            var sourcePath = File.Exists(FilePath) ? FilePath : LegacyFilePath;
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(sourcePath))
                           ?? new AppSettings();
            if (sourcePath == LegacyFilePath)
            {
                Save(settings);
            }
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(FolderPath);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
        }
        catch
        {
            // 设置保存失败不影响本次路径打开。
        }
    }
}
