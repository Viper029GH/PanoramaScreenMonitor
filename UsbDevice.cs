using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PanoramaScreenMonitor;

internal sealed class UsbInterfaceException(string message) : IOException(message);

internal static class UsbDevice
{
    internal static readonly Guid PrinterClass = new("28d78fad-5a12-11d1-ae5b-0000f803a8c2");
    internal static readonly Guid UsbClass = new("a5dcbf10-6530-11d2-901f-00c04fb951ed");

    internal static UsbInventory Discover()
    {
        var names = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var printers = Interfaces(PrinterClass, names);
        return UsbProfiles.Classify(printers, Interfaces(UsbClass), names);
    }

    internal static bool IsPresent(UsbCandidate device) =>
        Interfaces(PrinterClass).Contains(device.Path, StringComparer.OrdinalIgnoreCase);

    private static List<string> Interfaces(Guid guid, Dictionary<string, string?>? descriptions = null)
    {
        IntPtr set = SetupDiGetClassDevsW(ref guid, IntPtr.Zero, IntPtr.Zero, 0x12);
        if (set == new IntPtr(-1)) throw NativeError("Не удалось перечислить USB-устройства");
        try
        {
            var paths = new List<string>();
            for (uint i = 0; ; i++)
            {
                var data = new InterfaceData { Size = Marshal.SizeOf<InterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data))
                {
                    if (Marshal.GetLastWin32Error() == 259) return paths;
                    throw NativeError("Не удалось прочитать USB-интерфейс");
                }
                SetupDiGetDeviceInterfaceDetailW(set, ref data, IntPtr.Zero, 0, out int bytes, IntPtr.Zero);
                if (bytes < 8) throw NativeError("Не удалось получить размер пути USB");
                IntPtr detail = Marshal.AllocHGlobal(bytes);
                try
                {
                    Marshal.WriteInt32(detail, 8); // SP_DEVICE_INTERFACE_DETAIL_DATA_W on x64
                    var device = new DeviceInfoData { Size = Marshal.SizeOf<DeviceInfoData>() };
                    if (!GetDeviceInterfaceDetailWithInfo(set, ref data, detail, bytes, out _, ref device))
                        throw NativeError("Не удалось получить путь USB");
                    string path = Marshal.PtrToStringUni(detail + 4)!;
                    paths.Add(path);
                    if (descriptions is not null && UsbProfiles.ReadId(path) is not null)
                        descriptions[path] = ReadDescription(set, ref device, path);
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }

    private static string? ReadDescription(IntPtr set, ref DeviceInfoData device, string path)
    {
        var buffer = new byte[512];
        if (!SetupDiGetDeviceRegistryPropertyW(set, ref device, 0, out uint type, buffer, buffer.Length, out int size)
            || type != 1 || size < 2 || size > buffer.Length) return null;
        string value = System.Text.Encoding.Unicode.GetString(buffer, 0, size).TrimEnd('\0').Trim();
        string? instance = path.Split('#').ElementAtOrDefault(2);
        if (string.IsNullOrEmpty(value) || (!string.IsNullOrEmpty(instance)
            && value.Contains(instance, StringComparison.OrdinalIgnoreCase))) return null;
        return new string(value.Where(character => !char.IsControl(character)).Take(120).ToArray());
    }

    internal static bool Write(string path, byte[] packet, CancellationToken token, Func<bool>? mayWrite = null)
    {
        token.ThrowIfCancellationRequested();
        using var device = CreateFileW(path, 0x40000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
        if (device.IsInvalid) throw NativeError("Не удалось открыть RK PANO для записи");
        using var done = new EventWaitHandle(false, EventResetMode.ManualReset);
        IntPtr operation = Marshal.AllocHGlobal(Marshal.SizeOf<OverlappedData>());
        var buffer = GCHandle.Alloc(packet, GCHandleType.Pinned);
        try
        {
            Marshal.StructureToPtr(new OverlappedData { Event = done.SafeWaitHandle.DangerousGetHandle() },
                operation, false);
            if (mayWrite is not null && !mayWrite()) return false;
            if (!WriteFile(device, buffer.AddrOfPinnedObject(), packet.Length, IntPtr.Zero, operation)
                && Marshal.GetLastWin32Error() != 997)
                throw NativeError("Ошибка записи USB");

            int signal = WaitHandle.WaitAny([done, token.WaitHandle], 1800);
            if (signal != 0)
            {
                CancelIoEx(device, operation);
                // Windows owns the buffer and OVERLAPPED until completion, even after cancellation.
                GetOverlappedResult(device, operation, out _, true);
                token.ThrowIfCancellationRequested();
                throw new TimeoutException("Истекло время записи в USB.");
            }
            if (!GetOverlappedResult(device, operation, out int written, false))
                throw NativeError("Ошибка завершения записи USB");
            if (written != packet.Length) throw new IOException("USB принял только часть команды.");
            return true;
        }
        finally
        {
            buffer.Free();
            Marshal.FreeHGlobal(operation);
        }
    }

    private static IOException NativeError(string message)
    {
        int code = Marshal.GetLastWin32Error();
        return new IOException($"{message} (Windows {code}).", new Win32Exception(code));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InterfaceData
    {
        public int Size;
        public Guid Guid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfoData
    {
        public int Size;
        public Guid Class;
        public uint DeviceInstance;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OverlappedData
    {
        public IntPtr Internal, InternalHigh;
        public uint Offset, OffsetHigh;
        public IntPtr Event;
    }

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevsW(ref Guid guid, IntPtr enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr device, ref Guid guid,
        uint index, ref InterfaceData data);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set, ref InterfaceData data,
        IntPtr detail, int bytes, out int required, IntPtr device);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true)]
    private static extern bool GetDeviceInterfaceDetailWithInfo(IntPtr set, ref InterfaceData data,
        IntPtr detail, int bytes, out int required, ref DeviceInfoData device);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr set, ref DeviceInfoData device,
        uint property, out uint type, [Out] byte[] buffer, int size, out int required);
    [DllImport("setupapi.dll")]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr attributes,
        uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(SafeFileHandle file, IntPtr buffer, int count, IntPtr written, IntPtr operation);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CancelIoEx(SafeFileHandle file, IntPtr operation);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetOverlappedResult(SafeFileHandle file, IntPtr operation, out int written, bool wait);
}
