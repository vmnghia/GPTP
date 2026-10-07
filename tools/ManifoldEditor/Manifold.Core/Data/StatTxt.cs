using System.Text;

namespace Manifold.Core.Data;

/// <summary>
/// A .tbl string table (rez\stat_txt.tbl): u16 count, count u16 offsets, NUL-terminated
/// strings. Button string ids count from 1; 0 means none. Many button strings are stored as
/// the hotkey, a NUL, then the text (vanilla's Move is "m", NUL, "\x03M\x01ove"); those are
/// joined, unless another string starts right after the NUL.
/// </summary>
public sealed class StatTxt
{
    readonly string[] strings;

    StatTxt(string[] strings) => this.strings = strings;

    public int Count => strings.Length;

    static int EndOf(byte[] bytes, int start)
    {
        int end = Array.IndexOf(bytes, (byte)0, start);
        return end < 0 ? bytes.Length : end;
    }

    public static StatTxt Parse(byte[] bytes)
    {
        int count = BitConverter.ToUInt16(bytes, 0);
        var starts = new HashSet<int>();
        for (int i = 0; i < count; i++)
            starts.Add(BitConverter.ToUInt16(bytes, 2 + i * 2));
        var strings = new string[count];
        for (int i = 0; i < count; i++)
        {
            int start = BitConverter.ToUInt16(bytes, 2 + i * 2);
            int end = EndOf(bytes, start);
            string text = Encoding.Latin1.GetString(bytes, start, end - start);
            if (end == start + 1 && end + 1 < bytes.Length && !starts.Contains(end + 1))
                text += Encoding.Latin1.GetString(bytes, end + 1, EndOf(bytes, end + 1) - (end + 1));
            strings[i] = text;
        }
        return new StatTxt(strings);
    }

    /// <summary>The string for a button's string id, or null for 0 or an id past the end.</summary>
    public string? Get(ushort id) => id >= 1 && id <= strings.Length ? strings[id - 1] : null;

    /// <summary>A button string's hotkey: its first character, as stat_txt stores it.</summary>
    public static char? HotkeyOf(string? buttonString) =>
        string.IsNullOrEmpty(buttonString) ? null : char.ToUpperInvariant(buttonString[0]);
}
