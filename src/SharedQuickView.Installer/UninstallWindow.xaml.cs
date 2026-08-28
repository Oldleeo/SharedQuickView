using System.Windows;
using System.Windows.Input;

namespace SharedQuickViewInstaller;

public partial class UninstallWindow : Window
{
    private readonly string _installDirectory;
    private bool _working;
    private bool _completed;

    public UninstallWindow(string installDirectory)
    {
        InitializeComponent();
        _installDirectory = installDirectory;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private async void UninstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_completed)
        {
            CloseAndShutdown();
            return;
        }

        await PerformUninstallAsync(DeleteSettingsCheck.IsChecked == true, removeSystemIntegration: true);
    }

    internal async Task<bool> RunForQaAsync(bool deleteSettings, bool removeSystemIntegration = true)
    {
        DeleteSettingsCheck.IsChecked = deleteSettings;
        var cleanupPending = await PerformUninstallAsync(deleteSettings, removeSystemIntegration);
        if (!_completed)
        {
            throw new InvalidOperationException(DescriptionText.Text);
        }
        return cleanupPending;
    }

    private async Task<bool> PerformUninstallAsync(bool deleteSettings, bool removeSystemIntegration)
    {
        _working = true;
        CancelButton.IsEnabled = false;
        UninstallButton.IsEnabled = false;
        DeleteSettingsCheck.IsEnabled = false;
        UninstallProgress.Visibility = Visibility.Visible;
        var progress = new Progress<string>(message => StatusText.Text = message);
        try
        {
            var cleanupPending = await Task.Run(() => InstallServices.Uninstall(
                _installDirectory,
                deleteSettings,
                progress,
                removeSystemIntegration));
            _completed = true;
            TitleText.Text = "共享速览已卸载";
            var description = deleteSettings
                ? "程序文件与个人设置均已移除。"
                : "程序文件已移除，个人设置已保留，重新安装后可继续使用。";
            DescriptionText.Text = cleanupPending
                ? description + " 卸载器自身将在下次重启 Windows 时自动清理。"
                : description;
            UninstallProgress.IsIndeterminate = false;
            UninstallProgress.Value = 100;
            StatusText.Text = cleanupPending ? "卸载完成 · 重启后清理卸载器" : "卸载完成";
            UninstallButton.Content = "完成";
            UninstallButton.IsEnabled = true;
            CancelButton.Visibility = Visibility.Collapsed;
            return cleanupPending;
        }
        catch (Exception exception)
        {
            TitleText.Text = "卸载没有完成";
            DescriptionText.Text = exception.Message;
            UninstallProgress.Visibility = Visibility.Collapsed;
            StatusText.Text = "未删除其他文件";
            UninstallButton.Content = "重试";
            UninstallButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
            DeleteSettingsCheck.IsEnabled = true;
            return false;
        }
        finally
        {
            _working = false;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_working) return;
        CloseAndShutdown();
    }

    private void CloseAndShutdown()
    {
        Close();
        System.Windows.Application.Current.Shutdown();
    }
}
