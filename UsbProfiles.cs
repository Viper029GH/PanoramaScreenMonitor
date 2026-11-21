using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PanoramaScreenMonitor;

internal sealed record UsbProfile(string Id, string Name, params string[] Products)
{
    internal bool Experimental => Id != "391A:1011";

    internal bool MatchesProduct(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        string normalized = new(name.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        return Products.Contains(normalized, StringComparer.Ordinal);
    }
}

internal sealed record UsbCandidate(UsbProfile Profile, string Key, string Path, string? WindowsName)
{
    public override string ToString() => $"{Profile.Name} · {Profile.Id}";
}

internal sealed record UsbInventory(UsbCandidate[] Candidates, string[] MissingPrinter, string[] Unknown)
{
    internal static readonly UsbInventory Empty = new([], [], []);
}

internal static class UsbProfiles
{
    internal static readonly UsbProfile[] All =
    [
        new("391A:1011", "Panorama", "PANO", "PANORAMA", "TRYXPANORAMA"),
        new("391A:1021", "Panorama SE", "PASE", "PANORAMASE", "TRYXPANORAMASE"),
        new("391A:1061", "Panorama SE", "PASE", "PANORAMASE", "TRYXPANORAMASE"),
        new("391A:1031", "Panorama WB", "PAWB", "PANORAMAWB", "TRYXPANORAMAWB", "PANO", "PANORAMA"),
        new("391A:10A1", "Panorama WB", "PAWB", "PANORAMAWB", "TRYXPANORAMAWB"),
        new("391A:1071", "Panorama SE V2", "PASEV2", "PANORAMASEV2", "TRYXPANORAMASEV2"),
        new("391A:1081", "Panorama V2", "PANOV2", "PANORAMAV2", "TRYXPANORAMAV2"),
        new("391A:10B1", "Panorama", "PANO", "PANORAMA", "PANOV2", "PANORAMAV2", "TRYXPANORAMAV2"),
        new("391A:1091", "Panorama WB V2", "PAWBV2", "PANORAMAWBV2", "TRYXPANORAMAWBV2")
    ];

    private static readonly Dictionary<string, UsbProfile> ById = All.ToDictionary(profile => profile.Id);
    private static readonly Regex IdPattern = new(
        @"^[^#]*\bUSB#VID_391A&PID_([0-9A-F]{4})(?:&[^#]*)?#",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static string? ReadId(string path)
    {
        var match = IdPattern.Match(path);
        return match.Success ? $"391A:{match.Groups[1].Value.ToUpperInvariant()}" : null;
    }

    internal static UsbInventory Classify(IEnumerable<string> printerPaths, IEnumerable<string> usbPaths,
        IReadOnlyDictionary<string, string?>? names = null)
    {
        var printers = printerPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var candidates = new List<UsbCandidate>();
        foreach (string path in printers)
        {
            if (ReadId(path) is not { } id || !ById.TryGetValue(id, out var profile)) continue;
            // A local fingerprint allows exact matching without exposing the device instance string.
            string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())));
            candidates.Add(new(profile, key, path, names?.GetValueOrDefault(path)));
        }

        var printerInstances = printers.Select(InterfaceInstance).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingPrinter = usbPaths.Where(path => !printerInstances.Contains(InterfaceInstance(path)))
            .Select(ReadId).OfType<string>().Where(ById.ContainsKey).Distinct().Order().ToArray();
        var unknown = printers.Concat(usbPaths).Select(ReadId).OfType<string>()
            .Where(id => !ById.ContainsKey(id)).Distinct().Order().ToArray();
        return new(candidates.OrderBy(candidate => candidate.Profile.Id).ThenBy(candidate => candidate.Key).ToArray(),
            missingPrinter, unknown);
    }

    private static string InterfaceInstance(string path) =>
        path.LastIndexOf("#{", StringComparison.Ordinal) is int end && end >= 0 ? path[..end] : path;

    internal static UsbCandidate? Select(UsbInventory devices, string? savedKey, ref string? automaticKey)
    {
        if (savedKey is not null) return devices.Candidates.SingleOrDefault(device => device.Key == savedKey);
        if (devices.Candidates.Length != 1) return null;
        var candidate = devices.Candidates[0];
        automaticKey ??= candidate.Key;
        return candidate.Key == automaticKey ? candidate : null;
    }
}
