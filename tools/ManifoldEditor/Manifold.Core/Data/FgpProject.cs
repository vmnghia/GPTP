using System.Text;

namespace Manifold.Core.Data;

/// <summary>
/// A FireGraft project (.fgp): "FgPa", u32 version, a NUL-terminated game version
/// string, then sections <c>char[4] tag, u32 length, data</c> to the end of the file.
/// </summary>
public sealed class FgpProject
{
    public uint Version { get; }
    public string GameVersion { get; }
    public IReadOnlyDictionary<string, byte[]> Sections { get; }

    FgpProject(uint version, string gameVersion, Dictionary<string, byte[]> sections)
    {
        Version = version;
        GameVersion = gameVersion;
        Sections = sections;
    }

    public static FgpProject Parse(byte[] bytes)
    {
        if (bytes.Length < 9 || Encoding.ASCII.GetString(bytes, 0, 4) != "FgPa")
            throw new InvalidDataException("not a FireGraft project");
        uint version = BitConverter.ToUInt32(bytes, 4);
        int nul = Array.IndexOf(bytes, (byte)0, 8);
        if (nul < 0) throw new InvalidDataException("no game version");
        string gameVersion = Encoding.ASCII.GetString(bytes, 8, nul - 8);
        var sections = new Dictionary<string, byte[]>();
        int at = nul + 1;
        while (at < bytes.Length)
        {
            if (at + 8 > bytes.Length) throw new InvalidDataException("section header cut short");
            string tag = Encoding.ASCII.GetString(bytes, at, 4);
            int length = checked((int)BitConverter.ToUInt32(bytes, at + 4));
            if (at + 8 + length > bytes.Length) throw new InvalidDataException($"section {tag} cut short");
            sections[tag] = bytes[(at + 8)..(at + 8 + length)];
            at += 8 + length;
        }
        return new FgpProject(version, gameVersion, sections);
    }
}

/// <summary>A button as FireGraft stores it: condition and action are list indexes.</summary>
public readonly record struct FgpButton(ushort Position, ushort Icon, uint ConditionIndex, uint ActionIndex,
    ushort ConditionVar, ushort ActionVar, ushort EnabledString, ushort DisabledString);

public sealed record FgpButtonSet(int SetId, IReadOnlyList<FgpButton> Buttons);

/// <summary>
/// One record of the .fgp's <c>Unit</c> section: which of FireGraft's own button sets the
/// game's set table entry (a unit id, or 228-249) uses.
/// </summary>
public sealed record FgpUnitLink(int Entry, int ButtonCount, int FgSet, ushort ConnectedUnit);

/// <summary>
/// Imports FireGraft's button sets. FireGraft numbers its sets its own way (its tree, in
/// the <c>SBut</c> section: "[11] Mixed Group", "[68] Marine/Firebat + Heroes", ...), not by
/// unit: <c>Buts</c> holds the sets it changed, by FireGraft's number, and <c>Unit</c> says
/// which FireGraft set each table entry uses (decoded 2026-10-08 from the user's project).
/// </summary>
public static class ButsImport
{
    /// <summary>Buts: u16 setCount, per set u8 FireGraft set, u8 buttonCount, 20-byte buttons.</summary>
    public static IReadOnlyList<FgpButtonSet> ParseButs(byte[] buts)
    {
        var result = new List<FgpButtonSet>();
        int count = BitConverter.ToUInt16(buts, 0);
        int at = 2;
        for (int s = 0; s < count; s++)
        {
            if (at + 2 > buts.Length) throw new InvalidDataException("Buts cut short");
            int setId = buts[at], n = buts[at + 1];
            at += 2;
            if (at + n * Button.Size > buts.Length) throw new InvalidDataException("Buts cut short");
            var buttons = new FgpButton[n];
            for (int i = 0; i < n; i++, at += Button.Size)
            {
                var raw = Button.Read(buts.AsSpan(at));
                buttons[i] = new FgpButton(raw.Position, raw.Icon, raw.Condition, raw.Action,
                    raw.ConditionVar, raw.ActionVar, raw.EnabledString, raw.DisabledString);
            }
            result.Add(new FgpButtonSet(setId, buttons));
        }
        if (at != buts.Length) throw new InvalidDataException("data after the last Buts set");
        return result;
    }

    /// <summary>
    /// Unit: u16 count, then per record u8 table entry, u8 button count, u8 FireGraft set + 1
    /// (0: none), u16 connected unit (0xFFFF: none), u8 n, then n three-byte items whose
    /// meaning is unknown (the entry again, then two bytes, mostly 01 00) and are skipped.
    /// </summary>
    public static IReadOnlyList<FgpUnitLink> ParseUnit(byte[] unit)
    {
        if (unit.Length < 2) throw new InvalidDataException("Unit cut short");
        int count = BitConverter.ToUInt16(unit, 0);
        var links = new List<FgpUnitLink>();
        int at = 2;
        for (int r = 0; r < count; r++)
        {
            if (at + 6 > unit.Length) throw new InvalidDataException("Unit cut short");
            int n = unit[at + 5];
            if (unit[at + 2] != 0)
                links.Add(new FgpUnitLink(unit[at], unit[at + 1], unit[at + 2] - 1, BitConverter.ToUInt16(unit, at + 3)));
            at += 6 + 3 * n;
            if (at > unit.Length) throw new InvalidDataException("Unit cut short");
        }
        if (at != unit.Length) throw new InvalidDataException("data after the last Unit record");
        return links;
    }

    /// <summary>
    /// FireGraft's changed sets as address-based buttons, by FireGraft's number. A set with
    /// an index that has no address, or a position outside 1-15, is left out and named in
    /// <paramref name="report"/>.
    /// </summary>
    public static Dictionary<int, IReadOnlyList<Button>> ToButtons(IReadOnlyList<FgpButtonSet> fgpSets,
        FunctionTable conditions, FunctionTable actions, List<string> report)
    {
        var result = new Dictionary<int, IReadOnlyList<Button>>();
        foreach (var set in fgpSets)
        {
            var buttons = new List<Button>();
            string? problem = null;
            foreach (var b in set.Buttons)
            {
                var condition = conditions.ByIndex(b.ConditionIndex);
                var action = actions.ByIndex(b.ActionIndex);
                if (condition is null) { problem = $"condition {b.ConditionIndex} has no address"; break; }
                if (action is null) { problem = $"action {b.ActionIndex} has no address"; break; }
                if (b.Position < 1 || b.Position > Card.MaxPosition) { problem = $"position {b.Position}"; break; }
                buttons.Add(new Button(b.Position, b.Icon, condition.Address, action.Address,
                    b.ConditionVar, b.ActionVar, b.EnabledString, b.DisabledString));
            }
            if (problem is not null) { report.Add($"FireGraft set {set.SetId}: {problem}; not imported"); continue; }
            result[set.SetId] = buttons;
        }
        return result;
    }

    /// <summary>
    /// The table entries FireGraft changed: each entry linked in <c>Unit</c> to a set in
    /// <c>Buts</c> gets a copy of that set's buttons (one set per unit). A link's connected
    /// unit wins, except 0xFFFF (none), which keeps vanilla's. Links to a set FireGraft
    /// didn't change, and changed sets no entry uses, are reported and leave vanilla alone.
    /// </summary>
    public static Dictionary<int, ButtonSet> ToSets(IReadOnlyDictionary<int, IReadOnlyList<Button>> fgSets,
        IReadOnlyList<FgpUnitLink> links, IReadOnlyList<ButtonSet> vanilla, List<string> report)
    {
        var result = new Dictionary<int, ButtonSet>();
        foreach (var link in links)
        {
            if (link.Entry >= Card.SetCount) { report.Add($"Unit: entry {link.Entry}: no such set"); continue; }
            if (!fgSets.TryGetValue(link.FgSet, out var buttons))
            {
                report.Add($"set {link.Entry}: uses FireGraft set {link.FgSet}, which FireGraft didn't change or couldn't be imported; kept vanilla");
                continue;
            }
            if (buttons.Count != link.ButtonCount)
                report.Add($"set {link.Entry}: FireGraft set {link.FgSet} has {buttons.Count} buttons, its Unit record says {link.ButtonCount}");
            uint connected = link.ConnectedUnit == 0xFFFF ? vanilla[link.Entry].ConnectedUnit : link.ConnectedUnit;
            result[link.Entry] = new ButtonSet(buttons.ToArray(), connected);
        }
        foreach (int fgSet in fgSets.Keys.Where(f => links.All(l => l.FgSet != f)).Order())
            report.Add($"FireGraft set {fgSet}: no unit uses it; not imported");
        return result;
    }

    /// <summary>The whole import: the changed table entries of a FireGraft project.</summary>
    public static Dictionary<int, ButtonSet> Import(FgpProject project, FunctionTable conditions, FunctionTable actions,
        IReadOnlyList<ButtonSet> vanilla, List<string> report)
    {
        if (!project.Sections.TryGetValue("Buts", out var buts)) return new();
        if (!project.Sections.TryGetValue("Unit", out var unit))
        {
            report.Add("the FireGraft project has button sets but no Unit section: they can't be placed; vanilla kept");
            return new();
        }
        var fgSets = ToButtons(ParseButs(buts), conditions, actions, report);
        return ToSets(fgSets, ParseUnit(unit), vanilla, report);
    }
}
