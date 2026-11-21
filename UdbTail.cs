using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PanoramaScreenMonitor;

internal sealed class UdbTail : IDisposable
{
    private const string Prefix = "[USB][Recv] RspPackage:";
    private const int MaxBuffered = 128 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    internal static readonly string DefaultDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "kanali", "logs");
    private static string CurrentFileName => $"udb_{DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.log";
    internal static bool HasCurrentLog() => File.Exists(Path.Combine(DefaultDirectory, CurrentFileName));

    private sealed class Cursor(FileStream file)
    {
        public FileStream File { get; } = file;
        public FileIdentity Identity { get; } = FileIdentity.Read(file.SafeFileHandle);
        public MemoryStream Line { get; } = new();
        public StringBuilder Response { get; } = new();
        public bool DroppingLine { get; set; }
        public bool Receiving { get; set; }

        public void Reset()
        {
            Line.SetLength(0);
            Response.Clear();
            DroppingLine = false;
            Receiving = false;
        }
    }

    private readonly string directory;
    private readonly Dictionary<string, Cursor> current = [];
    private readonly List<Cursor> cursors = [];
    private readonly ulong track;
    private bool sawFile;

    internal UdbTail(ulong track, string? directory = null)
    {
        this.track = track;
        this.directory = directory ?? DefaultDirectory;
        Discover(skipExisting: true);
    }

    private void Discover(bool skipExisting)
    {
        string name = CurrentFileName;
        FileStream file;
        try
        {
            file = new FileStream(Path.Combine(directory, name), FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
        }
        catch (FileNotFoundException) { return; }
        catch (DirectoryNotFoundException) { return; }

        var cursor = new Cursor(file);
        if (current.TryGetValue(name, out var previous) && previous.Identity == cursor.Identity)
        {
            cursor.File.Dispose();
            cursor.Line.Dispose();
            return;
        }

        sawFile = true;
        if (skipExisting) file.Seek(0, SeekOrigin.End);
        current[name] = cursor;
        cursors.Add(cursor);
    }

    internal async Task WaitForFileAsync(CancellationToken token)
    {
        var elapsed = Stopwatch.StartNew();
        while (!sawFile && elapsed.ElapsedMilliseconds < 2500)
        {
            await Task.Delay(50, token).ConfigureAwait(false);
            Discover(skipExisting: true);
        }
        if (!sawFile) throw new TimeoutException("Ожидание журнала UDB. USB-команда не отправлена.");
    }

    internal Protocol.Block? Poll(Func<Protocol.Block, bool> accepts)
    {
        Discover(skipExisting: false);
        Span<byte> bytes = stackalloc byte[4096];
        foreach (var cursor in cursors)
        {
            if (cursor.File.Length < cursor.File.Position)
            {
                cursor.File.Position = 0;
                cursor.Reset();
            }
            // Bound each pass even when UDB is writing continuously.
            for (int chunk = 0; chunk < 16; chunk++)
            {
                int count = cursor.File.Read(bytes);
                if (count == 0) break;
                if (Consume(cursor, bytes[..count], accepts) is { } match) return match;
            }
        }
        return null;
    }

    private Protocol.Block? Consume(Cursor cursor, ReadOnlySpan<byte> bytes, Func<Protocol.Block, bool> accepts)
    {
        foreach (byte value in bytes)
        {
            if (value != '\n')
            {
                if (cursor.DroppingLine) continue;
                if (cursor.Line.Length < MaxBuffered) cursor.Line.WriteByte(value);
                else
                {
                    cursor.Reset();
                    cursor.DroppingLine = true;
                }
                continue;
            }

            string line;
            try { line = Utf8.GetString(cursor.Line.GetBuffer(), 0, (int)cursor.Line.Length).TrimEnd('\r'); }
            catch (DecoderFallbackException) { cursor.Reset(); continue; }
            cursor.Line.SetLength(0);
            if (cursor.DroppingLine) { cursor.Reset(); continue; }

            int marker = line.IndexOf(Prefix, StringComparison.Ordinal);
            if (marker >= 0 && line[(marker + Prefix.Length)..].TrimStart().StartsWith("header", StringComparison.Ordinal))
            {
                cursor.Response.Clear();
                cursor.Receiving = true;
                line = line[(marker + Prefix.Length)..];
            }
            if (!cursor.Receiving) continue;
            if (cursor.Response.Length + line.Length > MaxBuffered)
            {
                cursor.Reset();
                continue;
            }

            cursor.Response.AppendLine(line);
            // Protobuf's log representation puts the body after header and error.
            if (!line.EndsWith('}') || (line.Length > 1 && char.IsWhiteSpace(line[0]))) continue;
            var response = Protocol.Parse(cursor.Response.ToString());
            if (response?.Child("header")?.Value("track_id") is { Quoted: false } id
                && ulong.TryParse(id.Text, NumberStyles.None, CultureInfo.InvariantCulture, out ulong actual)
                && actual == track && accepts(response))
                return response;
        }
        return null;
    }

    internal async Task<Protocol.Block> WaitAsync(Func<Protocol.Block, bool> accepts, CancellationToken token)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.ElapsedMilliseconds < 2500)
        {
            token.ThrowIfCancellationRequested();
            if (Poll(accepts) is { } match) return match;
            await Task.Delay(50, token).ConfigureAwait(false);
        }
        throw new TimeoutException(sawFile
            ? "UDB не подтвердил команду за 2,5 с."
            : "Журнал UDB не найден. Проверьте, что KANALI запущен.");
    }

    public void Dispose()
    {
        foreach (var cursor in cursors)
        {
            cursor.File.Dispose();
            cursor.Line.Dispose();
        }
    }

    private readonly record struct FileIdentity(uint Volume, uint IndexHigh, uint IndexLow)
    {
        internal static FileIdentity Read(SafeFileHandle file)
        {
            if (!GetFileInformationByHandle(file, out var info))
                throw new IOException("Не удалось определить экземпляр журнала UDB.");
            return new(info.Volume, info.IndexHigh, info.IndexLow);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInfo
    {
        public uint Attributes, CreatedLow, CreatedHigh, AccessLow, AccessHigh, WriteLow, WriteHigh,
            Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInfo info);
}
