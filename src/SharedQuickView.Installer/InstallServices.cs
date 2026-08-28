using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace SharedQuickViewInstaller;

internal sealed record InstallOptions(
    string InstallDirectory,
    bool CreateDesktopShortcut,
    bool CreateStartMenuShortcut,
    bool StartWithWindows);

internal static class InstallServices
{
    public const string AppName = "共享速览";
    public const string AppExeName = "共享速览.exe";
    public const string UninstallerName = "卸载共享速览.exe";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string UninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\共享速览";

    public static string DefaultInstallDirectory
    {
        get => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppName);
    }

    public static string ReadDocument(string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException("安装包缺少协议文件。");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public static string ValidateInstallDirectory(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("请选择安装位置。");
        }

        var fullPath = Path.GetFullPath(value.Trim()).TrimEnd(Path.DirectorySeparatorChar);
        var root = Path.GetPathRoot(fullPath)?.TrimEnd(Path.DirectorySeparatorChar);
        if (string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("不能直接安装到磁盘根目录，请选择一个文件夹，例如 C:\\Program Files\\共享速览。");
        }
        return fullPath;
    }

    public static void Install(InstallOptions options, IProgress<string>? progress = null)
    {
        var installDirectory = ValidateInstallDirectory(options.InstallDirectory);
        progress?.Report("正在准备安装目录…");
        Directory.CreateDirectory(installDirectory);

        var appPath = Path.Combine(installDirectory, AppExeName);
        var uninstallerPath = Path.Combine(installDirectory, UninstallerName);
        CloseRunningApplication(appPath);

        progress?.Report("正在安装共享速览…");
        ExtractResource("Payload.共享速览.exe", appPath);
        File.Copy(Environment.ProcessPath ?? throw new InvalidOperationException("找不到安装程序。"), uninstallerPath, overwrite: true);
        ExtractResource("Docs.用户协议.txt", Path.Combine(installDirectory, "用户协议.txt"));
        ExtractResource("Docs.隐私政策.txt", Path.Combine(installDirectory, "隐私政策.txt"));

        progress?.Report("正在创建快捷方式…");
        var desktopShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk");
        if (options.CreateDesktopShortcut)
        {
            CreateShortcut(desktopShortcut, appPath, installDirectory);
        }
        else
        {
            DeleteIfExists(desktopShortcut);
        }

        var startMenuDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), AppName);
        var startMenuShortcut = Path.Combine(startMenuDirectory, AppName + ".lnk");
        if (options.CreateStartMenuShortcut)
        {
            Directory.CreateDirectory(startMenuDirectory);
            CreateShortcut(startMenuShortcut, appPath, installDirectory);
            CreateShortcut(Path.Combine(startMenuDirectory, "卸载共享速览.lnk"), uninstallerPath, installDirectory, "--uninstall");
        }
        else if (Directory.Exists(startMenuDirectory))
        {
            Directory.Delete(startMenuDirectory, recursive: true);
        }

        progress?.Report("正在写入系统安装信息…");
        SetStartup(options.StartWithWindows, appPath);
        RegisterUninstaller(installDirectory, appPath, uninstallerPath);

        var receipt = new
        {
            Product = AppName,
            Version = "2.0.2",
            Publisher = "@老李Oldlee",
            InstalledAt = DateTimeOffset.Now,
            PrivacyAndAgreementAccepted = true,
            options.CreateDesktopShortcut,
            options.CreateStartMenuShortcut,
            options.StartWithWindows
        };
        File.WriteAllText(
            Path.Combine(installDirectory, "安装信息.json"),
            JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8);
        progress?.Report("安装完成");
    }

    public static bool Uninstall(
        string installDirectory,
        bool deleteSettings,
        IProgress<string>? progress = null,
        bool removeSystemIntegration = true)
    {
        var fullPath = ValidateInstallDirectory(installDirectory);
        var expectedApp = Path.Combine(fullPath, AppExeName);
        if (!File.Exists(expectedApp))
        {
            throw new InvalidOperationException("指定目录中没有找到共享速览，已停止卸载以保护其他文件。");
        }

        progress?.Report("正在关闭共享速览…");
        CloseRunningApplication(expectedApp);
        if (removeSystemIntegration)
        {
            SetStartup(false, expectedApp);
        }

        progress?.Report("正在移除快捷方式和系统记录…");
        if (removeSystemIntegration)
        {
            DeleteIfExists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk"));
            var startMenuDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), AppName);
            if (Directory.Exists(startMenuDirectory))
            {
                Directory.Delete(startMenuDirectory, recursive: true);
            }
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            baseKey.DeleteSubKeyTree(UninstallKeyPath, throwOnMissingSubKey: false);
        }

        if (deleteSettings)
        {
            progress?.Report("正在删除个人设置…");
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            DeleteDirectoryIfExists(Path.Combine(appData, "Oldlee", AppName));
            DeleteDirectoryIfExists(Path.Combine(appData, "Oldlee", "IP路径快开"));
        }

        progress?.Report("正在移除程序文件…");
        var currentExe = Environment.ProcessPath;
        var cleanupPending = !string.IsNullOrWhiteSpace(currentExe) && IsPathInside(currentExe, fullPath);
        if (cleanupPending)
        {
            DeleteDirectoryContentsExcept(fullPath, currentExe!);
            ScheduleDeleteAtRestart(currentExe!, fullPath);
        }
        else
        {
            DeleteInstallDirectoryWithRetry(fullPath);
        }
        progress?.Report("卸载完成");
        return cleanupPending;
    }

    public static void LaunchInstalledApp(string installDirectory)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(ValidateInstallDirectory(installDirectory), AppExeName),
            UseShellExecute = true
        });
    }

    private static void ExtractResource(string resourceName, string destination)
    {
        using var input = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
                          ?? throw new InvalidOperationException($"安装包缺少资源：{resourceName}");
        using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
    }

    private static void SetStartup(bool enabled, string appPath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (enabled)
        {
            key.SetValue(AppName, $"\"{appPath}\"");
        }
        else
        {
            key.DeleteValue(AppName, throwOnMissingValue: false);
        }
        key.DeleteValue("IP路径快开", throwOnMissingValue: false);
    }

    private static void RegisterUninstaller(string installDirectory, string appPath, string uninstallerPath)
    {
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = baseKey.CreateSubKey(UninstallKeyPath, writable: true);
        key.SetValue("DisplayName", AppName);
        key.SetValue("DisplayVersion", "2.0.2");
        key.SetValue("Publisher", "老李Oldlee");
        key.SetValue("URLInfoAbout", "https://x.com/oldleeoo");
        key.SetValue("InstallLocation", installDirectory);
        key.SetValue("DisplayIcon", appPath);
        key.SetValue("UninstallString", $"\"{uninstallerPath}\" --uninstall");
        key.SetValue("QuietUninstallString", $"\"{uninstallerPath}\" --uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        var sizeKb = (int)Math.Min(int.MaxValue, Directory.EnumerateFiles(installDirectory).Sum(path => new FileInfo(path).Length) / 1024);
        key.SetValue("EstimatedSize", sizeKb, RegistryValueKind.DWord);
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory, string arguments = "")
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows 快捷方式组件不可用。");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = targetPath;
        shortcut.WorkingDirectory = workingDirectory;
        shortcut.IconLocation = targetPath + ",0";
        shortcut.Arguments = arguments;
        shortcut.Description = "共享速览 · @老李Oldlee";
        shortcut.Save();
        Marshal.FinalReleaseComObject(shortcut);
        Marshal.FinalReleaseComObject(shell);
    }

    private static void CloseRunningApplication(string expectedExecutable)
    {
        var failures = new List<string>();
        foreach (var process in Process.GetProcessesByName("共享速览"))
        {
            try
            {
                try
                {
                    var processPath = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(processPath) &&
                        !string.Equals(Path.GetFullPath(processPath), Path.GetFullPath(expectedExecutable), StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }
                catch
                {
                    // 无法读取进程路径时仍按产品进程名关闭，避免托盘进程阻止卸载。
                }

                if (process.CloseMainWindow() && process.WaitForExit(1500))
                {
                    continue;
                }

                process.Kill(entireProcessTree: true);
                if (!process.WaitForExit(5000))
                {
                    failures.Add($"PID {process.Id}");
                }
            }
            catch (Exception exception)
            {
                failures.Add($"PID {process.Id}：{exception.Message}");
            }
            finally { process.Dispose(); }
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException("无法自动关闭共享速览：" + string.Join("；", failures));
        }
    }

    private static void DeleteInstallDirectoryWithRetry(string path)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                lastError = exception;
                Thread.Sleep(250);
            }
        }

        throw new IOException("程序已经关闭，但安装目录仍被 Windows 或安全软件占用，请稍后重试。", lastError);
    }

    private static bool IsPathInside(string candidatePath, string directory)
    {
        var fullCandidate = Path.GetFullPath(candidatePath);
        var fullDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        return fullCandidate.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static void DeleteDirectoryContentsExcept(string directory, string preservedFile)
    {
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            if (string.Equals(Path.GetFullPath(file), Path.GetFullPath(preservedFile), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            File.SetAttributes(file, FileAttributes.Normal);
            File.Delete(file);
        }

        foreach (var childDirectory in Directory.EnumerateDirectories(directory))
        {
            if (IsPathInside(preservedFile, childDirectory))
            {
                DeleteDirectoryContentsExcept(childDirectory, preservedFile);
                if (!Directory.EnumerateFileSystemEntries(childDirectory).Any())
                {
                    Directory.Delete(childDirectory);
                }
            }
            else
            {
                Directory.Delete(childDirectory, recursive: true);
            }
        }
    }

    private static void ScheduleDeleteAtRestart(string executablePath, string installDirectory)
    {
        if (!MoveFileEx(executablePath, null, MoveFileDelayUntilReboot))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法登记卸载器的重启后清理任务。");
        }
        if (!MoveFileEx(installDirectory, null, MoveFileDelayUntilReboot))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法登记安装目录的重启后清理任务。");
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    private const int MoveFileDelayUntilReboot = 0x4;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileEx(string existingFileName, string? newFileName, int flags);
}
