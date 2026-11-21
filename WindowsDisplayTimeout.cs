using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PanoramaScreenMonitor;

internal static class WindowsDisplayTimeout
{
    private static readonly Guid Display = new("7516b95f-f776-4464-8c53-06167f40cc99");
    private static readonly Guid VideoIdle = new("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e");

    // VIDEOIDLE uses seconds, with zero meaning that idle never turns the display off.
    internal static uint? ReadSeconds()
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out IntPtr scheme) != 0) return null;
        try
        {
            uint seconds = 0;
            uint result = SystemInformation.PowerStatus.PowerLineStatus switch
            {
                PowerLineStatus.Online => PowerReadACValueIndex(IntPtr.Zero, scheme, in Display, in VideoIdle, out seconds),
                PowerLineStatus.Offline => PowerReadDCValueIndex(IntPtr.Zero, scheme, in Display, in VideoIdle, out seconds),
                _ => 1
            };
            return result == 0 ? seconds : null;
        }
        finally { LocalFree(scheme); }
    }

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
    [DllImport("powrprof.dll")]
    private static extern uint PowerReadACValueIndex(IntPtr root, IntPtr scheme, in Guid subgroup,
        in Guid setting, out uint seconds);
    [DllImport("powrprof.dll")]
    private static extern uint PowerReadDCValueIndex(IntPtr root, IntPtr scheme, in Guid subgroup,
        in Guid setting, out uint seconds);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
