using System.Diagnostics;
using System.Windows.Threading;

namespace PanoramaScreenMonitor;

internal static class SelfTest
{
    private sealed class Simulation : IScreenTransport
    {
        internal long? Udb;
        internal bool State;
        internal int Reads;
        internal List<bool> Writes { get; } = [];
        internal TaskCompletionSource? Reading;
        internal TaskCompletionSource? Writing;
        internal readonly List<uint> BrightnessWrites = [];
        internal uint Brightness = 100;
        internal bool FailBrightness;
        internal int InfoReads;
        public long? Session() => Udb;

        public Task<DeviceInfo> ReadDeviceInfoAsync(CancellationToken token)
        {
            Interlocked.Increment(ref InfoReads);
            return Task.FromResult(new DeviceInfo("test PANO", "firmware", "app", "Linux"));
        }

        public async Task<Transition> ApplyAsync(bool? target, Func<bool> mayWrite, CancellationToken token,
            uint? brightness = null)
        {
            Interlocked.Increment(ref Reads);
            if (Reading is { } reading) await reading.Task.WaitAsync(token);
            if (brightness.HasValue && State && mayWrite())
            {
                Brightness = brightness.Value;
                BrightnessWrites.Add(Brightness);
                if (FailBrightness) throw new TimeoutException("Simulated brightness confirmation timeout");
            }
            if (target is not null && target != State && mayWrite())
            {
                Writes.Add(target.Value);
                State = target.Value;
                if (Writing is { } writing) await writing.Task.WaitAsync(token);
            }
            return new(State, null, Brightness);
        }
    }

    internal static int Run(string report)
    {
        try
        {
            CheckProtocol();
            var frame = new DispatcherFrame();
            Exception? failure = null;
            Dispatcher.CurrentDispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await CheckControllerAsync();
                    await CheckWindowsTimeoutAsync();
                    await CheckBrightnessAndDiagnosticsAsync();
                }
                catch (Exception ex) { failure = ex; }
                finally { frame.Continue = false; }
            });
            Dispatcher.PushFrame(frame);
            if (failure is not null) throw failure;
            File.WriteAllText(report, "PASS: wire framing, full configuration, UDB log correlation/rotation/timeout, "
                + "contradictory ACK, waiting for KANALI, active startup, pause/manual mode, "
                + "idle/input race before and after OFF, KANALI restart, live Windows timeout changes, Never, "
                + "brightness coalescing and confirmation timeout, DeviceInfo initial/reconnect/refresh, localization.\n"
                + $"Current Windows display timeout (seconds): {WindowsDisplayTimeout.ReadSeconds()?.ToString() ?? "unavailable"}");
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(report, ex.ToString());
            return 1;
        }
    }

    private static void CheckProtocol()
    {
        static string Header(int id) => $"[USB][Recv] RspPackage: header {{\n  version: 1\n  track_id: {id}\n}}\nerror {{\n}}\n";
        const string body = "user_config {\n  work_config {\n    single_mode_media_file: \"A\\040B\"\n  }\n"
            + "  display_config {\n    backlight_brightness: 80\n    mirror: true\n    ui_rotation: 90\n  }\n}\n";
        string directory = Directory.CreateTempSubdirectory("panorama-test-").FullName;
        try
        {
            string log = Path.Combine(directory, $"udb_{DateTime.Now:yyyy-MM-dd}.log");
            File.WriteAllText(log, Header(42) + body);
            using var tail = new UdbTail(42, directory);
            static bool Accepts(Protocol.Block response) => response.ContainsKey("user_config");
            Require(tail.Poll(Accepts) is null, "Old log response ignored");
            File.AppendAllText(log, Header(420) + body + Header(42) + body[..^2]);
            Require(tail.Poll(Accepts) is null, "Wrong track and incomplete block ignored");
            File.AppendAllText(log, body[^2..]);
            var off = tail.Poll(Accepts)?.Child("user_config") ?? throw new Exception("Split response");
            Require(!Protocol.Backlight(off), "Missing backlight defaults to false in display block");
            byte[] encoded = Protocol.Configuration(off, true);
            Require(Convert.ToHexString(encoded) == "1A051A034120422A0810501801205A0801", "Full configuration preserved");
            Require(Convert.ToHexString(Protocol.Frame(42, 104, [])) == "54525958090000000A040801102AC20600", "Wire frame");
            Require(Protocol.Brightness(off) == 80, "Read current brightness");
            var on = Protocol.Parse("display_config { backlight_enable: true backlight_brightness: 80 mirror: true }")!;
            Require(Convert.ToHexString(Protocol.Configuration(on, null, 0)) == "2A0408011801",
                "Zero brightness preserves backlight enable and other fields");
            Require(Protocol.Brightness(Protocol.Parse("display_config {}")!) == 0, "Default brightness");
            Require(PanoramaTransport.Verify(on, true, null, 40).BrightnessError is not null,
                "Unexpected readback must not confirm a brightness change");
            Require(Convert.ToHexString(Protocol.Frame(42, 100, [0x0a, 0x02, 0x4e, 0x41])) ==
                "545259580D0000000A040801102AA206040A024E41", "DeviceInfo request frame");
            var ack = Protocol.Parse("error { code: Fail why: \"work_config convert failed\" }");
            Require(PanoramaTransport.Verify(off, false, ack).Diagnostic is not null, "Readback overrides ACK");
            off.Child("display_config")!.Add("unexpected", new Protocol.Scalar("1", false));
            bool rejected = false;
            try { Protocol.Configuration(off, true); }
            catch (IOException) { rejected = true; }
            Require(rejected, "Unknown configuration field blocks write");

            using var rotated = new UdbTail(43, directory);
            File.Move(log, log + ".old");
            File.WriteAllText(log, Header(43) + body);
            Require(rotated.Poll(Accepts) is not null, "File rotation detected");
            using var device = new UdbTail(45, directory);
            const string info = "device_info {\n  product_name: \"PANO\"\n  firmware_version: \"fw\"\n"
                + "  app_version: \"app\"\n  os_name: \"Linux\"\n  os_version: \"kernel 5.1\"\n}\n";
            static bool AcceptsInfo(Protocol.Block response) => !Protocol.Error(response) && response.ContainsKey("device_info");
            File.AppendAllText(log, Header(450) + info + Header(45) + info[..^2]);
            Require(device.Poll(AcceptsInfo) is null, "Ignore unrelated or partial DeviceInfo");
            File.AppendAllText(log, info[^2..]);
            var details = device.Poll(AcceptsInfo)?.Child("device_info");
            Require(details is not null && Protocol.ReadDeviceInfo(details) == new DeviceInfo("PANO", "fw", "app", "Linux · kernel 5.1"),
                "Parse only the matching DeviceInfo block");
            using var unmatched = new UdbTail(44, directory);
            try
            {
                _ = unmatched.WaitAsync(Accepts, CancellationToken.None).GetAwaiter().GetResult();
                throw new Exception("Timeout expected");
            }
            catch (TimeoutException) { }
        }
        finally { Directory.Delete(directory, true); }
    }

    private static async Task CheckControllerAsync()
    {
        var input = new IdleInput(true);
        var transport = new Simulation();
        var controller = new MonitorController(transport, new Profile(true), input, new(1));
        controller.Start();
        try
        {
            await Eventually(() => controller.Activity == "Ожидание запуска KANALI");
            Require(transport.Reads == 0, "No access before KANALI");
            transport.Udb = 1;
            await Eventually(() => !controller.Busy && controller.State == true && controller.Info is not null);
            Require(transport.Writes.SequenceEqual([true]), "Active startup restores off screen");

            transport.Reading = new(TaskCreationOptions.RunContinuationsAsynchronously);
            int reads = transport.Reads;
            input.Simulate(60_000);
            controller.Pulse();
            await Eventually(() => transport.Reads > reads);
            input.Simulate(0);
            transport.Reading.SetResult();
            await Eventually(() => !controller.Busy);
            transport.Reading = null;
            Require(transport.Writes.Count == 1 && controller.State == true, "Return before OFF skips write");

            transport.Writing = new(TaskCreationOptions.RunContinuationsAsynchronously);
            input.Simulate(60_000);
            controller.Pulse();
            await Eventually(() => !transport.State && controller.Busy);
            input.Simulate(0);
            transport.Writing.SetResult();
            await Eventually(() => transport.Writes.Count == 3 && !controller.Busy && controller.State == true);
            Require(transport.Writes.SequenceEqual([true, false, true]), "Input during OFF is followed by ON");

            controller.ChangeMode(Mode.Manual, false);
            await Eventually(() => !controller.Busy && controller.State == false);
            await Task.Delay(360);
            Require(controller.State == false, "Manual OFF is held despite input");
            controller.ChangeMode(Mode.Paused);
            transport.Udb = 2;
            await Eventually(() => transport.Reads >= reads + 5 && !controller.Busy);
            Require(controller.State == false, "Paused after KANALI restart reads without switching");
            controller.ChangeMode(Mode.Auto);
            await Eventually(() => !controller.Busy && controller.State == true);
        }
        finally { await controller.StopAsync(); }
    }

    private static async Task CheckWindowsTimeoutAsync()
    {
        var input = new IdleInput(true);
        var transport = new Simulation { Udb = 1, State = true };
        int seconds = 30;
        var controller = new MonitorController(transport, new Profile(true), input,
            new(Minutes: 10, SyncWindowsDisplayTimeout: true),
            () =>
            {
                int value = Volatile.Read(ref seconds);
                return value >= 0 ? (uint)value : null;
            });
        controller.Start();
        try
        {
            await Eventually(() => controller.TimeoutSeconds == 30 && controller.State == true);
            input.Simulate(31_000);
            await Eventually(() => controller.State == false);
            Volatile.Write(ref seconds, -1);
            await Eventually(() => controller.TimeoutSeconds is null);
            Require(controller.State == false, "Unavailable Windows time preserves screen state");
            Volatile.Write(ref seconds, 0);
            await Eventually(() => controller.State == true && controller.TimeoutSeconds == 0);
            Volatile.Write(ref seconds, 20);
            await Eventually(() => controller.State == false && controller.TimeoutSeconds == 20);
        }
        finally { await controller.StopAsync(); }
    }

    private static async Task CheckBrightnessAndDiagnosticsAsync()
    {
        foreach (var choice in UiLanguage.Choices.Where(choice => choice.Code != "ru"))
            Require(new UiLanguage(choice.Code).Text("Сохранить") != "Сохранить", "Localized settings");

        var input = new IdleInput(true);
        var transport = new Simulation { Udb = 1, State = true };
        var controller = new MonitorController(transport, new Profile(true), input, new(1));
        controller.Start();
        try
        {
            await Eventually(() => !controller.Busy && controller.Info?.AppVersion == "app");
            Require(controller.LastConfiguration?.Brightness == 100 && controller.LastRead.HasValue,
                "Initial diagnostics retain confirmed configuration and read time");
            transport.Reading = new(TaskCreationOptions.RunContinuationsAsynchronously);
            controller.SetBrightness(40);
            await Eventually(() => controller.Busy);
            controller.SetBrightness(45);
            controller.SetBrightness(50);
            transport.Reading.SetResult();
            await Eventually(() => !controller.Busy && controller.LastConfiguration?.Brightness == 50);
            Require(transport.BrightnessWrites.SequenceEqual([40u, 50u]), "Only last pending brightness applied");
            Require(controller.Mode == Mode.Auto && controller.State == true, "Brightness preserves power and mode");
            transport.Reading = null;

            int infoReads = transport.InfoReads;
            controller.RefreshDiagnostics();
            await Eventually(() => transport.InfoReads > infoReads && !controller.Busy);
            Require(transport.Writes.Count == 0, "Diagnostics is read-only");

            transport.FailBrightness = true;
            controller.SetBrightness(20);
            await Eventually(() => !controller.Busy && controller.BrightnessIssue is not null);
            Require(controller.State is null && controller.LastConfiguration?.Brightness == 50,
                "Confirmation timeout marks the old brightness stale");
            transport.FailBrightness = false;
            await Eventually(() => controller.State == true && controller.LastConfiguration?.Brightness == 20);

            transport.Reading = new(TaskCreationOptions.RunContinuationsAsynchronously);
            controller.SetBrightness(25);
            await Eventually(() => controller.Busy);
            input.Simulate(60_000);
            Require(transport.Writes.Count == 0 && controller.State == true, "Power waits for in-flight brightness");
            transport.Reading.SetResult();
            await Eventually(() => controller.State == false);
            transport.Reading = null;
            Require(controller.LastConfiguration?.Brightness == 25, "Automatic OFF preserves applied brightness");
            controller.SetBrightness(30);
            Require(controller.RequestedBrightness is null, "Brightness cannot be queued while off");
            input.Simulate(0);
            await Eventually(() => controller.State == true);
            infoReads = transport.InfoReads;
            transport.Udb = null;
            await Eventually(() => controller.WaitingForKanali && controller.State is null && controller.InfoStale);
            transport.Udb = 2;
            await Eventually(() => transport.InfoReads > infoReads && !controller.Busy && !controller.InfoStale);
        }
        finally { await controller.StopAsync(); }
    }

    private static async Task Eventually(Func<bool> condition)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            Require(elapsed.ElapsedMilliseconds < 5000, "Controller test timed out");
            await Task.Delay(20);
        }
    }

    private static void Require(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
    }
}
