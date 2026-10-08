namespace Manifold.Core.Data;

public enum Race { None, Zerg, Terran, Protoss }

/// <summary>What a set belongs to: a kind of unit, or one of the cards 228-249.</summary>
public enum UnitKind { Unit, Building, Addon, Hero, Subunit, Card }

/// <summary>
/// The parts of arr\units.dat the editor uses: each unit's race (StarEdit group flags) and
/// whether it is a hero. The file is 54 arrays, 19876 bytes in 1.16.1; the offsets follow
/// from GPTP's units_dat order (SCBW/scbwdata.h).
/// </summary>
public sealed class UnitsDat
{
    public const int FileSize = 19876;
    public const int UnitCount = 228;
    /// <summary>Array 22 (BaseProperty), u32 per unit.</summary>
    public const int BasePropertyOffset = 7032;
    /// <summary>Array 44 (GroupFlags), u8 per unit: Zerg 0x01, Terran 0x02, Protoss 0x04.</summary>
    public const int GroupFlagsOffset = 16684;
    const uint HeroProperty = 0x40;

    readonly byte[] groupFlags;
    readonly uint[] properties;

    UnitsDat(byte[] groupFlags, uint[] properties)
    {
        this.groupFlags = groupFlags;
        this.properties = properties;
    }

    public static UnitsDat Parse(byte[] bytes)
    {
        if (bytes.Length != FileSize)
            throw new InvalidDataException($"units.dat is {bytes.Length} bytes, not {FileSize}");
        var groups = bytes[GroupFlagsOffset..(GroupFlagsOffset + UnitCount)];
        var properties = new uint[UnitCount];
        for (int i = 0; i < UnitCount; i++)
            properties[i] = BitConverter.ToUInt32(bytes, BasePropertyOffset + i * 4);
        return new UnitsDat(groups, properties);
    }

    public Race RaceOf(int unit)
    {
        if (unit < 0 || unit >= UnitCount) return Race.None;
        byte flags = groupFlags[unit];
        return (flags & 0x01) != 0 ? Race.Zerg : (flags & 0x02) != 0 ? Race.Terran
            : (flags & 0x04) != 0 ? Race.Protoss : Race.None;
    }

    public bool IsHero(int unit) => unit >= 0 && unit < UnitCount && (properties[unit] & HeroProperty) != 0;

    /// <summary>
    /// The set list's type filter, from units.dat's special ability flags: hero (0x40) first,
    /// then add-on (0x02), building (0x01), subunit such as a turret (0x10), else unit.
    /// </summary>
    public UnitKind KindOf(int unit)
    {
        if (unit < 0 || unit >= UnitCount) return UnitKind.Card;
        uint p = properties[unit];
        return (p & HeroProperty) != 0 ? UnitKind.Hero
            : (p & 0x02) != 0 ? UnitKind.Addon
            : (p & 0x01) != 0 ? UnitKind.Building
            : (p & 0x10) != 0 ? UnitKind.Subunit
            : UnitKind.Unit;
    }
}
