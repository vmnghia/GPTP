using System.Text;

namespace Manifold.Core.Data;

/// <summary>
/// A .tbl string table (rez\stat_txt.tbl): u16 count, count u16 offsets, NUL-terminated
/// strings. Button string ids count from 1; 0 means none.
/// </summary>
public sealed class StatTxt
{
    readonly string[] strings;

    StatTxt(string[] strings) => this.strings = strings;

    public int Count => strings.Length;

    public static StatTxt Parse(byte[] bytes)
    {
        int count = BitConverter.ToUInt16(bytes, 0);
        var strings = new string[count];
        for (int i = 0; i < count; i++)
        {
            int start = BitConverter.ToUInt16(bytes, 2 + i * 2);
            int end = Array.IndexOf(bytes, (byte)0, start);
            if (end < 0) end = bytes.Length;
            strings[i] = Encoding.Latin1.GetString(bytes, start, end - start);
        }
        return new StatTxt(strings);
    }

    /// <summary>The string for a button's string id, or null for 0 or an id past the end.</summary>
    public string? Get(ushort id) => id >= 1 && id <= strings.Length ? strings[id - 1] : null;

    /// <summary>A button string's hotkey: its first character, as stat_txt stores it.</summary>
    public static char? HotkeyOf(string? buttonString) =>
        string.IsNullOrEmpty(buttonString) ? null : char.ToUpperInvariant(buttonString[0]);
}
