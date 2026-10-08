using System.Text;

namespace Manifold.Core.Data;

/// <summary>
/// String bytes as text, written as PyMS's PyTBL writes them (TBL.py's decompile_string and
/// compile_string): bytes 0-31, '#', '&lt;' and '&gt;' as &lt;N&gt; (decimal), every other
/// byte as its Latin-1 character. A string copied from PyTBL pastes unchanged.
/// </summary>
public static class TblText
{
    static bool Coded(byte b) => b < 32 || b == '#' || b == '<' || b == '>';

    public static string Decompile(ReadOnlySpan<byte> bytes)
    {
        var text = new StringBuilder(bytes.Length);
        foreach (byte b in bytes)
        {
            if (Coded(b)) text.Append('<').Append(b).Append('>');
            else text.Append((char)b);
        }
        return text.ToString();
    }

    /// <summary>
    /// The bytes of a text: &lt;N&gt; with N from 0 to 255 is that byte, anything else is
    /// literal. Null, with the reason, when a character has no byte (above U+00FF).
    /// </summary>
    public static byte[]? Compile(string text, out string? error)
    {
        var bytes = new List<byte>(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '<' && Code(text, i) is (byte value, int length))
            {
                bytes.Add(value);
                i += length - 1;
                continue;
            }
            if (c > 0xFF)
            {
                error = $"'{c}' can't be stored: stat_txt holds one byte per character (Latin-1)";
                return null;
            }
            bytes.Add((byte)c);
        }
        error = null;
        return bytes.ToArray();
    }

    /// <summary>A code &lt;digits&gt; at i whose value is 0-255 (PyTBL's int() allows leading zeros).</summary>
    static (byte Value, int Length)? Code(string text, int i)
    {
        int j = i + 1;
        int value = 0;
        while (j < text.Length && char.IsAsciiDigit(text[j]))
        {
            value = Math.Min(value * 10 + (text[j] - '0'), 256);
            j++;
        }
        if (j == i + 1 || j >= text.Length || text[j] != '>' || value > 255) return null;
        return ((byte)value, j - i + 1);
    }

    /// <summary>A segment as the editor shows it: PyTBL's text without the final &lt;0&gt;.</summary>
    public static string EditText(ReadOnlySpan<byte> segment) =>
        Decompile(segment.Length > 0 && segment[^1] == 0 ? segment[..^1] : segment);
}

/// <summary>
/// A hotkey string (a button's enabled string, field 0x10; PyTBL): the hotkey, the tooltip
/// type, then the text. <see cref="Text"/> is without the final NUL.
/// </summary>
public sealed record HotkeyString(byte Hotkey, byte Type, byte[] Text)
{
    /// <summary>PyTBL's tooltip types: what the tooltip shows under the text.</summary>
    public static readonly IReadOnlyList<string> TypeLabels =
    [
        "Label only, no costs",
        "Minerals, gas, supply (units, buildings)",
        "Upgrade costs",
        "Energy (spells)",
        "Minerals, gas (technology research)",
        "Minerals, gas, no supply (Guardian, Devourer Aspect)",
    ];

    public static string TypeLabel(byte type) =>
        type < TypeLabels.Count ? $"<{type}> {TypeLabels[type]}" : $"<{type}> (not a PyTBL type)";

    /// <summary>The parts of a segment, or null when it is too short to hold a hotkey and a type.</summary>
    public static HotkeyString? Split(byte[] segment)
    {
        if (segment.Length < 2 || segment[0] == 0) return null;
        int end = segment[^1] == 0 ? segment.Length - 1 : segment.Length;
        return new HotkeyString(segment[0], segment[1], segment[2..Math.Max(2, end)]);
    }

    public byte[] Join() => [Hotkey, Type, .. Text, 0];
}
