namespace Manifold.Core.Data;

/// <summary>The .dat files the editor opens, in PyDAT's tab order.</summary>
public enum DatId { Units, Weapons, Flingy, Sprites, Images, Upgrades, Techdata, Sfxdata, Portdata, Mapdata, Orders }

/// <summary>
/// How a property is stored in a .dat file. All are unsigned and little-endian. A value is
/// held as its raw number; the compound types (size, position, extents, hit points, supply)
/// are split for display only.
/// </summary>
public enum DatType { Byte, Short, Long, Size, Position, Extents, HitPoints, Supply }

public static class DatTypes
{
    /// <summary>How many bytes a value of the type takes in the file.</summary>
    public static int Bytes(DatType type) => type switch
    {
        DatType.Byte => 1,
        DatType.Short => 2,
        DatType.Long => 4,
        DatType.Size => 4,
        DatType.Position => 4,
        DatType.Extents => 8,
        DatType.HitPoints => 4,
        DatType.Supply => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    /// <summary>The largest raw value the type holds: all its bytes set.</summary>
    public static ulong Max(DatType type) => Bytes(type) == 8 ? ulong.MaxValue : (1UL << (8 * Bytes(type))) - 1;
}

/// <summary>
/// One column of a .dat file: a name (PyMS's), a type, and optionally the entries it covers
/// in the vanilla layout, a different type in the expanded layout, and the .dat file its
/// value points into.
/// </summary>
/// <param name="VanillaFirst">First entry covered in the vanilla layout; null with no count: every entry.</param>
/// <param name="VanillaCount">How many entries it covers in the vanilla layout; null: all of them.</param>
/// <param name="ExpandedType">The type in the expanded layout, when it differs.</param>
/// <param name="Target">The .dat file the value is an index into, when it is one.</param>
public sealed record DatProperty(string Name, DatType Type, int? VanillaFirst = null, int? VanillaCount = null,
    DatType? ExpandedType = null, DatId? Target = null)
{
    public DatType TypeIn(bool expanded) => expanded && ExpandedType is { } t ? t : Type;

    /// <summary>Whether the property has a value on the entry. The expanded layout covers every entry.</summary>
    public bool Covers(int entry, bool expanded)
    {
        if (entry < 0) return false;
        if (expanded || VanillaCount is null) return true;
        int first = VanillaFirst ?? 0;
        return entry >= first && entry < first + VanillaCount.Value;
    }
}

/// <summary>
/// One .dat file's layout, as PyMS's DATFormat describes it: the vanilla entry count, the
/// expanded layout's limits, and the properties in file order (each property is one array
/// of all the entries).
/// </summary>
/// <param name="ExpandedMin">Fewest entries of an expanded file; null: no minimum.</param>
/// <param name="ExpandedMax">Most entries of an expanded file; null: the file has no expanded layout.</param>
/// <param name="ExpandedMultiple">An expanded file's entry count is a multiple of this.</param>
/// <param name="Reserved">Entries an expanded file keeps free (units.dat's 228-250).</param>
public sealed record DatFormat(DatId Id, string FileName, int Entries, IReadOnlyList<DatProperty> Properties,
    int? ExpandedMin = null, int? ExpandedMax = null, int? ExpandedMultiple = null, IReadOnlyList<int>? Reserved = null)
{
    public string ArchivePath => "arr\\" + FileName;

    /// <summary>The size in bytes of the vanilla file.</summary>
    public int VanillaSize
    {
        get
        {
            int size = 0;
            foreach (var p in Properties) size += (p.VanillaCount ?? Entries) * DatTypes.Bytes(p.Type);
            return size;
        }
    }

    /// <summary>The bytes one entry takes in the expanded layout; null when there is none.</summary>
    public int? ExpandedEntrySize
    {
        get
        {
            if (ExpandedMax is null) return null;
            int size = 0;
            foreach (var p in Properties) size += DatTypes.Bytes(p.TypeIn(true));
            return size;
        }
    }

    public int IndexOf(string property)
    {
        for (int i = 0; i < Properties.Count; i++)
            if (Properties[i].Name == property) return i;
        throw new ArgumentException($"{FileName} has no property \"{property}\"", nameof(property));
    }

    public DatProperty this[string property] => Properties[IndexOf(property)];
}
