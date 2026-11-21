using System.Globalization;
using System.Text;

namespace PanoramaScreenMonitor;

internal sealed class UiLanguage
{
    internal sealed record Choice(string Code, string Name)
    {
        public override string ToString() => Name;
    }
    internal static readonly Choice[] Choices =
    [
        new("ru", "Русский"), new("en", "English"), new("de", "Deutsch"), new("fr", "Français"),
        new("es", "Español"), new("pt-BR", "Português (Brasil)"), new("zh-CN", "简体中文"), new("ja", "日本語")
    ];

    private static readonly Dictionary<string, string[]> Translations = ReadTranslations();
    private readonly int column;
    internal CultureInfo Culture { get; }

    internal UiLanguage(string? code)
    {
        string normalized = Normalize(code);
        column = Array.FindIndex(Choices, item => item.Code == normalized);
        Culture = CultureInfo.GetCultureInfo(normalized);
    }

    internal static string Normalize(string? code) => Choices.FirstOrDefault(item => item.Code == code)?.Code ?? "ru";
    internal string Text(string text) => Translations.TryGetValue(text, out var values) ? values[column] : text;
    internal string Error(string details, string fallback) =>
        column == 0 || Translations.ContainsKey(details) ? Text(details) : Text(fallback);

    private static Dictionary<string, string[]> ReadTranslations()
    {
        using var stream = typeof(UiLanguage).Assembly.GetManifestResourceStream("PanoramaScreenMonitor.Translations.txt")
            ?? throw new InvalidOperationException("Translations are missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var translations = new Dictionary<string, string[]>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            string[] parts = line.Split('|');
            if (parts.Length != Choices.Length) throw new InvalidDataException($"Invalid translation: {parts[0]}");
            translations.Add(parts[0], parts);
        }
        return translations;
    }
}
