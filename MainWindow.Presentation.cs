using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace PanoramaScreenMonitor;

public partial class MainWindow
{
    private sealed record DeviceOption(string Key, string Label)
    {
        public override string ToString() => Label;
    }

    private bool refreshingDevices;

    private string L(string text) => language.Text(text);
    private string F(string text, params object[] values) => string.Format(language.Culture, L(text), values);
    private string? Error(string? details, string fallback) => details is null ? null : language.Error(details, fallback);

    private void CaptureLabels(DependencyObject element)
    {
        if (element is TextBlock { Text.Length: > 0 } label)
        {
            string source = label.Text;
            localize.Add(locale => label.Text = locale.Text(source));
        }
        if (element is ContentControl { Content: string content } control)
            localize.Add(locale => control.Content = locale.Text(content));
        if (element is HeaderedContentControl { Header: string title } header)
            localize.Add(locale => header.Header = locale.Text(title));
        if (element is FrameworkElement { ToolTip: string tip } item)
            localize.Add(locale => item.ToolTip = locale.Text(tip));
        string name = AutomationProperties.GetName(element);
        if (!string.IsNullOrEmpty(name))
            localize.Add(locale => AutomationProperties.SetName(element, locale.Text(name)));

        foreach (var child in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>())
            CaptureLabels(child);
    }

    private void ApplyAppearance()
    {
        language = new UiLanguage(monitor.Settings.Language);
        foreach (var apply in localize) apply(language);
        AutomationProperties.SetName(BrightnessSlider, L("Яркость"));
        var theme = monitor.Settings.Theme;
        if (!Enum.IsDefined(theme)) theme = InterfaceTheme.System;
        ThemeBox.ItemsSource = new[] { L("Системная"), L("Тёмная"), L("Светлая") };
        ThemeBox.SelectedIndex = (int)theme;
        ((System.Windows.Forms.ToolStripMenuItem)menu.Items[0]).Text = L("Открыть окно");
        trayOn.Text = L("Включить экран");
        trayOff.Text = L("Выключить экран");
        ((System.Windows.Forms.ToolStripMenuItem)menu.Items[^1]).Text = L("Выйти");
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        bool light = WindowTheme.Apply(Resources, monitor.Settings.Theme);
        menu.BackColor = System.Drawing.ColorTranslator.FromHtml(light ? "#FFFFFF" : "#1C1F25");
        menu.ForeColor = System.Drawing.ColorTranslator.FromHtml(light ? "#202936" : "#E7E9EE");
        foreach (System.Windows.Forms.ToolStripItem item in menu.Items) item.ForeColor = menu.ForeColor;
        menu.Renderer = WindowTheme.TrayRenderer(light);
    }

    private void SystemAppearanceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!exiting && monitor.Settings.Theme == InterfaceTheme.System) ApplyTheme();
        }));
    }

    private void SetSaveNotice(string message)
    {
        saveNotice = message;
        Saved.Text = L(message);
    }

    private void RefreshAdjustment()
    {
        uint? confirmed = monitor.LastConfiguration?.Brightness;
        BrightnessSlider.IsEnabled = monitor.CanControl && monitor.State == true && confirmed.HasValue;
        if (!BrightnessSlider.IsEnabled)
        {
            brightnessCommit.Stop();
            editingBrightness = mouseBrightness = keyBrightness = false;
        }

        uint? selected = editingBrightness ? (uint)BrightnessSlider.Value : monitor.RequestedBrightness;
        if (!editingBrightness)
        {
            updatingSlider = true;
            BrightnessSlider.Value = selected ?? confirmed ?? 0;
            updatingSlider = false;
        }
        BrightnessValue.Text = selected.HasValue && confirmed.HasValue && selected != confirmed
            ? $"{confirmed}% → {selected}%"
            : (confirmed.HasValue ? $"{confirmed}%" : L("Нет данных"));
        string? error = Error(monitor.BrightnessIssue, "Не удалось подтвердить яркость");
        if (confirmed.HasValue && !monitor.State.HasValue)
            error = error is null ? L("Состояние не подтверждено") : $"{error} · {L("Состояние не подтверждено")}";
        BrightnessError.Text = error;
        BrightnessError.ToolTip = monitor.BrightnessIssue;
        BrightnessError.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void BrightnessChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (updatingSlider || !BrightnessSlider.IsEnabled) return;
        editingBrightness = true;
        RefreshAdjustment();
        if (!mouseBrightness && !keyBrightness) ScheduleBrightness();
    }

    private void BrightnessMouseDown(object sender, MouseButtonEventArgs e)
    {
        mouseBrightness = true;
        brightnessCommit.Stop();
    }

    private void BrightnessMouseUp(object sender, MouseButtonEventArgs e)
    {
        mouseBrightness = false;
        CommitBrightness();
    }

    private void BrightnessCaptureLost(object sender, MouseEventArgs e)
    {
        if (mouseBrightness && e.LeftButton == MouseButtonState.Released)
        {
            mouseBrightness = false;
            CommitBrightness();
        }
    }

    private void BrightnessKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown))
            return;
        keyBrightness = true;
        brightnessCommit.Stop();
    }

    private void BrightnessKeyUp(object sender, KeyEventArgs e)
    {
        if (!keyBrightness) return;
        keyBrightness = false;
        ScheduleBrightness();
    }

    private void BrightnessFocusLost(object sender, KeyboardFocusChangedEventArgs e)
    {
        keyBrightness = false;
        if (!mouseBrightness) CommitBrightness();
    }

    private void ScheduleBrightness()
    {
        brightnessCommit.Stop();
        if (editingBrightness) brightnessCommit.Start();
    }

    private void CommitBrightness()
    {
        brightnessCommit.Stop();
        if (!editingBrightness) return;
        editingBrightness = false;
        uint value = (uint)BrightnessSlider.Value;
        uint? last = monitor.RequestedBrightness ?? monitor.LastConfiguration?.Brightness;
        if (value != last) monitor.SetBrightness(value);
        else RefreshAdjustment();
    }

    private void RefreshDiagnostics(object sender, RoutedEventArgs e) => monitor.RefreshDiagnostics();

    private void RefreshDeviceSelection()
    {
        DeviceSelectionRow.Visibility = monitor.ChooseDevice ? Visibility.Visible : Visibility.Collapsed;
        if (!monitor.ChooseDevice) return;
        var choices = monitor.Devices.Candidates.Select((device, index) =>
            new DeviceOption(device.Key, $"{index + 1}. {device.Profile.Name} · {device.Profile.Id}")).ToArray();
        refreshingDevices = true;
        try
        {
            if (DeviceSelection.ItemsSource is not DeviceOption[] previous || !previous.SequenceEqual(choices))
                DeviceSelection.ItemsSource = choices;
            string? key = monitor.Settings.SelectedDevice ?? monitor.Device?.Key;
            DeviceSelection.SelectedItem = ((DeviceOption[])DeviceSelection.ItemsSource)
                .FirstOrDefault(option => option.Key == key);
        }
        finally { refreshingDevices = false; }
    }

    private void DeviceSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (refreshingDevices || DeviceSelection.SelectedItem is not DeviceOption option) return;
        try { monitor.SelectDevice(option.Key); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            ShowDeviceSaveError();
            RefreshDeviceSelection();
        }
    }

    private void ShowDeviceSaveError()
    {
        Issue.Text = L("Не удалось сохранить настройки");
        Issue.Visibility = Visibility.Visible;
        SetSaveNotice("Не удалось сохранить настройки");
    }

    private void RefreshDeviceDetails()
    {
        var info = monitor.Info;
        var config = monitor.LastConfiguration;
        var device = monitor.Device;
        string dataMissing = L("Нет данных");
        string model = ValueOr(info?.Product, ValueOr(device?.WindowsName, device?.Profile.Name ?? "TRYX Panorama"));
        string? usbId = device?.Profile.Id ?? (demo is not null ? "391A:1011" : null);
        string title = usbId is null ? model : $"{model} · USB {usbId}";
        Device.Text = demo is null ? title : L("Деморежим · имитация RK PANO");
        bool stale = monitor.State is null;
        ConnectionText.Text = L(monitor.WaitingForKanali ? "Ожидание запуска KANALI"
            : device is null && demo is null ? monitor.Activity
            : !monitor.JournalAvailable ? "Ожидание журнала UDB. USB-команда не отправлена."
            : !stale ? "USB подключено · UDB доступен"
            : monitor.UsbUnavailable ? "Интерфейс USB недоступен"
            : monitor.LastRead is null && monitor.Issue is null ? "Подготовка к подключению…" : "Нет ответа конфигурации");
        RefreshDevice.IsEnabled = !monitor.Busy && !monitor.WaitingForKanali;
        RefreshCompatibilityDetails(device, info, stale);

        string screen = config is null ? dataMissing
            : $"{L(config.State ? "Экран включён" : "Экран выключен")} · {config.Brightness}%";
        string date = monitor.LastRead?.ToString("g", language.Culture) ?? dataMissing;
        string outdated = L("данные устарели");
        if (config is not null && stale) screen += $" · {outdated}";
        if (monitor.LastRead.HasValue && stale && (monitor.Info is null || monitor.InfoStale)) date += $" · {outdated}";
        string modelLine = F("Модель · {0}", title);
        if (info is not null && monitor.InfoStale) modelLine += $" · {outdated}";
        DeviceDetails.Text = string.Join(Environment.NewLine,
            modelLine,
            F("Прошивка · {0}", ValueOr(info?.Firmware, dataMissing)),
            F("Версия ПО экрана · {0}", ValueOr(info?.AppVersion, dataMissing)),
            F("ОС экрана · {0}", ValueOr(info?.Os, dataMissing)),
            F("Экран и яркость · {0}", screen),
            F("Последнее чтение · {0}", date));
        string? details = monitor.InfoIssue ?? (stale ? monitor.Issue : null);
        DeviceNotice.Text = Error(details, "Не удалось прочитать диагностику");
        DeviceNotice.ToolTip = details;
        DeviceNotice.Visibility = details is null ? Visibility.Collapsed : Visibility.Visible;
        if (saveNotice is not null) Saved.Text = L(saveNotice);
    }

    private void RefreshCompatibilityDetails(UsbCandidate? device, DeviceInfo? info, bool stale)
    {
        CompatibilityText.Visibility = demo is null ? Visibility.Visible : Visibility.Collapsed;
        ProbeDetails.Visibility = demo is null ? Visibility.Visible : Visibility.Collapsed;
        if (demo is not null) return;
        CompatibilityText.Text = L(device is null ? "Управление недоступно"
            : !device.Profile.Experimental && monitor.CanControl ? "Проверенная модель"
            : monitor.CanControl ? "Экспериментальная модель — чтение работает"
            : monitor.CompatibilityIssue is not null ? "Управление недоступно"
            : device.Profile.Experimental ? "Экспериментальная модель — требуется проверка" : "Управление недоступно");

        var details = new List<string>();
        if (device is not null)
        {
            details.Add($"USBPRINT · {device.Profile.Id}");
            if (!string.IsNullOrWhiteSpace(device.WindowsName)) details.Add($"Windows · {device.WindowsName}");
            details.Add(L(info is not null && !monitor.InfoStale ? "DeviceInfo прочитан" : "DeviceInfo не прочитан"));
            details.Add(L(!stale ? "Конфигурация прочитана" : "Конфигурация не прочитана"));
            if (device.Profile.Experimental) details.Add(L("Экспериментальная совместимость"));
        }
        else if (monitor.Devices.Candidates.Length > 0)
        {
            details.Add(string.Join(", ", monitor.Devices.Candidates.Select(candidate => candidate.Profile.Id)));
            details.Add(L(monitor.Activity));
        }
        if (monitor.Devices.MissingPrinter.Length > 0)
            details.Add($"{L("Интерфейс USBPRINT отсутствует.")} {string.Join(", ", monitor.Devices.MissingPrinter)}");
        if (monitor.Devices.Unknown.Length > 0)
            details.Add($"{L("Неизвестный USB ID TRYX.")} {string.Join(", ", monitor.Devices.Unknown)}");
        ProbeDetails.Text = string.Join(" · ", details);
    }

    private static string ValueOr(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
}
