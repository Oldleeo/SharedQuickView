using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.IO;
using WinForms = System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using MediaBrushes = System.Windows.Media.Brushes;

namespace SharedQuickViewInstaller;

public partial class InstallerWindow : Window
{
    private int _step;
    private bool _installing;
    private string? _installedDirectory;

    public InstallerWindow()
    {
        InitializeComponent();
        InstallPathBox.Text = InstallServices.DefaultInstallDirectory;
        AgreementText.Text = InstallServices.ReadDocument("Docs.用户协议.txt");
        PrivacyText.Text = InstallServices.ReadDocument("Docs.隐私政策.txt");
        UpdateStep();
    }

    internal void SetQaStep(int step)
    {
        _step = Math.Clamp(step, 0, 3);
        UpdateStep();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var configuredPath = InstallPathBox.Text.Trim();
        var parentPath = Path.GetDirectoryName(configuredPath);
        var initialPath = Directory.Exists(configuredPath)
            ? configuredPath
            : Directory.Exists(parentPath)
                ? parentPath!
                : Path.GetPathRoot(configuredPath) ?? string.Empty;
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "选择共享速览的安装文件夹",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = initialPath
        };
        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            var selectedPath = dialog.SelectedPath ?? string.Empty;
            InstallPathBox.Text = string.Equals(
                Path.GetFileName(selectedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                InstallServices.AppName,
                StringComparison.OrdinalIgnoreCase)
                ? selectedPath
                : Path.Combine(selectedPath, InstallServices.AppName);
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_step > 0 && !_installing)
        {
            _step--;
            UpdateStep();
        }
    }

    private async void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_step == 0)
        {
            _step = 1;
            UpdateStep();
            return;
        }

        if (_step == 1)
        {
            try
            {
                InstallPathBox.Text = InstallServices.ValidateInstallDirectory(InstallPathBox.Text);
            }
            catch (Exception exception)
            {
                System.Windows.MessageBox.Show(exception.Message, "安装位置无效", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _step = 2;
            UpdateStep();
            return;
        }

        if (_step == 2)
        {
            if (AcceptCheck.IsChecked != true)
            {
                System.Windows.MessageBox.Show("请先阅读并勾选同意用户协议和隐私政策。", "需要同意", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            await InstallAsync();
            return;
        }

        if (_step == 3 && !_installing)
        {
            if (_installedDirectory is not null && LaunchAfterCheck.IsChecked == true)
            {
                InstallServices.LaunchInstalledApp(_installedDirectory);
            }
            CloseAndShutdown();
        }
    }

    private async Task InstallAsync()
    {
        _installing = true;
        _step = 3;
        UpdateStep();
        var options = new InstallOptions(
            InstallPathBox.Text,
            DesktopShortcutCheck.IsChecked == true,
            StartMenuCheck.IsChecked == true,
            AutoStartCheck.IsChecked == true);
        var progress = new Progress<string>(message => ProgressStatus.Text = message);

        try
        {
            await Task.Run(() => InstallServices.Install(options, progress));
            _installedDirectory = InstallServices.ValidateInstallDirectory(options.InstallDirectory);
            CompleteIcon.Text = "✓";
            CompleteIcon.Foreground = new SolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString("#26A65B"));
            CompleteTitle.Text = "安装完成";
            ProgressStatus.Text = "共享速览已准备就绪";
            InstallProgress.IsIndeterminate = false;
            InstallProgress.Value = 100;
            LaunchAfterCheck.Visibility = Visibility.Visible;
            NextButton.Content = "完成";
            NextButton.IsEnabled = true;
        }
        catch (Exception exception)
        {
            CompleteIcon.Text = "!";
            CompleteIcon.Foreground = MediaBrushes.IndianRed;
            CompleteTitle.Text = "安装没有完成";
            ProgressStatus.Text = exception.Message;
            InstallProgress.Visibility = Visibility.Collapsed;
            NextButton.Content = "关闭";
            NextButton.IsEnabled = true;
            LaunchAfterCheck.Visibility = Visibility.Collapsed;
        }
        finally
        {
            _installing = false;
        }
    }

    private void AcceptCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_step == 2) NextButton.IsEnabled = AcceptCheck.IsChecked == true;
    }

    private void UpdateStep()
    {
        WelcomePage.Visibility = _step == 0 ? Visibility.Visible : Visibility.Collapsed;
        OptionsPage.Visibility = _step == 1 ? Visibility.Visible : Visibility.Collapsed;
        PrivacyPage.Visibility = _step == 2 ? Visibility.Visible : Visibility.Collapsed;
        CompletePage.Visibility = _step == 3 ? Visibility.Visible : Visibility.Collapsed;

        var titles = new[] { "欢迎安装", "选择安装位置与选项", "用户协议与隐私政策", "安装共享速览" };
        PageTitle.Text = titles[_step];
        BackButton.Visibility = _step is 1 or 2 ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Content = _step switch { 0 => "开始", 1 => "继续", 2 => "同意并安装", _ => "安装中…" };
        NextButton.IsEnabled = _step != 2 || AcceptCheck.IsChecked == true;

        var active = new SolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString("#2563EB"));
        var inactive = new SolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString("#8B96A8"));
        var labels = new[] { Step1Label, Step2Label, Step3Label, Step4Label };
        for (var index = 0; index < labels.Length; index++)
        {
            labels[index].Foreground = index == _step ? active : inactive;
            labels[index].FontWeight = index == _step ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_installing)
        {
            System.Windows.MessageBox.Show("安装正在进行，请稍候。", "共享速览", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        CloseAndShutdown();
    }

    private void CloseAndShutdown()
    {
        Close();
        System.Windows.Application.Current.Shutdown();
    }
}
