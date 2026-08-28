using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;

namespace SharedQuickViewInstaller;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var qaArgument = e.Args.FirstOrDefault(arg => arg.StartsWith("--qa-", StringComparison.OrdinalIgnoreCase));
        if (e.Args.Length >= 3 && e.Args[0].Equals("--qa-install-cycle", StringComparison.OrdinalIgnoreCase))
        {
            RunInstallCycle(e.Args[1], e.Args[2]);
            return;
        }
        if (e.Args.Length >= 3 && e.Args[0].Equals("--qa-uninstall-ui-cycle", StringComparison.OrdinalIgnoreCase))
        {
            RunUninstallUiCycle(e.Args[1], e.Args[2]);
            return;
        }
        if (e.Args.Length >= 2 && e.Args[0].Equals("--qa-uninstall-direct", StringComparison.OrdinalIgnoreCase))
        {
            RunDirectUninstallQa(e.Args[1]);
            return;
        }
        if (qaArgument is not null)
        {
            RunQaCapture(qaArgument);
            return;
        }

        Window window;
        if (e.Args.Length > 0 && e.Args[0].Equals("--uninstall", StringComparison.OrdinalIgnoreCase))
        {
            window = new UninstallWindow(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        }
        else if (e.Args.Length >= 2 && e.Args[0].Equals("--uninstall-run", StringComparison.OrdinalIgnoreCase))
        {
            window = new UninstallWindow(e.Args[1]);
        }
        else
        {
            window = new InstallerWindow();
        }

        MainWindow = window;
        window.Show();
    }

    private async void RunInstallCycle(string installDirectory, string resultPath)
    {
        try
        {
            var options = new InstallOptions(installDirectory, false, false, false);
            await Task.Run(() => InstallServices.Install(options));
            var appExists = File.Exists(Path.Combine(installDirectory, InstallServices.AppExeName));
            var uninstallerExists = File.Exists(Path.Combine(installDirectory, InstallServices.UninstallerName));
            if (!appExists || !uninstallerExists)
            {
                throw new InvalidOperationException("安装后缺少程序或卸载器。");
            }

            using var installedProcess = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(installDirectory, InstallServices.AppExeName),
                UseShellExecute = true
            }) ?? throw new InvalidOperationException("安装后的程序无法启动。");
            await Task.Delay(2200);
            if (installedProcess.HasExited)
            {
                throw new InvalidOperationException("安装后的程序启动后意外退出。");
            }

            await Task.Run(() => InstallServices.Uninstall(installDirectory, deleteSettings: false));
            if (!installedProcess.HasExited)
            {
                throw new InvalidOperationException("卸载器未能自动关闭正在运行的共享速览。");
            }
            if (Directory.Exists(installDirectory))
            {
                throw new InvalidOperationException("卸载后安装目录仍存在。");
            }
            File.WriteAllText(resultPath, "PASS: 安装、启动、自动关闭程序、卸载登记与完整移除均通过。", Encoding.UTF8);
            Shutdown(0);
        }
        catch (Exception exception)
        {
            File.WriteAllText(resultPath, "FAIL: " + exception, Encoding.UTF8);
            Shutdown(1);
        }
    }

    private async void RunQaCapture(string argument)
    {
        var separator = argument.IndexOf('=');
        if (separator < 0) { Shutdown(2); return; }
        var mode = argument[5..separator].ToLowerInvariant();
        var output = argument[(separator + 1)..].Trim('"');
        Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");

        Window window;
        if (mode == "uninstall")
        {
            window = new UninstallWindow(Path.Combine(Path.GetTempPath(), "共享速览-QA"));
        }
        else
        {
            var installer = new InstallerWindow();
            installer.SetQaStep(mode switch { "options" => 1, "privacy" => 2, "complete" => 3, _ => 0 });
            window = installer;
        }
        MainWindow = window;
        window.Show();
        await Task.Delay(650);
        QaCapture.Save(window, output);
        window.Close();
        Shutdown();
    }

    private async void RunUninstallUiCycle(string installDirectory, string resultPath)
    {
        try
        {
            await Task.Run(() => InstallServices.Install(new InstallOptions(installDirectory, false, false, false)));
            using var installedProcess = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(installDirectory, InstallServices.AppExeName),
                UseShellExecute = true
            }) ?? throw new InvalidOperationException("安装后的程序无法启动。");
            await Task.Delay(2200);

            var window = new UninstallWindow(installDirectory);
            MainWindow = window;
            window.Show();
            await window.RunForQaAsync(deleteSettings: false);

            if (!installedProcess.HasExited || Directory.Exists(installDirectory))
            {
                throw new InvalidOperationException("卸载界面未能关闭程序或移除安装目录。");
            }
            File.WriteAllText(resultPath, "PASS: 卸载界面后台线程、自动关闭程序和目录移除均通过。", Encoding.UTF8);
            window.Close();
            Shutdown(0);
        }
        catch (Exception exception)
        {
            File.WriteAllText(resultPath, "FAIL: " + exception, Encoding.UTF8);
            Shutdown(1);
        }
    }

    private async void RunDirectUninstallQa(string resultPath)
    {
        try
        {
            var installDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            var window = new UninstallWindow(installDirectory);
            MainWindow = window;
            window.Show();
            var cleanupPending = await window.RunForQaAsync(deleteSettings: false, removeSystemIntegration: false);
            if (!cleanupPending || File.Exists(Path.Combine(installDirectory, InstallServices.AppExeName)))
            {
                throw new InvalidOperationException("直运行卸载器未正确清理主程序或登记自身清理。");
            }
            File.WriteAllText(resultPath, "PASS: 卸载器未复制到临时目录，主程序已关闭并删除，自身已登记为重启后清理。", Encoding.UTF8);
            window.Close();
            Shutdown(0);
        }
        catch (Exception exception)
        {
            File.WriteAllText(resultPath, "FAIL: " + exception, Encoding.UTF8);
            Shutdown(1);
        }
    }
}
