using System.Text.RegularExpressions;
using Manifold.Core.Data;

namespace Manifold.Core.Editor;

/// <summary>What an entry is called: the list's name, the .tbl's string, or both (PyDAT's Basic, TBL and Combined).</summary>
public enum NamesMode { Basic, Tbl, Combined }

/// <summary>Everything the names are built from; a missing source is null (a list: empty).</summary>
public sealed record DatNameSources(
    Func<DatId, DatFile?> Dat,
    StringTable? StatTxt,
    StringTable? UnitNames,
    StringTable? ImagesTbl,
    StringTable? SfxdataTbl,
    StringTable? PortdataTbl,
    StringTable? MapdataTbl,
    DatLists Lists);

/// <summary>
/// The names PyDAT shows for a .dat file's entries (PyMS's <c>DATEntryName</c>, spec §6).
/// Each entry has a default name ("Unit #5", "Expanded Image #1000" past the vanilla count);
/// the mode picks the list's name, the .tbl's string, or both.
/// </summary>
public static partial class DatNames
{
    const string Hotkey = @"^.<0*[0-5]>";
    const string Code = @"<\d+>";

    [GeneratedRegex(Hotkey)] private static partial Regex HotkeyRegex();
    [GeneratedRegex(Code)] private static partial Regex CodeRegex();

    public static string TypeName(DatId file) => file switch
    {
        DatId.Units => "Unit",
        DatId.Weapons => "Weapon",
        DatId.Flingy => "Flingy",
        DatId.Sprites => "Sprite",
        DatId.Images => "Image",
        DatId.Upgrades => "Upgrade",
        DatId.Techdata => "Tech",
        DatId.Sfxdata => "Sound",
        DatId.Portdata => "Portrait",
        DatId.Mapdata => "Map",
        DatId.Orders => "Order",
        _ => throw new ArgumentOutOfRangeException(nameof(file)),
    };

    /// <summary>Whether the file's TBL names have a simple form (hotkeys and codes dropped).</summary>
    public static bool HasSimple(DatId file) =>
        file is DatId.Units or DatId.Weapons or DatId.Upgrades or DatId.Techdata or DatId.Orders;

    /// <summary>
    /// The table's string for the 1-based id as text (PyTBL's, <c>&lt;N&gt;</c> for control bytes) without
    /// its final NUL; null when there is no table, or no such string (id 0 is none).
    /// </summary>
    public static string? TblString(StringTable? table, ulong id)
    {
        if (table is null || id == 0 || id > (ulong)table.Count) return null;
        var segment = table.Segment((int)id);
        if (segment.Length > 0 && segment[^1] == 0) segment = segment[..^1];
        return TblText.Decompile(segment);
    }

    /// <summary>The names of every entry of the file (its own count, else the vanilla one).</summary>
    public static IReadOnlyList<string> Build(DatId file, DatNameSources sources, NamesMode mode, bool simple)
    {
        var format = DatFormats.Of(file);
        var dat = sources.Dat(file);
        int count = dat?.Count ?? format.Entries;
        var list = sources.Lists[file.ToString()];
        simple = simple && HasSimple(file);
        var names = new string[count];
        for (int id = 0; id < count; id++)
        {
            if (file == DatId.Units && format.Reserved is { } reserved && reserved.Contains(id))
            {
                names[id] = $"Reserved Unit #{id}";
                continue;
            }
            string fallback = $"{TypeName(file)} #{id}";
            if (id >= format.Entries) fallback = "Expanded " + fallback;
            string? listName = id < list.Count && list[id].Length > 0 ? list[id] : null;
            string? tblName = mode == NamesMode.Basic ? null : TblName(file, id, dat, sources, simple);
            names[id] = Choose(mode, listName, tblName, fallback);
        }
        return names;
    }

    static string Choose(NamesMode mode, string? listName, string? tblName, string fallback)
    {
        string? name = null;
        if (mode == NamesMode.Combined && !string.IsNullOrEmpty(tblName))
        {
            listName ??= fallback;
            name = listName == tblName ? listName : $"{listName} ({tblName})";
        }
        else if (mode != NamesMode.Tbl && !string.IsNullOrEmpty(listName)) name = listName;
        else if (mode != NamesMode.Basic && !string.IsNullOrEmpty(tblName)) name = tblName;
        return string.IsNullOrEmpty(name) ? fallback : name;
    }

    static string? TblName(DatId file, int id, DatFile? dat, DatNameSources s, bool simple)
    {
        switch (file)
        {
            case DatId.Units: return UnitName(id, s, simple);
            case DatId.Weapons or DatId.Upgrades or DatId.Techdata or DatId.Orders:
                return Label(dat, id, "label", s.StatTxt, "None", simple);
            case DatId.Sfxdata: return Label(dat, id, "sound_file", s.SfxdataTbl, "No sound", false);
            case DatId.Portdata: return Label(dat, id, "idle_portrait_file", s.PortdataTbl, "None", false);
            case DatId.Mapdata: return Label(dat, id, "map_file", s.MapdataTbl, null, false);
            case DatId.Images: return GrpImage(s, id);
            case DatId.Sprites: return GrpSprite(s, id);
            case DatId.Flingy:
                return s.Dat(DatId.Flingy) is { } flingy && id < flingy.Count && flingy.Get(id, "sprite") is { } sprite
                    ? GrpSprite(s, (int)sprite)
                    : null;
            default: return null;
        }
    }

    static string? UnitName(int id, DatNameSources s, bool simple)
    {
        // The table of unit names, when there is one, replaces stat_txt's first 228 strings.
        string? name;
        if (s.UnitNames is not null) name = TblString(s.UnitNames, (ulong)id + 1);
        else name = id < 228 ? TblString(s.StatTxt, (ulong)id + 1) : null;
        if (name is null || !simple) return name;

        var pieces = name.Split("<0>");
        string special = pieces.Length > 1 ? pieces[1] : "*";
        string result = CodeRegex().Replace(pieces[0], "");
        if (special.Length > 0 && special != "*") result += $" ({CodeRegex().Replace(special, "")})";
        return result;
    }

    static string? Label(DatFile? dat, int id, string property, StringTable? table, string? none, bool simple)
    {
        if (dat is null || table is null || id >= dat.Count || dat.Get(id, property) is not { } label) return null;
        if (label == 0) return none;
        string? name = TblString(table, label);
        if (name is null || !simple) return name;
        return CodeRegex().Replace(HotkeyRegex().Replace(name, ""), "");
    }

    static string? GrpImage(DatNameSources s, int id)
    {
        if (s.Dat(DatId.Images) is not { } images || id < 0 || id >= images.Count) return null;
        return images.Get(id, "grp_file") is { } grp ? TblString(s.ImagesTbl, grp) : null;
    }

    static string? GrpSprite(DatNameSources s, int id)
    {
        if (s.Dat(DatId.Sprites) is not { } sprites || id < 0 || id >= sprites.Count) return null;
        return sprites.Get(id, "image") is { } image ? GrpImage(s, (int)image) : null;
    }
}
