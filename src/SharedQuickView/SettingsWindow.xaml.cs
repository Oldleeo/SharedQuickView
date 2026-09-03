using System.Windows;
using System.Windows.Input;

namespace IpPathQuickOpen;

public partial class SettingsWindow : Window
{
    private readonly MainWindow _mainWindow;
    private readonly AppSettings _settings;

    internal SettingsWindow(MainWindow mainWindow, AppSettings settings)
    {
        InitializeComponent();
        _mainWindow = mainWindow;
        _settings = settings;
        Owner = mainWindow;

        AutoStartCheck.IsChecked = StartupManager.IsEnabled();
        AlwaysTopCheck.IsChecked = settings.AlwaysOnTop;
        StartCompactCheck.IsChecked = settings.StartCompact;
        AutoClipboardCheck.IsChecked = settings.AutoOpenClipboard;
        AutoSameIpCheck.IsChecked = settings.AutoOpenSameTargetIp;
        RecognizeWebUrlsCheck.IsChecked = settings.RecognizeWebUrls;
        RecognizeLocalPathsCheck.IsChecked = settings.RecognizeLocalPaths;
        ClearAfterOpenCheck.IsChecked = settings.ClearAfterOpen;
        MinimizeAfterOpenCheck.IsChecked = settings.MinimizeAfterOpen;
        ConfirmLargeBatchCheck.IsChecked = settings.ConfirmLargeBatch;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var wantsAutoStart = AutoStartCheck.IsChecked == true;
        if (!StartupManager.SetEnabled(wantsAutoStart))
        {
            SaveHint.Text = "开机启动设置失败，请检查系统权限";
            SaveHint.Foreground = System.Windows.Media.Brushes.IndianRed;
            return;
        }

        _settings.AutoStart = wantsAutoStart;
        _settings.AlwaysOnTop = AlwaysTopCheck.IsChecked == true;
        _settings.StartCompact = StartCompactCheck.IsChecked == true;
        _settings.AutoOpenClipboard = AutoClipboardCheck.IsChecked == true;
        _settings.AutoOpenSameTargetIp = AutoSameIpCheck.IsChecked == true;
        _settings.RecognizeWebUrls = RecognizeWebUrlsCheck.IsChecked == true;
        _settings.RecognizeLocalPaths = RecognizeLocalPathsCheck.IsChecked == true;
        _settings.ClearAfterOpen = ClearAfterOpenCheck.IsChecked == true;
        _settings.MinimizeAfterOpen = MinimizeAfterOpenCheck.IsChecked == true;
        _settings.ConfirmLargeBatch = ConfirmLargeBatchCheck.IsChecked == true;
        SettingsStore.Save(_settings);
        _mainWindow.ApplySettings();
        DialogResult = true;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void CopyrightLink_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ExternalLinks.OpenOldleeProfile();
        }
        catch
        {
            System.Windows.MessageBox.Show(
                ExternalLinks.OldleeProfile,
                "无法打开浏览器，请复制此网址",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}
