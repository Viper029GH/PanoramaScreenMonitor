using System.Text.Json;
using Microsoft.Win32;

namespace PanoramaScreenMonitor;

internal sealed record Preferences(int Minutes = 10, bool AutoStart = false, bool StartHidden = false,
    bool SyncWindowsDisplayTimeout = false, string Language = "ru", InterfaceTheme Theme = InterfaceTheme.System,
    string? SelectedDevice = null, string[]? ExperimentalAutoDevices = null);

internal sealed class Profile(bool demo, string? directory = null)
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "PanoramaScreenMonitor";
    internal string DirectoryPath { get; } = directory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        demo ? "PanoramaScreenMonitor.Demo" : "PanoramaScreenMonitor");

    private string SettingsPath => Path.Combine(DirectoryPath, "settings.json");

    internal Preferences Load()
    {
        Preferences settings;
        try
        {
            settings = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(SettingsPath)) ?? new();
            settings = settings with { Minutes = Math.Clamp(settings.Minutes, 1, 1440) };
        }
        catch (FileNotFoundException) { settings = new(); }
        catch (DirectoryNotFoundException) { settings = new(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log("Не удалось прочитать настройки.");
            settings = new();
        }
        if (demo) return settings with { AutoStart = false };
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return settings with { AutoStart = key?.GetValue(RunName) is string };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log("Не удалось прочитать настройку автозапуска.");
            return settings;
        }
    }

    internal void Save(Preferences settings)
    {
        Directory.CreateDirectory(DirectoryPath);
        string temp = SettingsPath + ".new";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(settings));
            if (!demo)
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                var old = key.GetValue(RunName) as string;
                UpdateRunKey(key, settings.AutoStart ? $"\"{Environment.ProcessPath}\"" : null);
                try { File.Move(temp, SettingsPath, overwrite: true); }
                catch
                {
                    UpdateRunKey(key, old);
                    throw;
                }
            }
            else File.Move(temp, SettingsPath, overwrite: true);
        }
        finally { File.Delete(temp); }
    }

    private static void UpdateRunKey(RegistryKey key, string? value)
    {
        if (value is null) key.DeleteValue(RunName, throwOnMissingValue: false);
        else key.SetValue(RunName, value);
    }

    internal void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            string file = Path.Combine(DirectoryPath, "monitor.log");
            if (File.Exists(file) && new FileInfo(file).Length > 128 * 1024)
                File.Move(file, file + ".1", overwrite: true);
            File.AppendAllText(file, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}  {message}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
