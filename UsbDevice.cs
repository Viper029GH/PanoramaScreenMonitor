using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace PanoramaScreenMonitor;

internal sealed class UsbInterfaceException(string message) : IOException(message);

internal static class UsbDevice
{
    internal static string Find()
    {
        var printer = new Guid("28d78fad-5a12-11d1-ae5b-0000f803a8c2");
        string[] matches = Interfaces(printer)
            .Where(path => Regex.IsMatch(path, @"vid_391a&pid_1011(?:[&#]|$)", RegexOptions.IgnoreCase))
            .ToArray();
        if (matches.Length == 1) return matches[0];
        if (matches.Length > 1) throw new UsbInterfaceException("Найдено несколько интерфейсов RK PANO (391A:1011).");

        var usb = new Guid("a5dcbf10-6530-11d2-901f-00c04fb951ed");
        if (Interfaces(usb).Any(path => Regex.IsMatch(path, @"vid_391a&pid_(?!1011(?:[&#]|$))",
            RegexOptions.IgnoreCase)))
            throw new UsbInterfaceException("Обнаружено другое устройство TRYX. Поддерживается RK PANO (391A:1011).");
        throw new UsbInterfaceException("Устройство RK PANO (391A:1011) не найдено.");
    }

    private static List<string> Interfaces(Guid guid)
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
                    if (!SetupDiGetDeviceInterfaceDetailW(set, ref data, detail, bytes, out _, IntPtr.Zero))
                        throw NativeError("Не удалось получить путь USB");
                    paths.Add(Marshal.PtrToStringUni(detail + 4)!);
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
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
