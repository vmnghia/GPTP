using System.Text;

namespace Manifold.Core.Data;

/// <summary>
/// The strings of a .tbl (rez\stat_txt.tbl) as the editor shows them; the bytes are a
/// <see cref="StringTable"/>. Button string ids count from 1; 0 means none. Many button
/// strings are the hotkey, a NUL tooltip type, then the text (vanilla's Move is "m", NUL,
/// "\x03M\x01ove"); those are joined.
/// </summary>
public sealed class StatTxt
{
    StatTxt(StringTable table) => Table = table;

    /// <summary>The table these strings are read from (the one the editor edits).</summary>
    public StringTable Table { get; }

    public int Count => Table.Count;

    public static StatTxt Parse(byte[] bytes) => new(StringTable.Parse(bytes));

    public static StatTxt Of(StringTable table) => new(table);

    /// <summary>
    /// The string for a button's string id, or null for 0 or an id past the end: the first
    /// part of its segment, joined with the next part when the first NUL is the second byte
    /// (the hotkey, a NUL type, then the text).
    /// </summary>
    public string? Get(ushort id)
    {
        if (id < 1 || id > Table.Count) return null;
        var segment = Table.Segment(id);
        int end = EndOf(segment, 0);
        string text = Encoding.Latin1.GetString(segment, 0, end);
        if (end == 1 && segment.Length > 2)
            text += Encoding.Latin1.GetString(segment, 2, EndOf(segment, 2) - 2);
        return text;
    }

    /// <summary>
    /// A unit's name for lists: its name, then its subname in brackets unless it is "*"
    /// (unit strings are Name, NUL, Subname, NUL, StarEdit group: "Terran Siege Tank
    /// (Siege Mode)", "Edmund Duke (Siege Tank)"). Control codes are left out; null without a string.
    /// </summary>
    public string? UnitName(int unit)
    {
        if (unit < 0 || unit + 1 > Table.Count) return null;
        var parts = Encoding.Latin1.GetString(Table.Segment(unit + 1)).Split('\0')
            .Select(p => new string(p.Where(c => c >= ' ').ToArray()).Trim()).ToArray();
        string name = parts[0];
        if (parts.Length > 1 && parts[1].Length > 0 && parts[1] != "*") name += $" ({parts[1]})";
        return name;
    }

    static int EndOf(byte[] bytes, int start)
    {
        int end = Array.IndexOf(bytes, (byte)0, start);
        return end < 0 ? bytes.Length : end;
    }

    /// <summary>A button string's hotkey: its first character, as stat_txt stores it.</summary>
    public static char? HotkeyOf(string? buttonString) =>
        string.IsNullOrEmpty(buttonString) ? null : char.ToUpperInvariant(buttonString[0]);
}
