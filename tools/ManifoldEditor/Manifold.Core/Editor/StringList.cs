using System.Globalization;
using System.Text;
using Manifold.Core.Data;

namespace Manifold.Core.Editor;

/// <summary>The Strings view's filters (spec §6).</summary>
public enum StringFilter { All, UsedByButtons, UnitNames, Edited }

/// <summary>One row of the Strings view: the id, the whole entry as PyTBL lists it, and its uses.</summary>
public sealed record StringRow(int Id, string Text, string Uses)
{
    public override string ToString() => Uses.Length == 0 ? $"{Id,5}   {Text}" : $"{Id,5}   {Text}      [{Uses}]";
}

/// <summary>A text code from PyTBL's reference (PyMS/FileFormats/TBL.py, TBL_REF), for the insert list.</summary>
public sealed record TblCode(int Code, string Meaning)
{
    public string Text => $"<{Code}>";
    public override string ToString() => $"<{Code}>  {Meaning}";
}

/// <summary>The Strings view's list, search and labels, and the strings a button can pick.</summary>
public static class StringList
{
    /// <summary>PyTBL's code reference, in-game meanings (spec §2).</summary>
    public static readonly IReadOnlyList<TblCode> Codes =
    [
        new(1, "Cyan (default)"), new(2, "Cyan"), new(3, "Yellow"), new(4, "White"), new(5, "Grey*"),
        new(6, "Red"), new(7, "Green"), new(8, "Red (Player 1)"), new(14, "Blue (Player 2)"),
        new(15, "Teal (Player 3)"), new(16, "Purple (Player 4)"), new(17, "Orange (Player 5)"),
        new(21, "Brown (Player 6)"), new(22, "White (Player 7)"), new(23, "Yellow (Player 8)"),
        new(24, "Green (Player 9)"), new(25, "Brighter Yellow (Player 10)"), new(26, "Cyan (Player 12)"),
        new(27, "Pinkish (Player 11)"), new(28, "Dark Cyan"), new(29, "Greygreen"), new(30, "Bluegrey"),
        new(31, "Turquoise"), new(11, "Invisible*"), new(20, "Invisible*"), new(12, "Truncate"),
        new(9, "Tab"), new(10, "Newline"), new(18, "Right Align"), new(19, "Center Align"),
        new(27, "Escape Key (as a hotkey)"), new(0, "End Substring"), new(35, "#"), new(60, "<"), new(62, ">"),
    ];

    public const string OverpowerNote = "* The game ignores colour codes after this one.";

    /// <summary>Where a string is used: "Siege Tank: 11, 12; Arbiter: 4 (disabled)"; "unit name" for ids 1-228.</summary>
    public static string UsesLabel(int id, IEnumerable<ButtonRef> refs, IReadOnlyList<ButtonSet> sets, Func<int, string> setName)
    {
        var parts = refs.GroupBy(r => r.SetId).Select(g =>
            $"{setName(g.Key)}: " + string.Join(", ", g.Select(r =>
                sets[r.SetId].Buttons[r.Index].Position + (r.Field == StringField.Disabled ? " (disabled)" : "")))).ToList();
        if (StringUses.UnitOf(id) is not null) parts.Insert(0, "unit name");
        return string.Join("; ", parts);
    }

    /// <summary>The whole entry as PyTBL lists it, without the final &lt;0&gt;.</summary>
    public static string EntryText(StringTable table, int id) => TblText.EditText(table.Segment(id));

    /// <summary>The entry's readable text: no codes, NULs as spaces, for searching by words.</summary>
    static string Plain(byte[] segment) =>
        new string(Encoding.Latin1.GetString(segment).Select(c => c == '\0' ? ' ' : c).Where(c => c >= ' ').ToArray());

    /// <summary>
    /// The rows that match, in id order. "#123" (or a plain number) goes to that id; other text
    /// matches the PyTBL text or the readable text, ignoring case unless asked.
    /// </summary>
    public static IReadOnlyList<StringRow> Rows(StringTable table, IReadOnlyList<ButtonSet> sets, string search,
        bool matchCase, StringFilter filter, Func<int, string> setName)
    {
        var uses = StringUses.All(sets);
        search = search.Trim();
        string number = search.StartsWith('#') ? search[1..] : search;
        bool byId = number.Length > 0 && int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out _);
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var rows = new List<StringRow>();
        for (int id = 1; id <= table.Count; id++)
        {
            bool keep = filter switch
            {
                StringFilter.UsedByButtons => uses[id].Any(),
                StringFilter.UnitNames => StringUses.UnitOf(id) is not null,
                StringFilter.Edited => table.IsEdited(id),
                _ => true,
            };
            if (!keep) continue;
            string text = EntryText(table, id);
            if (byId ? id.ToString(CultureInfo.InvariantCulture) != number.TrimStart('0')
                : search.Length > 0 && !text.Contains(search, comparison) && !Plain(table.Segment(id)).Contains(search, comparison))
                continue;
            rows.Add(new StringRow(id, text, UsesLabel(id, uses[id], sets, setName)));
        }
        return rows;
    }

    /// <summary>Whether any button shows the string when enabled: it is then a hotkey string.</summary>
    public static bool IsHotkeyString(IReadOnlyList<ButtonSet> sets, int id) =>
        StringUses.All(sets)[id].Any(r => r.Field == StringField.Enabled);
}
