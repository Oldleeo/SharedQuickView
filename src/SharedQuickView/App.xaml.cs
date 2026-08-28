using System.Windows;
using System.IO;

namespace IpPathQuickOpen;

public partial class App : System.Windows.Application
{
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var qaArgument = e.Args.FirstOrDefault(arg => arg.StartsWith("--qa-", StringComparison.OrdinalIgnoreCase));
        _mainWindow = new MainWindow(qaArgument is not null);
        MainWindow = _mainWindow;
        _mainWindow.Show();

        if (qaArgument is not null)
        {
            RunQaCapture(qaArgument);
        }
    }

    private async void RunQaCapture(string argument)
    {
        if (_mainWindow is null)
        {
            return;
        }

        await Task.Delay(650);
        var separator = argument.IndexOf('=');
        if (separator < 0)
        {
            Shutdown(2);
            return;
        }

        var mode = argument[5..separator].ToLowerInvariant();
        var outputPath = argument[(separator + 1)..].Trim('"');
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");

        if (mode == "compact")
        {
            _mainWindow.EnterCompactModeForQa();
            await Task.Delay(350);
            QaCapture.Save(_mainWindow, outputPath);
        }
        else if (mode is "demo-input" or "demo-preview")
        {
            _mainWindow.PrepareDemoForQa(converted: mode == "demo-preview");
            await Task.Delay(250);
            QaCapture.Save(_mainWindow, outputPath);
        }
        else if (mode == "settings")
        {
            var settingsWindow = new SettingsWindow(_mainWindow, _mainWindow.SettingsData);
            settingsWindow.Height = 820;
            settingsWindow.Show();
            await Task.Delay(350);
            QaCapture.Save(settingsWindow, outputPath);
            settingsWindow.Close();
        }
        else
        {
            QaCapture.Save(_mainWindow, outputPath);
        }

        Shutdown();
    }
}
