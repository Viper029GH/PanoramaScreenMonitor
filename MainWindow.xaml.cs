using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Globalization;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace PanoramaScreenMonitor;

public partial class MainWindow : Window
{
    private readonly Profile profile;
    private readonly IdleInput idle;
    private readonly DemoTransport? demo;
    private readonly MonitorController monitor;
    private readonly Forms.NotifyIcon tray;
    private readonly Forms.ContextMenuStrip menu = new();
    private readonly System.Drawing.Icon trayIcon;
    private readonly Forms.ToolStripMenuItem trayOn, trayOff, trayAuto;
    private HwndSource? source;
    private IntPtr notification;
    private bool exiting;
    private bool updatingSlider, editingBrightness, mouseBrightness, keyBrightness;
    private readonly DispatcherTimer brightnessCommit = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly List<Action<UiLanguage>> localize = [];
    private UiLanguage language = new("ru");
    private string? saveNotice;

    internal MainWindow(bool demoMode)
    {
        InitializeComponent();
        profile = new Profile(demoMode);
        idle = new IdleInput(demoMode);
        demo = demoMode ? new DemoTransport() : null;
        var preferences = profile.Load();
        monitor = new MonitorController((IScreenTransport?)demo ?? new PanoramaTransport(), profile, idle, preferences);
        monitor.Changed += Refresh;
        brightnessCommit.Tick += (_, _) => CommitBrightness();
        CaptureLabels(this);
        LanguageBox.ItemsSource = UiLanguage.Choices;
        LanguageBox.DisplayMemberPath = "Name";
        LanguageBox.SelectedValuePath = "Code";
        LanguageBox.SelectedValue = UiLanguage.Normalize(preferences.Language);
        MinutesBox.Text = preferences.Minutes.ToString();
        RunCheck.IsChecked = preferences.AutoStart;
        RunCheck.IsEnabled = !demoMode;
        TrayCheck.IsChecked = preferences.StartHidden;
        SyncWindowsCheck.IsChecked = preferences.SyncWindowsDisplayTimeout;
        RefreshTimeoutFields();
        DemoPanel.Visibility = demoMode ? Visibility.Visible : Visibility.Collapsed;

        AddTrayItem("Открыть окно", ShowFromTray);
        menu.Items.Add(new Forms.ToolStripSeparator());
        trayOn = AddTrayItem("Включить экран", () => monitor.ChangeMode(Mode.Manual, true));
        trayOff = AddTrayItem("Выключить экран", () => monitor.ChangeMode(Mode.Manual, false));
        trayAuto = AddTrayItem("Пауза", ToggleMode);
        menu.Items.Add(new Forms.ToolStripSeparator());
        AddTrayItem("Выйти", () => _ = ExitAsync());
        using var iconStream = Application.GetResourceStream(new Uri("Panorama.ico", UriKind.Relative)).Stream;
        using var original = new System.Drawing.Icon(iconStream);
        trayIcon = (System.Drawing.Icon)original.Clone();
        tray = new Forms.NotifyIcon { Icon = trayIcon, ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => ShowFromTray();
        ApplyAppearance();
        SystemEvents.UserPreferenceChanged += SystemAppearanceChanged;
        Closing += (_, e) =>
        {
            if (exiting) return;
            e.Cancel = true;
            Hide();
        };
        Application.Current.SessionEnding += SessionEnding;
    }

    private Forms.ToolStripMenuItem AddTrayItem(string title, Action action)
    {
        var item = new Forms.ToolStripMenuItem(title);
        item.Click += (_, _) => action();
        menu.Items.Add(item);
        return item;
    }

    internal void Begin()
    {
        var handle = new WindowInteropHelper(this).EnsureHandle();
        if (demo is null)
        {
            source = HwndSource.FromHwnd(handle);
            source.AddHook(WindowMessage);
            var filter = new NotificationFilter
            {
                Size = Marshal.SizeOf<NotificationFilter>(),
                Type = 5,
                Class = new Guid("28d78fad-5a12-11d1-ae5b-0000f803a8c2")
            };
            notification = RegisterDeviceNotificationW(handle, ref filter, 0);
            if (notification == IntPtr.Zero) profile.Log("Не удалось подписаться на уведомления об устройствах USB.");
            SystemEvents.PowerModeChanged += PowerChanged;
        }
        Refresh();
        tray.Visible = true;
        if (!monitor.Settings.StartHidden) Show();
        monitor.Start();
    }

    internal void ShowFromTray()
    {
        if (exiting) return;
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void Refresh()
    {
        string text = monitor.State switch
        {
            true => "Экран включён",
            false => "Экран выключен",
            _ => "Состояние не подтверждено"
        };
        ScreenState.Text = L(text);
        StateIcon.Text = monitor.State switch { true => "\uE7F4", false => "\uE708", _ => "\uE897" };
        StateIcon.Foreground = monitor.State == true ? (Brush)FindResource("ThemeAccent") : (Brush)FindResource("ThemeMuted");
        ModeText.Text = L(monitor.Mode switch { Mode.Paused => "Пауза", Mode.Manual => "Ручной", _ => "Авто" });
        const string countdown = "До выключения  ";
        Activity.Text = monitor.Activity.StartsWith(countdown, StringComparison.Ordinal)
            ? F("До выключения {0}", monitor.Activity[countdown.Length..]) : L(monitor.Activity);
        Issue.Text = Error(monitor.Issue, "Ошибка связи с устройством");
        Issue.ToolTip = monitor.Issue;
        Issue.Visibility = string.IsNullOrEmpty(Issue.Text) ? Visibility.Collapsed : Visibility.Visible;
        AutoButton.Content = trayAuto.Text = L(monitor.Mode == Mode.Auto ? "Пауза" : "Возобновить автоуправление");
        OnButton.IsEnabled = OffButton.IsEnabled = AutoButton.IsEnabled = SaveButton.IsEnabled = !monitor.Busy;
        trayOn.Enabled = trayOff.Enabled = trayAuto.Enabled = !monitor.Busy;
        tray.Text = $"Panorama — {ScreenState.Text} · {ModeText.Text}";
        RefreshTimeoutFields();
        RefreshAdjustment();
        RefreshDeviceDetails();
    }

    private void SyncWindowsChanged(object sender, RoutedEventArgs e) => RefreshTimeoutFields();

    private void RefreshTimeoutFields()
    {
        bool sync = SyncWindowsCheck.IsChecked == true;
        ManualTimeoutRow.Visibility = sync ? Visibility.Collapsed : Visibility.Visible;
        WindowsTimeoutText.Visibility = sync ? Visibility.Visible : Visibility.Collapsed;
        WindowsTimeoutText.Text = !monitor.Settings.SyncWindowsDisplayTimeout
            ? L("Применится после сохранения")
            : monitor.WindowsTimeoutSeconds switch
            {
                null => L("Тайм-аут Windows пока недоступен"),
                0 => L("Windows · никогда не выключать"),
                uint seconds when seconds % 60 == 0 => F("Windows · {0} мин", seconds / 60),
                uint seconds => F("Windows · {0} с", seconds)
            };
    }

    private void ToggleMode() => monitor.ChangeMode(monitor.Mode == Mode.Auto ? Mode.Paused : Mode.Auto);
    private void ToggleAuto(object sender, RoutedEventArgs e) => ToggleMode();
    private void SetOn(object sender, RoutedEventArgs e) => monitor.ChangeMode(Mode.Manual, true);
    private void SetOff(object sender, RoutedEventArgs e) => monitor.ChangeMode(Mode.Manual, false);
    private void Minimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void HideWindow(object sender, RoutedEventArgs e) => Hide();

    private void Save(object sender, RoutedEventArgs e)
    {
        bool sync = SyncWindowsCheck.IsChecked == true;
        if (!int.TryParse(MinutesBox.Text, out int minutes) || minutes is < 1 or > 1440)
        {
            if (!sync)
            {
                SetSaveNotice("Введите от 1 до 1440 минут");
                MinutesBox.Focus();
                MinutesBox.SelectAll();
                return;
            }
            minutes = monitor.Settings.Minutes;
        }
        try
        {
            string code = LanguageBox.SelectedValue as string ?? "ru";
            var theme = (InterfaceTheme)Math.Max(0, ThemeBox.SelectedIndex);
            monitor.Save(new(minutes, RunCheck.IsChecked == true, TrayCheck.IsChecked == true, sync, code, theme));
            MinutesBox.Text = minutes.ToString();
            ApplyAppearance();
            SetSaveNotice("Настройки сохранены");
            Refresh();
            monitor.Pulse();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            SetSaveNotice("Не удалось сохранить настройки");
            profile.Log("Ошибка сохранения настроек.");
        }
    }

    private void SimulateIdle(object sender, RoutedEventArgs e)
    {
        idle.Simulate((monitor.TimeoutSeconds ?? 0) * 1000L);
        monitor.Pulse();
    }
    private void SimulateInput(object sender, RoutedEventArgs e) { idle.Simulate(0); monitor.Pulse(); }
    private void DemoSlow(object sender, RoutedEventArgs e) { if (demo is not null) demo.Slow = ((CheckBox)sender).IsChecked == true; }
    private void DemoAck(object sender, RoutedEventArgs e) { if (demo is not null) demo.AckError = ((CheckBox)sender).IsChecked == true; }
    private void DemoBrightnessTimeout(object sender, RoutedEventArgs e)
    {
        if (demo is not null)
        {
            demo.FailBrightnessConfirmation = true;
            ((Button)sender).ToolTip = L("Сработает при следующем изменении яркости");
        }
    }
    private void DemoTimeout(object sender, RoutedEventArgs e)
    {
        if (demo is null) return;
        demo.FailNext = true;
        monitor.Invalidate();
    }

    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x219 && wParam.ToInt64() is 0x8000 or 0x8004) monitor.Invalidate();
        return IntPtr.Zero;
    }

    private void PowerChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) Dispatcher.BeginInvoke(new Action(monitor.Invalidate));
    }

    private void SessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        ReleaseNotifications();
        _ = monitor.StopAsync();
    }

    private async Task ExitAsync()
    {
        if (exiting) return;
        exiting = true;
        Hide();
        ReleaseNotifications();
        await monitor.StopAsync();
        Application.Current.Shutdown();
    }

    private void ReleaseNotifications()
    {
        Application.Current.SessionEnding -= SessionEnding;
        SystemEvents.UserPreferenceChanged -= SystemAppearanceChanged;
        brightnessCommit.Stop();
        if (demo is null) SystemEvents.PowerModeChanged -= PowerChanged;
        source?.RemoveHook(WindowMessage);
        source = null;
        if (notification != IntPtr.Zero) UnregisterDeviceNotification(notification);
        notification = IntPtr.Zero;
        tray.Dispose();
        menu.Dispose();
        trayIcon.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NotificationFilter
    {
        public int Size, Type, Reserved;
        public Guid Class;
        public short Name;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr RegisterDeviceNotificationW(IntPtr handle, ref NotificationFilter filter, uint flags);
    [DllImport("user32.dll")]
    private static extern bool UnregisterDeviceNotification(IntPtr handle);
}
