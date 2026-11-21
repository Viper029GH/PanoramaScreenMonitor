using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace PanoramaScreenMonitor;

internal enum Mode { Auto, Paused, Manual }

internal sealed class IdleInput(bool demo)
{
    private long demoIdle;
    internal long Milliseconds
    {
        get
        {
            if (demo) return Interlocked.Read(ref demoIdle);
            var input = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() };
            if (!GetLastInputInfo(ref input)) throw new IOException("Не удалось определить время последнего ввода.");
            return unchecked((uint)Environment.TickCount - input.Tick);
        }
    }

    internal void Simulate(long idle) => Interlocked.Exchange(ref demoIdle, idle);

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInput { public uint Size, Tick; }
    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInput input);
}

internal sealed class MonitorController
{
    private readonly IScreenTransport transport;
    private readonly IUsbScreenTransport? usb;
    private readonly Profile profile;
    private readonly IdleInput idle;
    private readonly Func<uint?> readDisplayTimeout;
    private readonly DispatcherTimer timer;
    private readonly CancellationTokenSource shutdown = new();
    private Task? tick;
    private long? session;
    private long nextProbe;
    private long retryAt;
    private int generation;
    private bool manualTarget;
    private bool closing;
    private bool waiting;
    private bool? lastRecordedState;
    private string? lastRecordedIssue;
    private uint? queuedBrightness;
    private uint? applyingBrightness;
    private bool infoPending;
    private bool refreshConfiguration;
    private string? automaticDevice;
    private bool compatibilityConfirmed;
    private bool rebind;
    private bool journalPresent;

    internal MonitorController(IScreenTransport transport, Profile profile, IdleInput idle, Preferences preferences,
        Func<uint?>? readDisplayTimeout = null)
    {
        this.transport = transport;
        usb = transport as IUsbScreenTransport;
        this.profile = profile;
        this.idle = idle;
        this.readDisplayTimeout = readDisplayTimeout ?? WindowsDisplayTimeout.ReadSeconds;
        Settings = preferences;
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (_, _) => Pulse();
    }

    internal event Action? Changed;
    internal Mode Mode { get; private set; }
    internal Preferences Settings { get; private set; }
    internal bool? State { get; private set; }
    internal bool Busy { get; private set; }
    internal string? Issue { get; private set; }
    internal Transition? LastConfiguration { get; private set; }
    internal uint? RequestedBrightness => queuedBrightness ?? applyingBrightness;
    internal string? BrightnessIssue { get; private set; }
    internal DeviceInfo? Info { get; private set; }
    internal string? InfoIssue { get; private set; }
    internal DateTimeOffset? LastRead { get; private set; }
    internal bool InfoStale { get; private set; }
    internal bool WaitingForKanali => waiting;
    internal bool UsbUnavailable { get; private set; }
    internal UsbInventory Devices { get; private set; } = UsbInventory.Empty;
    internal UsbCandidate? Device { get; private set; }
    internal bool CanControl => usb is null || compatibilityConfirmed;
    internal bool Experimental => Device?.Profile.Experimental == true;
    internal bool CanAutoControl => !Experimental
        || Settings.ExperimentalAutoDevices?.Contains(Device!.Key) == true;
    internal string? CompatibilityIssue { get; private set; }
    internal bool ChooseDevice => Devices.Candidates.Length > 1 || (Devices.Candidates.Length > 0 && Device is null);
    internal bool JournalAvailable => usb is null || journalPresent;
    internal uint? WindowsTimeoutSeconds { get; private set; }
    internal uint? TimeoutSeconds => Settings.SyncWindowsDisplayTimeout
        ? WindowsTimeoutSeconds : (uint)Settings.Minutes * 60;
    internal string Activity { get; private set; } = "Подготовка к подключению…";

    internal void Start() { timer.Start(); Pulse(); }

    internal void Pulse()
    {
        if (closing || tick is { IsCompleted: false }) return;
        tick = TickAsync();
    }

    internal void ChangeMode(Mode mode, bool target = false)
    {
        if (closing || Busy || !CanControl || (mode == Mode.Auto && !CanAutoControl)) return;
        Mode = mode;
        manualTarget = target;
        retryAt = 0;
        nextProbe = 0;
        profile.Log(mode == Mode.Manual ? $"Ручной режим. Подсветка {(target ? "вкл." : "выкл.")}" : $"Режим {mode}.");
        Changed?.Invoke();
        Pulse();
    }

    internal void Save(Preferences preferences)
    {
        profile.Save(preferences);
        Settings = preferences;
        nextProbe = 0;
        Changed?.Invoke();
    }

    internal void SelectDevice(string key)
    {
        if (closing || !Devices.Candidates.Any(device => device.Key == key)) return;
        profile.Save(Settings with { SelectedDevice = key });
        Settings = Settings with { SelectedDevice = key };
        Mode = Mode.Auto;
        if (Device?.Key != key) Device = null;
        ResetCompatibility(clearData: true);
        Invalidate();
    }

    internal void AllowExperimentalAuto()
    {
        if (closing || Busy || !Experimental || !CanControl || CanAutoControl) return;
        string[] approved = [.. Settings.ExperimentalAutoDevices ?? [], Device!.Key];
        Save(Settings with { ExperimentalAutoDevices = approved });
        ChangeMode(Mode.Auto);
    }

    internal void Invalidate()
    {
        if (closing) return;
        Interlocked.Increment(ref generation);
        if (usb is not null)
        {
            ResetCompatibility(clearData: false);
            rebind = true;
        }
        State = null;
        UsbUnavailable = false;
        queuedBrightness = null;
        infoPending = usb is null;
        InfoStale = true;
        retryAt = 0;
        nextProbe = 0;
        Changed?.Invoke();
        Pulse();
    }

    internal void SetBrightness(uint value)
    {
        if (closing || !CanControl || State != true || value > 100) return;
        queuedBrightness = value;
        BrightnessIssue = null;
        Changed?.Invoke();
        Pulse();
    }

    internal void RefreshDiagnostics()
    {
        if (closing) return;
        if (usb is not null)
        {
            Invalidate();
            return;
        }
        infoPending = true;
        refreshConfiguration = true;
        retryAt = 0;
        Pulse();
    }

    private async Task TickAsync()
    {
        int revision = Volatile.Read(ref generation);
        try
        {
            long now = Environment.TickCount64;
            if (now >= nextProbe)
            {
                nextProbe = now + 1000;
                bool sync = Settings.SyncWindowsDisplayTimeout;
                var (current, timeout, devices, journal) = await Task.Run(() =>
                    (transport.Session(), sync ? readDisplayTimeout() : null, usb?.Discover(), usb?.HasJournal() == true));
                if (closing || revision != generation) return;
                WindowsTimeoutSeconds = timeout;
                if (usb is not null)
                    UpdateConnection(current, devices!, journal);
                else if (current != session)
                {
                    session = current;
                    State = null;
                    UsbUnavailable = false;
                    queuedBrightness = null;
                    infoPending = current.HasValue;
                    InfoStale = true;
                    retryAt = 0;
                }
                if (current is null)
                {
                    if (!waiting) profile.Log("Ожидание KANALI (UDB).");
                    waiting = true;
                    Activity = "Ожидание запуска KANALI";
                }
                else waiting = false;
            }
            if (usb is not null)
            {
                if (Device is null)
                {
                    Activity = ChooseDevice ? "Выберите экран в настройках."
                        : Devices.MissingPrinter.Length > 0 ? "Интерфейс USBPRINT отсутствует."
                        : Devices.Unknown.Length > 0 ? "Неизвестный USB ID TRYX."
                        : Settings.SelectedDevice is not null || automaticDevice is not null ? "Выбранный экран отключён."
                        : "Устройство Panorama не найдено.";
                    return;
                }
                if (session is not null && !journalPresent)
                {
                    Activity = "Ожидание журнала UDB. USB-команда не отправлена.";
                    return;
                }
                if (session is not null && !compatibilityConfirmed)
                {
                    if (now >= retryAt) await CheckCompatibilityAsync(revision);
                    else Activity = "Ожидание повторной проверки";
                    return;
                }
            }
            if (session is null) return;

            uint? timeoutSeconds = TimeoutSeconds;
            bool automatic = Mode == Mode.Auto;
            bool followsWindows = Settings.SyncWindowsDisplayTimeout;
            long idleTime = automatic && timeoutSeconds is > 0 ? idle.Milliseconds : 0;
            long threshold = timeoutSeconds.GetValueOrDefault() * 1000L;
            bool? target = Mode switch
            {
                Mode.Manual => manualTarget,
                Mode.Paused => null,
                _ when !CanAutoControl => null,
                _ when timeoutSeconds is null => null,
                _ => threshold == 0 || idleTime < threshold
            };
            if (State != true) queuedBrightness = null;
            bool needsPower = State is null || (target.HasValue && State != target);
            bool coolingDown = now < retryAt;
            if ((!needsPower || coolingDown) && queuedBrightness is null && (!refreshConfiguration || coolingDown))
            {
                if (infoPending)
                {
                    await ReadDiagnosticsAsync(revision);
                    return;
                }
                Activity = coolingDown ? "Ожидание повторной проверки" : Mode switch
                {
                    Mode.Paused => "Автоматические переходы приостановлены",
                    Mode.Manual => "Удержание выбранного состояния",
                    _ when !CanAutoControl => "Автоуправление для этой модели выключено.",
                    _ when timeoutSeconds is null => "Тайм-аут Windows недоступен",
                    _ when timeoutSeconds == 0 => "Автовыключение отключено в Windows",
                    _ when State == false => "Ожидание ввода",
                    _ => Countdown(threshold, idleTime)
                };
                return;
            }

            if (!needsPower || coolingDown)
            {
                applyingBrightness = queuedBrightness;
                queuedBrightness = null;
                target = null;
            }
            refreshConfiguration = false;
            Busy = true;
            Activity = applyingBrightness.HasValue ? "Применение яркости…"
                : target is null ? "Чтение состояния…" : "Выполняется команда…";
            Changed?.Invoke();
            uint? brightness = applyingBrightness;
            var result = await Task.Run(() => transport.ApplyAsync(target,
                () => revision == Volatile.Read(ref generation)
                    && (brightness.HasValue || !automatic || MayApplyAutomatic(target, followsWindows, timeoutSeconds)),
                shutdown.Token, brightness));
            if (closing || revision != generation) return;
            if (usb is null && State is null && InfoStale && Info is not null) infoPending = true;
            State = result.State;
            UsbUnavailable = false;
            LastConfiguration = result;
            LastRead = DateTimeOffset.Now;
            if (!result.State) queuedBrightness = null;
            if (applyingBrightness.HasValue)
            {
                BrightnessIssue = result.BrightnessError;
                profile.Log(BrightnessIssue ?? $"Яркость подтверждена: {result.Brightness}%.");
            }
            Issue = result.Diagnostic;
            if (lastRecordedState != State || lastRecordedIssue != Issue)
                profile.Log($"Подсветка {(State == true ? "включена" : "выключена")}. {Issue}".TrimEnd());
            lastRecordedState = State;
            lastRecordedIssue = Issue;
        }
        catch (OperationCanceledException) when (closing) { }
        catch (Exception ex) when (PanoramaTransport.IsReadFailure(ex))
        {
            if (closing || revision != generation) return;
            State = null;
            UsbUnavailable = ex is UsbInterfaceException;
            queuedBrightness = null;
            InfoStale = true;
            Issue = PanoramaTransport.Describe(ex);
            if (usb is not null)
            {
                compatibilityConfirmed = false;
                CompatibilityIssue = Issue;
            }
            if (applyingBrightness.HasValue) BrightnessIssue = Issue;
            if (Issue != lastRecordedIssue) profile.Log(Issue);
            lastRecordedIssue = Issue;
            lastRecordedState = null;
            retryAt = Environment.TickCount64 + 3000;
        }
        finally
        {
            applyingBrightness = null;
            Busy = false;
            timer.Interval = TimeSpan.FromMilliseconds(State == true ? 1000 : 200);
            if (State == true && Mode == Mode.Auto && Activity == "Выполняется команда…")
                Activity = "Подсветка подтверждена";
            if (!closing) Changed?.Invoke();
        }
    }

    private void UpdateConnection(long? current, UsbInventory devices, bool journal)
    {
        Devices = devices;
        var candidate = UsbProfiles.Select(devices, Settings.SelectedDevice, ref automaticDevice);
        bool changed = candidate?.Key != Device?.Key;
        if (changed || current != session || journal != journalPresent || rebind)
        {
            if (changed) Mode = Mode.Auto;
            ResetCompatibility(clearData: changed);
            Device = candidate;
            session = current;
            journalPresent = journal;
            usb!.Bind(candidate);
            rebind = false;
        }
    }

    private void ResetCompatibility(bool clearData)
    {
        compatibilityConfirmed = false;
        CompatibilityIssue = null;
        State = null;
        queuedBrightness = null;
        InfoStale = true;
        retryAt = 0;
        if (!clearData) return;
        LastConfiguration = null;
        LastRead = null;
        Info = null;
        InfoIssue = null;
        Issue = null;
        BrightnessIssue = null;
    }

    private async Task CheckCompatibilityAsync(int revision)
    {
        Busy = true;
        Activity = "Проверка совместимости…";
        Changed?.Invoke();
        var result = await Task.Run(() => usb!.CheckAsync(shutdown.Token));
        if (closing || revision != generation) return;
        compatibilityConfirmed = result.CanControl;
        CompatibilityIssue = result.BlockReason;
        InfoIssue = result.InfoError;
        if (result.Info is not null)
        {
            Info = result.Info;
            InfoStale = false;
        }
        if (result.Configuration is { } config)
        {
            LastConfiguration = config;
            State = config.State;
        }
        if (result.Info is not null || result.Configuration is not null) LastRead = DateTimeOffset.Now;
        Issue = result.BlockReason;
        Activity = result.CanControl ? "Подсветка подтверждена" : "Управление недоступно";
        if (!result.CanControl) retryAt = Environment.TickCount64 + 3000;
    }

    private async Task ReadDiagnosticsAsync(int revision)
    {
        infoPending = false;
        Busy = true;
        Activity = "Чтение диагностики…";
        Changed?.Invoke();
        try
        {
            var info = await Task.Run(() => transport.ReadDeviceInfoAsync(shutdown.Token));
            if (closing || revision != generation) return;
            Info = info;
            UsbUnavailable = false;
            InfoIssue = null;
            InfoStale = false;
            LastRead = DateTimeOffset.Now;
        }
        catch (Exception ex) when (PanoramaTransport.IsReadFailure(ex))
        {
            if (closing || revision != generation) return;
            InfoIssue = PanoramaTransport.Describe(ex);
            if (ex is UsbInterfaceException)
            {
                UsbUnavailable = true;
                State = null;
                queuedBrightness = null;
            }
            InfoStale = true;
            profile.Log(InfoIssue);
        }
    }

    private bool MayApplyAutomatic(bool? target, bool followsWindows, uint? timeoutSeconds)
    {
        if (followsWindows) timeoutSeconds = readDisplayTimeout();
        if (timeoutSeconds is null) return false;
        bool shouldBeOn = timeoutSeconds == 0 || idle.Milliseconds < timeoutSeconds.Value * 1000L;
        return target == shouldBeOn;
    }

    private static string Countdown(long threshold, long idleTime)
    {
        var remaining = TimeSpan.FromSeconds(Math.Ceiling(Math.Max(0, threshold - idleTime) / 1000d));
        return $"До выключения  {(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";
    }

    internal async Task StopAsync()
    {
        closing = true;
        timer.Stop();
        shutdown.Cancel();
        if (tick is not null && await Task.WhenAny(tick, Task.Delay(3000)) != tick) return;
        shutdown.Dispose();
    }
}
