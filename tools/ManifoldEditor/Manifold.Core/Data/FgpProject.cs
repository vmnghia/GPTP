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

/// <summary>Imports the .fgp's <c>Buts</c> section: only the sets FireGraft changed.</summary>
public static class ButsImport
{
    /// <summary>Buts: u16 setCount, per set u8 setId, u8 buttonCount, 20-byte buttons.</summary>
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
    /// Turns FireGraft's sets into address-based sets. A set with an index that has no
    /// address, or a position outside 1-15, is left out and named in <paramref name="report"/>.
    /// The connected unit is not in Buts: it is taken from <paramref name="connectedUnits"/>.
    /// </summary>
    public static Dictionary<int, ButtonSet> ToSets(IReadOnlyList<FgpButtonSet> fgpSets,
        FunctionTable conditions, FunctionTable actions, Func<int, uint> connectedUnits,
        List<string> report)
    {
        var result = new Dictionary<int, ButtonSet>();
        foreach (var set in fgpSets)
        {
            if (set.SetId >= Card.SetCount) { report.Add($"set {set.SetId}: no such set"); continue; }
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
            if (problem is not null) { report.Add($"set {set.SetId}: {problem}; kept vanilla"); continue; }
            result[set.SetId] = new ButtonSet(buttons, connectedUnits(set.SetId));
        }
        return result;
    }
}
