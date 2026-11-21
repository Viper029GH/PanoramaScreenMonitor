using System.Diagnostics;
using System.Security.Cryptography;

namespace PanoramaScreenMonitor;

internal sealed record Transition(bool State, string? Diagnostic, uint Brightness = 0, string? BrightnessError = null);
internal sealed record DeviceInfo(string? Product, string? Firmware, string? AppVersion, string? Os);
internal sealed record Compatibility(DeviceInfo? Info, Transition? Configuration, string? InfoError, string? BlockReason)
{
    internal bool CanControl => Configuration is not null && BlockReason is null;
}

internal interface IScreenTransport
{
    long? Session();
    Task<Transition> ApplyAsync(bool? target, Func<bool> mayWrite, CancellationToken token, uint? brightness = null);
    Task<DeviceInfo> ReadDeviceInfoAsync(CancellationToken token);
}

internal interface IUsbScreenTransport : IScreenTransport
{
    UsbInventory Discover();
    bool HasJournal();
    void Bind(UsbCandidate? candidate);
    Task<Compatibility> CheckAsync(CancellationToken token);
}

internal sealed class PanoramaTransport : IUsbScreenTransport
{
    private static readonly int CurrentSession = GetCurrentSession();
    private static long sequence = RandomNumberGenerator.GetInt32(1, int.MaxValue) * 1_000_000L;
    private UsbCandidate? selected;
    private long? verifiedSession;

    public UsbInventory Discover() => UsbDevice.Discover();
    public bool HasJournal() => UdbTail.HasCurrentLog();

    public void Bind(UsbCandidate? candidate)
    {
        selected = candidate;
        verifiedSession = null;
    }

    private static int GetCurrentSession()
    {
        using var process = Process.GetCurrentProcess();
        return process.SessionId;
    }

    public long? Session()
    {
        var processes = Process.GetProcessesByName("UDB");
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (process.SessionId == CurrentSession)
                        return process.StartTime.ToUniversalTime().Ticks;
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
            return null;
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    public async Task<Transition> ApplyAsync(bool? target, Func<bool> mayWrite, CancellationToken token,
        uint? brightness = null)
    {
        long session = RequireSession();
        var device = RequireDevice();
        string path = device.Path;
        if ((target.HasValue || brightness.HasValue) && verifiedSession != session)
            throw new IOException("Сначала требуется проверка чтения для этого устройства.");
        var config = await QueryAsync(path, session, token).ConfigureAwait(false);
        var current = ReadDisplay(config);
        if (brightness.HasValue && !current.State)
            return current with { BrightnessError = "Яркость можно изменить только при включённом экране." };
        if ((target is null || current.State == target) && (brightness is null || current.Brightness == brightness))
            return current;

        bool writeAttempted = false;
        try
        {
            byte[] payload = Protocol.Configuration(config, target, brightness);
            ulong track = NextTrack();
            using var ack = new UdbTail(track);
            if (!mayWrite()) return current;
            if (!UsbDevice.IsPresent(device)) throw new UsbInterfaceException("Выбранный экран отключён.");
            writeAttempted = true;
            if (!UsbDevice.Write(path, Protocol.Frame(track, 200, payload), token, () =>
                {
                    if (!mayWrite()) return false;
                    RequireSession(session);
                    return verifiedSession == session && HasJournal();
                }))
                return current;

            var verified = await QueryAsync(path, session, token).ConfigureAwait(false);
            var response = ack.Poll(block => Protocol.Error(block) || block.ContainsKey("ack")
                || block.ContainsKey("acknowledgement"));
            return Verify(verified, target ?? current.State, response, brightness);
        }
        catch (Exception ex) when (brightness.HasValue && IsReadFailure(ex))
        {
            if (writeAttempted)
            {
                try { current = ReadDisplay(await QueryAsync(path, session, token).ConfigureAwait(false)); }
                catch (Exception recovery) when (IsReadFailure(recovery))
                {
                    throw new IOException(Describe(ex), ex);
                }
            }
            return current with { BrightnessError = Describe(ex) };
        }
    }

    private static Transition ReadDisplay(Protocol.Block config) =>
        new(Protocol.Backlight(config), null, Protocol.Brightness(config));

    internal static Transition Verify(Protocol.Block verified, bool target, Protocol.Block? ack, uint? brightness = null)
    {
        var actual = ReadDisplay(verified);
        if (brightness.HasValue && (actual.Brightness != brightness || actual.State != target))
            return actual with { BrightnessError = "Чтение UDB не подтвердило выбранную яркость и состояние экрана." };
        if (actual.State != target)
            throw new IOException("Конфигурация UDB не подтвердила запрошенное состояние подсветки.");

        string? diagnostic = null;
        if (ack is not null && Protocol.Error(ack))
        {
            string? reason = ack.Child("error")?.Value("why")?.Text;
            diagnostic = reason == "\"work_config convert failed\""
                ? "UDB сообщил work_config convert failed. Состояние подтверждено чтением."
                : "UDB вернул ошибку записи. Состояние подтверждено чтением.";
        }
        return actual with { Diagnostic = diagnostic };
    }

    internal static bool IsReadFailure(Exception ex) => ex is IOException or TimeoutException or UnauthorizedAccessException
        or System.ComponentModel.Win32Exception or System.Text.DecoderFallbackException;

    internal static string Describe(Exception ex) => ex is System.Text.DecoderFallbackException
        ? "Некорректная кодировка строки UDB." : ex.Message;

    public async Task<DeviceInfo> ReadDeviceInfoAsync(CancellationToken token)
    {
        long session = RequireSession();
        return await QueryInfoAsync(RequireDevice().Path, session, token).ConfigureAwait(false);
    }

    private async Task<DeviceInfo> QueryInfoAsync(string path, long session, CancellationToken token)
    {
        var info = await QueryBlockAsync(path, session, 100, [0x0a, 0x02, 0x4e, 0x41],
            "device_info", token).ConfigureAwait(false);
        return Protocol.ReadDeviceInfo(info);
    }

    public async Task<Compatibility> CheckAsync(CancellationToken token)
    {
        verifiedSession = null;
        long session = RequireSession();
        var device = RequireDevice();
        DeviceInfo? info = null;
        string? infoError = null;
        try { info = await QueryInfoAsync(device.Path, session, token).ConfigureAwait(false); }
        catch (Exception ex) when (IsReadFailure(ex)) { infoError = Describe(ex); }

        Protocol.Block? config = null;
        string? configError = null;
        try { config = await QueryAsync(device.Path, session, token).ConfigureAwait(false); }
        catch (Exception ex) when (IsReadFailure(ex)) { configError = Describe(ex); }

        var result = Assess(device.Profile, info, config, infoError, configError);
        RequireSession(session);
        if (!UsbDevice.IsPresent(device)) throw new UsbInterfaceException("Выбранный экран отключён.");
        if (result.CanControl) verifiedSession = session;
        return result;
    }

    internal static Compatibility Assess(UsbProfile profile, DeviceInfo? info, Protocol.Block? config,
        string? infoError = null, string? configError = null)
    {
        Transition? display = null;
        if (config is not null)
        {
            try
            {
                display = ReadDisplay(config);
                _ = Protocol.Configuration(config, null);
            }
            catch (Exception ex) when (IsReadFailure(ex)) { configError = Describe(ex); }
        }

        if (profile.Experimental && string.IsNullOrWhiteSpace(info?.Product))
            infoError ??= "В ответе DeviceInfo отсутствует модель экрана.";

        string? block = configError;
        if (config is null) block ??= "В ответе UDB отсутствует запрошенный блок.";
        if (profile.Experimental)
        {
            block ??= infoError;
            if (!string.IsNullOrWhiteSpace(info?.Product) && !profile.MatchesProduct(info.Product))
                block = "Модель из DeviceInfo не соответствует USB-профилю.";
            if (display?.Brightness > 100) block ??= "Яркость в конфигурации выходит за диапазон 0–100%.";
        }
        return new(info, display, infoError, block);
    }

    private UsbCandidate RequireDevice() => selected
        ?? throw new UsbInterfaceException("Выбранный экран отключён.");

    private long RequireSession(long? expected = null)
    {
        long current = Session() ?? throw new IOException("Ожидание запуска KANALI.");
        if (expected.HasValue && current != expected)
            throw new IOException("KANALI завершён или перезапущен. Ожидание подключения.");
        return current;
    }

    private static ulong NextTrack() => (ulong)Interlocked.Increment(ref sequence);

    private Task<Protocol.Block> QueryAsync(string path, long session, CancellationToken token) =>
        QueryBlockAsync(path, session, 104, [], "user_config", token);

    private async Task<Protocol.Block> QueryBlockAsync(string path, long session, int command, byte[] body,
        string blockName, CancellationToken token)
    {
        ulong track = NextTrack();
        using var tail = new UdbTail(track);
        await tail.WaitForFileAsync(token).ConfigureAwait(false);
        RequireSession(session);
        UsbDevice.Write(path, Protocol.Frame(track, command, body), token);
        var response = await tail.WaitAsync(
            block => block.ContainsKey(blockName) || Protocol.Error(block), token).ConfigureAwait(false);
        if (Protocol.Error(response)) throw new IOException("UDB сообщил ошибку чтения.");
        return response.Child(blockName) ?? throw new IOException("В ответе UDB отсутствует запрошенный блок.");
    }
}

internal sealed class DemoTransport : IScreenTransport
{
    private bool state = true;
    private uint level = 100;
    internal volatile bool FailNext;
    internal volatile bool Slow;
    internal volatile bool AckError;
    internal volatile bool FailBrightnessConfirmation;
    public long? Session() => 1;

    public async Task<DeviceInfo> ReadDeviceInfoAsync(CancellationToken token)
    {
        await Task.Delay(Slow ? 2300 : 250, token).ConfigureAwait(false);
        return new("PANO (demo)", "v2.0.6 (demo)", "v2.0.4 (demo)", "Linux · kernel 5.1");
    }

    public async Task<Transition> ApplyAsync(bool? target, Func<bool> mayWrite, CancellationToken token,
        uint? brightness = null)
    {
        await Task.Delay(Slow ? 2300 : 300, token).ConfigureAwait(false);
        if (FailNext)
        {
            FailNext = false;
            throw new TimeoutException("Демо: имитация тайм-аута ответа UDB.");
        }
        if (brightness.HasValue && !state)
            return new(state, null, level, "Яркость можно изменить только при включённом экране.");
        if (((target is null || state == target) && (brightness is null || level == brightness)) || !mayWrite())
            return new(state, null, level);

        state = target ?? state;
        level = brightness ?? level;
        await Task.Delay(Slow ? 1400 : 250, token).ConfigureAwait(false);
        if (brightness.HasValue && FailBrightnessConfirmation)
        {
            FailBrightnessConfirmation = false;
            throw new TimeoutException("Демо: чтение после записи яркости не подтвердилось.");
        }
        var verified = new Protocol.Block { ["display_config"] = new Protocol.Block
            {
                ["backlight_enable"] = new Protocol.Scalar(state ? "true" : "false", false),
                ["backlight_brightness"] = new Protocol.Scalar(level.ToString(System.Globalization.CultureInfo.InvariantCulture), false)
            } };
        var ack = AckError ? Protocol.Parse("error { code: Fail why: \"work_config convert failed\" }") : null;
        return PanoramaTransport.Verify(verified, state, ack, brightness);
    }
}
