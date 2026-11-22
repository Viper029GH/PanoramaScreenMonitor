using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PanoramaScreenMonitor;

internal static class Protocol
{
    private static readonly string[] Sections =
        ["poweron_config", "standby_config", "work_config", "filter_config", "display_config"];

    private static readonly string[][] Fields =
    [
        ["media_file:s"],
        ["enable:b", "media_file:s"],
        ["media_mode:MEDIA_SINGLE|MEDIA_DUAL|MEDIA_KALEIDOSCOPE",
         "loop_mode:LOOP_SINGLE|LOOP_ALL|LOOP_RANDOM", "single_mode_media_file:s",
         "dual_mode_left_media_file:s", "dual_mode_right_media_file:s", "kaleidoscope_media_file:s",
         "kaleidoscope_source:KALEIDOSCOPE_LEFT|KALEIDOSCOPE_RIGHT"],
        ["enable:b", "filter_file:s", "alpha:u", "dual_mode_left_file:s", "dual_mode_right_file:s",
         "dual_mode_left_alpha:u", "dual_mode_right_alpha:u", "filter_mode:FILTER_SINGLE|FILTER_DUAL"],
        ["backlight_enable:b", "backlight_brightness:u", "mirror:b", "ui_rotation:u", "media_rotation:u"]
    ];

    internal sealed record Scalar(string Text, bool Quoted);

    internal sealed class Block : Dictionary<string, object>
    {
        public Block? Child(string key) => this.GetValueOrDefault(key) as Block;
        public Scalar? Value(string key) => this.GetValueOrDefault(key) as Scalar;
    }

    internal static bool Error(Block response)
    {
        if (!response.TryGetValue("error", out var value)) return false;
        if (value is not Block error) return true;
        if (error.Count == 0) return false;
        return error.Value("code") is not { Quoted: false, Text: "0" or "SUCCESS" or "Success" or "OK" };
    }

    internal static bool Backlight(Block config)
    {
        var display = config.Child("display_config")
            ?? throw new IOException("В ответе UDB отсутствует блок display_config.");
        return display.TryGetValue("backlight_enable", out var field)
            && ReadBool(field) != 0;
    }

    internal static uint Brightness(Block config)
    {
        var display = config.Child("display_config")
            ?? throw new IOException("В ответе UDB отсутствует блок display_config.");
        if (!display.TryGetValue("backlight_brightness", out var value)) return 0;
        if (value is Scalar { Quoted: false } scalar
            && uint.TryParse(scalar.Text, NumberStyles.None, CultureInfo.InvariantCulture, out uint brightness))
            return brightness;
        throw new IOException("Некорректное значение яркости в ответе UDB.");
    }

    internal static DeviceInfo ReadDeviceInfo(Block info) => new(
        ReadText(info, "product_name"), ReadText(info, "firmware_version"), ReadText(info, "app_version"),
        string.Join(" · ", new[] { ReadText(info, "os_name"), ReadText(info, "os_version") }
            .Where(value => !string.IsNullOrWhiteSpace(value))));

    private static string? ReadText(Block info, string key)
    {
        if (info.Value(key) is not { Quoted: true } value) return null;
        try
        {
            string decoded = Encoding.UTF8.GetString(Unescape(value.Text));
            return new string(decoded.Where(character => !char.IsControl(character)).Take(120).ToArray()).Trim();
        }
        catch (Exception ex) when (ex is IOException or DecoderFallbackException) { return null; }
    }

    private static uint ReadBool(object field) => field switch
    {
        Scalar { Quoted: false, Text: "true" } => 1,
        Scalar { Quoted: false, Text: "false" } => 0,
        _ => throw new IOException("Неизвестный формат логического поля конфигурации.")
    };

    internal static byte[] Configuration(Block config, bool? enabled, uint? brightness = null)
    {
        using var result = new MemoryStream();
        foreach (var (name, value) in config)
        {
            int section = Array.IndexOf(Sections, name);
            if (section < 0 || value is not Block block)
                throw new IOException("Неизвестный блок в конфигурации UDB. Запись отменена.");
            using var body = new MemoryStream();
            foreach (var (key, field) in block)
            {
                var specs = Fields[section];
                int index = Array.FindIndex(specs, spec => spec.StartsWith(key + ":", StringComparison.Ordinal));
                if (index < 0 || field is not Scalar scalar)
                    throw new IOException($"Неизвестное поле в {name}. Запись отменена.");
                string type = specs[index][(key.Length + 1)..];
                if (section == 4 && index == 0 && enabled.HasValue)
                {
                    _ = ReadBool(scalar);
                    continue;
                }
                if (type == "s" && scalar.Quoted)
                    Bytes(body, index + 1, Unescape(scalar.Text));
                else if (type == "b")
                    Number(body, index + 1, ReadBool(scalar));
                else if (!scalar.Quoted && type == "u" && uint.TryParse(scalar.Text, NumberStyles.None,
                    CultureInfo.InvariantCulture, out uint number))
                {
                    if (section != 4 || index != 1 || !brightness.HasValue)
                        Number(body, index + 1, number);
                }
                else if (!scalar.Quoted && type.Contains('|'))
                {
                    // These values are defined by the LoopMode descriptor in KANALI's UDB.
                    int label = (name, key, scalar.Text) switch
                    {
                        ("work_config", "loop_mode", "LoopMode_Single") => 0,
                        ("work_config", "loop_mode", "LoopMode_MediaAll") => 1,
                        ("work_config", "loop_mode", "LoopMode_MediaRand") => 2,
                        ("work_config", "loop_mode", "LoopMode_PresetAll") => 3,
                        ("work_config", "loop_mode", "LoopMode_PresetRand") => 4,
                        _ => Array.IndexOf(type.Split('|'), scalar.Text)
                    };
                    if (label >= 0) Number(body, index + 1, (uint)label);
                    else if (int.TryParse(scalar.Text, NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture, out int numeric))
                        Number(body, index + 1, unchecked((ulong)(long)numeric));
                    else throw new IOException($"Неизвестное значение {name}.{key}: «{scalar.Text[..Math.Min(64, scalar.Text.Length)]}». Запись отменена.");
                }
                else throw new IOException($"Неподдерживаемый формат {name}.{key}. Запись отменена.");
            }
            if (section == 4 && enabled == true) Number(body, 1, 1);
            if (section == 4 && brightness is > 0) Number(body, 2, brightness.Value);
            Bytes(result, section + 1, body.ToArray());
        }
        return result.ToArray();
    }

    private static byte[] Unescape(string quoted)
    {
        using var bytes = new MemoryStream();
        var utf8 = new UTF8Encoding(false, true);
        int start = 1;
        int end = quoted.Length - 1;
        for (int i = start; i < end; i++)
        {
            if (quoted[i] != '\\') continue;
            bytes.Write(utf8.GetBytes(quoted[start..i]));
            char escape = quoted[++i];
            if (escape is >= '0' and <= '7')
            {
                int value = escape - '0';
                for (int count = 1; count < 3 && i + 1 < end && quoted[i + 1] is >= '0' and <= '7'; count++)
                    value = (value * 8) + quoted[++i] - '0';
                if (value > 255) throw new IOException("Некорректное экранирование строки UDB.");
                bytes.WriteByte((byte)value);
            }
            else
            {
                int value = escape switch
                {
                    'a' => 7, 'b' => 8, 'f' => 12, 'n' => 10, 'r' => 13, 't' => 9, 'v' => 11,
                    '\\' => '\\', '"' => '"', '\'' => '\'',
                    _ => throw new IOException("Неизвестное экранирование строки UDB. Запись отменена.")
                };
                bytes.WriteByte((byte)value);
            }
            start = i + 1;
        }
        bytes.Write(utf8.GetBytes(quoted[start..end]));
        byte[] result = bytes.ToArray();
        _ = utf8.GetCharCount(result);
        return result;
    }

    internal static byte[] Frame(ulong track, int command, byte[] payload)
    {
        using var header = new MemoryStream();
        Number(header, 1, 1);
        Number(header, 2, track);
        using var frame = new MemoryStream();
        frame.Write("TRYX"u8);
        frame.Write(new byte[4]);
        Bytes(frame, 1, header.ToArray());
        Bytes(frame, command, payload);
        byte[] result = frame.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)result.Length - 8);
        return result;
    }

    private static void Varint(Stream stream, ulong value)
    {
        while (value >= 128)
        {
            stream.WriteByte((byte)(value | 128));
            value >>= 7;
        }
        stream.WriteByte((byte)value);
    }

    private static void Number(Stream stream, int field, ulong value)
    {
        Varint(stream, (ulong)(field << 3));
        Varint(stream, value);
    }

    private static void Bytes(Stream stream, int field, byte[] value)
    {
        Varint(stream, (ulong)((field << 3) | 2));
        Varint(stream, (ulong)value.Length);
        stream.Write(value);
    }

    private static readonly Regex Token = new(
        """\G\s*(?:"(?:[^"\\\r\n]|\\[^\r\n])*"|[a-zA-Z_][a-zA-Z_0-9]*|-?[0-9]+|[{}:])""",
        RegexOptions.CultureInvariant);

    internal static Block? Parse(string text)
    {
        int position = 0;
        try { return ParseBlock(text, ref position, 0, nested: false); }
        catch (FormatException) { return null; }
    }

    private static Block ParseBlock(string text, ref int position, int depth, bool nested)
    {
        if (depth > 12) throw new FormatException();
        var block = new Block();
        while (position < text.Length && !text.AsSpan(position).Trim().IsEmpty)
        {
            string name = Next(text, ref position);
            if (name == "}" && nested) return block;
            if (!char.IsAsciiLetter(name[0]) && name[0] != '_') throw new FormatException();
            string separator = Next(text, ref position);
            bool hasColon = separator == ":";
            if (hasColon) separator = Next(text, ref position);
            object value = separator switch
            {
                "{" => ParseBlock(text, ref position, depth + 1, nested: true),
                "}" or ":" => throw new FormatException(),
                _ when hasColon => new Scalar(separator, separator[0] == '"'),
                _ => throw new FormatException()
            };
            if (!block.TryAdd(name, value)) throw new FormatException();
        }
        if (nested) throw new FormatException();
        return block;
    }

    private static string Next(string text, ref int position)
    {
        var match = Token.Match(text, position);
        if (!match.Success) throw new FormatException();
        position += match.Length;
        return match.Value.TrimStart();
    }
}
