using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WinForms = System.Windows.Forms;

namespace IpPathQuickOpen;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly WinForms.NotifyIcon _trayIcon;
    private bool _reallyExit;
    private bool _compact;
    private readonly bool _qaMode;
    private HwndSource? _windowSource;
    private IntPtr _windowHandle;
    private bool _processingClipboardUpdate;
    private const int WmClipboardUpdate = 0x031D;

    public MainWindow(bool qaMode = false)
    {
        InitializeComponent();
        _qaMode = qaMode;
        _settings = qaMode ? new AppSettings() : SettingsStore.Load();
        IpBox.Text = _settings.TargetIp;
        Topmost = _settings.AlwaysOnTop;

        _trayIcon = BuildTrayIcon();
        RestoreLocation();
        SourceInitialized += (_, _) =>
        {
            if (!_qaMode)
            {
                StartClipboardListener();
            }
        };
        Loaded += (_, _) =>
        {
            if (_settings.StartCompact || _settings.CompactMode)
            {
                EnterCompactMode();
            }
            else
            {
                PathsBox.Focus();
            }
        };
        Closing += MainWindow_Closing;
    }

    internal AppSettings SettingsData => _settings;

    internal void EnterCompactModeForQa() => EnterCompactMode();

    internal void ExitForQa() => ExitApplication();

    internal void PrepareDemoForQa(bool converted)
    {
        const string targetIp = "192.168.1.100";
        var sourcePaths = new[]
        {
            @"\\192.168.1.20\共享资料\设计部\产品图\春季新品",
            @"\\192.168.1.20\共享资料\设计部\产品图\夏季新品",
            @"\\192.168.1.20\共享资料\市场部\宣传素材"
        };

        IpBox.Text = targetIp;
        PathsBox.Text = converted
            ? string.Join(Environment.NewLine, PathParser.ParseAndReplace(string.Join(Environment.NewLine, sourcePaths), targetIp))
            : string.Join(Environment.NewLine, sourcePaths);
        SetStatus(converted ? "转换完成，共识别 3 个路径（演示）" : "已粘贴 3 个共享路径（演示）", false);
    }

    public void ApplySettings()
    {
        Topmost = _settings.AlwaysOnTop;
        if (!_qaMode)
        {
            SettingsStore.Save(_settings);
        }
        SetStatus(
            _settings.AutoOpenClipboard ? "剪贴板自动识别已开启，复制有效路径即可打开" : "设置已保存",
            false);
    }

    private WinForms.NotifyIcon BuildTrayIcon()
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("显示主窗口", null, (_, _) => Dispatcher.Invoke(ShowNormalWindow));
        menu.Items.Add("读取剪贴板并打开", null, (_, _) => Dispatcher.Invoke(PasteAndOpen));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(ExitApplication));

        System.Drawing.Icon? appIcon = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
            {
                appIcon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath);
            }
        }
        catch
        {
            // 开发环境取不到 EXE 图标时使用系统默认图标。
        }

        var icon = new WinForms.NotifyIcon
        {
            Text = "共享速览 · @老李Oldlee",
            Icon = appIcon ?? System.Drawing.SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        icon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowNormalWindow);
        return icon;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void PathsBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.V)
        {
            e.Handled = true;
            PasteAndOpen();
        }
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_compact && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.V)
        {
            e.Handled = true;
            PasteAndOpen();
        }
        else if (_compact && e.Key == Key.Escape)
        {
            ShowNormalWindow();
        }
    }

    private void PathsBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        PastePlaceholder.Visibility = string.IsNullOrEmpty(PathsBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void PasteOpenButton_Click(object sender, RoutedEventArgs e) => PasteAndOpen();
    private void CompactPasteButton_Click(object sender, RoutedEventArgs e) => PasteAndOpen();
    private void CompactPasteMenu_Click(object sender, RoutedEventArgs e) => PasteAndOpen();

    private void StartClipboardListener()
    {
        _windowHandle = new WindowInteropHelper(this).Handle;
        _windowSource = HwndSource.FromHwnd(_windowHandle);
        _windowSource?.AddHook(WindowMessageHook);
        AddClipboardFormatListener(_windowHandle);
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmClipboardUpdate && _settings.AutoOpenClipboard && !_processingClipboardUpdate)
        {
            Dispatcher.BeginInvoke(new Action(TryAutoOpenClipboard));
        }

        return IntPtr.Zero;
    }

    private void TryAutoOpenClipboard()
    {
        if (!_settings.AutoOpenClipboard || _processingClipboardUpdate)
        {
            return;
        }

        _processingClipboardUpdate = true;
        try
        {
            if (!System.Windows.Clipboard.ContainsText())
            {
                return;
            }

            var text = System.Windows.Clipboard.GetText(System.Windows.TextDataFormat.UnicodeText);
            var targetIp = IpBox.Text.Trim();
            if (!PathParser.IsValidTargetIp(targetIp))
            {
                return;
            }

            var detectedTargets = PathParser.ParseOpenTargets(
                    text,
                    targetIp,
                    _settings.RecognizeWebUrls,
                    _settings.RecognizeLocalPaths)
                .Where(item => item.Kind != OpenTargetKind.SharedPath
                               || _settings.AutoOpenSameTargetIp
                               || !string.Equals(item.SourceHost, targetIp, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (detectedTargets.Length == 0)
            {
                return;
            }

            PathsBox.Text = text;
            if (!IsVisible || _compact)
            {
                _trayIcon.ShowBalloonTip(
                    1200,
                    "检测到可打开内容",
                    $"正在自动打开 {detectedTargets.Length} 项",
                    WinForms.ToolTipIcon.Info);
            }
            ProcessAndOpen(text, detectedTargets);
        }
        catch (ExternalException)
        {
            // 剪贴板暂时被其他程序占用时忽略本次更新，下一次复制会再次触发。
        }
        finally
        {
            _processingClipboardUpdate = false;
        }
    }

    private void PasteAndOpen()
    {
        try
        {
            if (!System.Windows.Clipboard.ContainsText())
            {
                SetStatus("剪贴板里没有文字，请先复制共享路径", true, showDialogWhenCompact: true);
                return;
            }

            var text = System.Windows.Clipboard.GetText(System.Windows.TextDataFormat.UnicodeText);
            PathsBox.Text = text;
            ProcessAndOpen(text);
        }
        catch (ExternalException)
        {
            SetStatus("剪贴板暂时被占用，请再试一次", true, showDialogWhenCompact: true);
        }
    }

    private void ProcessAndOpen(string text, IReadOnlyList<OpenTarget>? selectedTargets = null)
    {
        var targetIp = IpBox.Text.Trim();
        if (!PathParser.IsValidTargetIp(targetIp))
        {
            SetStatus("请先填写正确的 IPv4 地址，例如 192.168.1.100", true, showDialogWhenCompact: true);
            if (!_compact)
            {
                IpBox.Focus();
                IpBox.SelectAll();
            }
            return;
        }

        var targets = selectedTargets ?? PathParser.ParseOpenTargets(
            text,
            targetIp,
            _settings.RecognizeWebUrls,
            _settings.RecognizeLocalPaths);
        if (targets.Count == 0)
        {
            SetStatus("没有识别到可打开内容（网址/本地路径需在设置中开启）", true, showDialogWhenCompact: true);
            return;
        }

        if (_settings.ConfirmLargeBatch && targets.Count > 20)
        {
            var answer = System.Windows.MessageBox.Show(
                $"识别到 {targets.Count} 项内容，将打开很多窗口。是否继续？",
                "确认批量打开", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
            {
                SetStatus($"已取消打开 {targets.Count} 项", true);
                return;
            }
        }

        SaveIp(silent: true);
        var launched = 0;
        var launchErrors = new List<string>();
        foreach (var target in targets)
        {
            try
            {
                PathLauncher.Open(target);
                launched++;
            }
            catch (Exception exception)
            {
                launchErrors.Add(exception.Message);
            }
        }

        if (_settings.ClearAfterOpen)
        {
            PathsBox.Clear();
        }

        SetStatus(launched == targets.Count
            ? $"已发送打开 {launched} 项"
            : $"识别 {targets.Count} 项，成功发送 {launched} 项", launched != targets.Count);

        if (launchErrors.Count > 0)
        {
            System.Windows.MessageBox.Show(
                $"有 {launchErrors.Count} 项内容未能打开。\n\n{launchErrors[0]}",
                "打开失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        if (_settings.MinimizeAfterOpen && !_compact)
        {
            EnterCompactMode();
        }
    }

    private void PreviewButton_Click(object sender, RoutedEventArgs e)
    {
        var targetIp = IpBox.Text.Trim();
        if (!PathParser.IsValidTargetIp(targetIp))
        {
            SetStatus("请先填写正确的目标 IP", true);
            return;
        }

        var targets = PathParser.ParseOpenTargets(
            PathsBox.Text,
            targetIp,
            _settings.RecognizeWebUrls,
            _settings.RecognizeLocalPaths);
        if (targets.Count == 0)
        {
            SetStatus("没有识别到可预览内容", true);
            return;
        }

        PathsBox.Text = string.Join(Environment.NewLine, targets.Select(item => item.Value));
        SetStatus($"预览完成，共识别 {targets.Count} 项（尚未打开）", false);
    }

    private void SaveIpButton_Click(object sender, RoutedEventArgs e) => SaveIp(silent: false);

    private bool SaveIp(bool silent)
    {
        var value = IpBox.Text.Trim();
        if (!PathParser.IsValidTargetIp(value))
        {
            SetStatus("IP 格式不正确，请输入例如 192.168.1.100", true);
            IpBox.Focus();
            IpBox.SelectAll();
            return false;
        }

        _settings.TargetIp = value;
        if (!_qaMode)
        {
            SettingsStore.Save(_settings);
        }
        if (!silent)
        {
            SetStatus($"目标 IP 已保存 · {value}", false);
        }
        return true;
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        PathsBox.Clear();
        PathsBox.Focus();
        SetStatus("已清空，等待粘贴路径", false);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => OpenSettings();
    private void SettingsMenu_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void OpenSettings()
    {
        var settingsWindow = new SettingsWindow(this, _settings);
        settingsWindow.ShowDialog();
    }

    private void CompactButton_Click(object sender, RoutedEventArgs e) => EnterCompactMode();

    private void EnterCompactMode()
    {
        if (!SaveIp(silent: true))
        {
            return;
        }

        _compact = true;
        _settings.CompactMode = true;
        if (!_qaMode)
        {
            SettingsStore.Save(_settings);
        }
        NormalView.Visibility = Visibility.Collapsed;
        CompactView.Visibility = Visibility.Visible;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        MinWidth = 0;
        MinHeight = 0;
        Width = 250;
        Height = 92;
        Shell.CornerRadius = new CornerRadius(25);
        Activate();
        Focus();
    }

    private void CompactView_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Source is not System.Windows.Controls.Button && e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CompactExpand_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ShowNormalWindow();
    }

    private void ExpandMenu_Click(object sender, RoutedEventArgs e) => ShowNormalWindow();

    private void ShowNormalWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        _compact = false;
        _settings.CompactMode = false;
        if (!_qaMode)
        {
            SettingsStore.Save(_settings);
        }
        NormalView.Visibility = Visibility.Visible;
        CompactView.Visibility = Visibility.Collapsed;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar = true;
        Width = Math.Max(640, Width);
        Height = Math.Max(610, Height);
        MinWidth = 570;
        MinHeight = 560;
        Shell.CornerRadius = new CornerRadius(24);
        Activate();
        PathsBox.Focus();
    }

    private void HideButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        _trayIcon.ShowBalloonTip(1200, "共享速览", "程序仍在右下角托盘运行", WinForms.ToolTipIcon.Info);
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

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

    private void SetStatus(string message, bool error, bool showDialogWhenCompact = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(error ? "#D14343" : "#4F596B"));
        StatusDot.Fill = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(error ? "#FF5F57" : "#34C759"));
        if (showDialogWhenCompact && _compact)
        {
            System.Windows.MessageBox.Show(message, "共享速览", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void Window_LocationChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Normal && Left >= 0 && Top >= 0)
        {
            _settings.Left = (int)Left;
            _settings.Top = (int)Top;
            if (!_qaMode)
            {
                SettingsStore.Save(_settings);
            }
        }
    }

    private void RestoreLocation()
    {
        if (_settings.Left < 0 || _settings.Top < 0)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        var visible = SystemParameters.WorkArea.IntersectsWith(
            new Rect(_settings.Left, _settings.Top, Width, Height));
        if (visible)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = _settings.Left;
            Top = _settings.Top;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_reallyExit)
        {
            return;
        }
        e.Cancel = true;
        Hide();
    }

    private void ExitMenu_Click(object sender, RoutedEventArgs e) => ExitApplication();

    private void ExitApplication()
    {
        _reallyExit = true;
        if (_windowHandle != IntPtr.Zero)
        {
            RemoveClipboardFormatListener(_windowHandle);
        }
        _windowSource?.RemoveHook(WindowMessageHook);
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr windowHandle);
}
