using Manifold.Core.Data;

namespace Manifold.Core.Editor;

/// <summary>
/// One place a value points at an entry (PyDAT's DATRef): the shown file <paramref name="Target"/>
/// is used by <paramref name="Property"/> of <paramref name="From"/>, called <paramref name="Label"/>
/// in PyDAT's list. With <paramref name="RangeEnd"/> the field is a range of entries,
/// <paramref name="Property"/> to <paramref name="RangeEnd"/>, both ends included.
/// </summary>
public sealed record DatRef(DatId Target, DatId From, string Property, string Label,
    DatTabId? SubTab = null, string? RangeEnd = null);

/// <summary>One line of the "Used by" list: an entry of <paramref name="File"/>, the field that points and the sub-tab it sits on.</summary>
public sealed record DatRefMatch(DatId File, int Entry, string Label, DatTabId? SubTab);

/// <summary>
/// PyDAT's "Used by" table (PyMS/PyDAT/*Tab.py setup_used_by, MIT) and its matching
/// (PyDAT/DATRef.py). The labels are PyDAT's, "Attach Unit" included.
/// </summary>
public static class DatRefs
{
    static DatRef R(DatId target, DatId from, string property, string label, DatTabId? subTab = null, string? rangeEnd = null) =>
        new(target, from, property, label, subTab, rangeEnd);

    public static IReadOnlyList<DatRef> All { get; } =
    [
        R(DatId.Units, DatId.Units, "subunit1", "Subunit 1", DatTabId.UnitsAdvanced),
        R(DatId.Units, DatId.Units, "subunit2", "Subunit 2", DatTabId.UnitsAdvanced),
        R(DatId.Units, DatId.Units, "infestation", "Infestation", DatTabId.UnitsAdvanced),

        R(DatId.Weapons, DatId.Units, "ground_weapon", "Ground Weapon", DatTabId.UnitsBasic),
        R(DatId.Weapons, DatId.Units, "air_weapon", "Air Weapon", DatTabId.UnitsBasic),
        R(DatId.Weapons, DatId.Orders, "targeting", "Targeting"),

        R(DatId.Flingy, DatId.Units, "graphics", "Graphics", DatTabId.UnitsGraphics),
        R(DatId.Flingy, DatId.Weapons, "graphics", "Graphics"),

        R(DatId.Sprites, DatId.Flingy, "sprite", "Sprite"),

        R(DatId.Images, DatId.Units, "construction_animation", "Construction", DatTabId.UnitsGraphics),
        R(DatId.Images, DatId.Sprites, "image", "Image"),

        R(DatId.Upgrades, DatId.Units, "armor_upgrade", "Upgrade", DatTabId.UnitsBasic),
        R(DatId.Upgrades, DatId.Weapons, "damage_upgrade", "Upgrade"),

        R(DatId.Techdata, DatId.Orders, "technology_energy", "Energy"),
        R(DatId.Techdata, DatId.Weapons, "unused_technology", "Unused"),

        R(DatId.Sfxdata, DatId.Units, "ready_sound", "Ready", DatTabId.UnitsSounds),
        R(DatId.Sfxdata, DatId.Units, "what_sound_start", "What", DatTabId.UnitsSounds, "what_sound_end"),
        R(DatId.Sfxdata, DatId.Units, "pissed_sound_start", "Annoyed", DatTabId.UnitsSounds, "pissed_sound_end"),
        R(DatId.Sfxdata, DatId.Units, "yes_sound_start", "Yes", DatTabId.UnitsSounds, "yes_sound_end"),

        R(DatId.Portdata, DatId.Units, "portrait", "Portrait", DatTabId.UnitsGraphics),

        R(DatId.Orders, DatId.Units, "comp_ai_idle", "Computer Idle", DatTabId.UnitsAiActions),
        R(DatId.Orders, DatId.Units, "human_ai_idle", "Human Idle", DatTabId.UnitsAiActions),
        R(DatId.Orders, DatId.Units, "return_to_idle", "Return to Idle", DatTabId.UnitsAiActions),
        R(DatId.Orders, DatId.Units, "attack_unit", "Attach Unit", DatTabId.UnitsAiActions),
        R(DatId.Orders, DatId.Units, "attack_move", "Attack Move", DatTabId.UnitsAiActions),
    ];

    /// <summary>
    /// Every entry of every file whose field points at <paramref name="entry"/> of <paramref name="target"/>,
    /// in PyDAT's order: file by file, each file's entries in turn, and within an entry the
    /// fields in table order. <paramref name="dat"/> gives the open file or null; a file that is
    /// not open is skipped, and so is a field the entry has no value for.
    /// </summary>
    public static IReadOnlyList<DatRefMatch> UsedBy(DatId target, int entry, Func<DatId, DatFile?> dat)
    {
        var matches = new List<DatRefMatch>();
        var rows = All.Where(r => r.Target == target).ToList();
        foreach (var from in rows.Select(r => r.From).Distinct())
        {
            if (dat(from) is not { } file) continue;
            var fromRows = rows.Where(r => r.From == from).ToList();
            for (int e = 0; e < file.Count; e++)
                foreach (var row in fromRows)
                    if (file.Get(e, row.Property) is { } value && Matches(file, e, row, value, entry))
                        matches.Add(new DatRefMatch(from, e, row.Label, row.SubTab));
        }
        return matches;
    }

    static bool Matches(DatFile file, int e, DatRef row, ulong value, int entry)
    {
        if (row.RangeEnd is not { } end) return value == (ulong)entry;
        // A range row is always a range, start to end (PyDAT's falsy end 0 matches the start alone).
        return file.Get(e, end) is { } last && value <= (ulong)entry && (ulong)entry <= last;
    }

    /// <summary>
    /// The Check buttons beside images.dat's GRP File and IScript ID: the other images whose
    /// <paramref name="property"/> equals the shown one's.
    /// </summary>
    public static IReadOnlyList<DatRefMatch> SameValue(DatFile images, int entry, string property, string label)
    {
        var matches = new List<DatRefMatch>();
        if (images.Get(entry, property) is not { } value) return matches;
        for (int e = 0; e < images.Count; e++)
            if (e != entry && images.Get(e, property) == value)
                matches.Add(new DatRefMatch(DatId.Images, e, label, null));
        return matches;
    }

    /// <summary>PyDAT's text for a line: <c>units.dat, Ground Weapon field (Basic sub-tab), entry 0: Terran Marine</c>.</summary>
    public static string Describe(DatRefMatch match, string entryName)
    {
        string subTab = match.SubTab is { } tab ? $" ({DatTabs.Of(tab).Name} sub-tab)" : "";
        return $"{DatFormats.Of(match.File).FileName}, {match.Label} field{subTab}, entry {match.Entry}: {entryName}";
    }
}
