namespace Manifold.Core.Data;

public enum Race { None, Zerg, Terran, Protoss }

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
}
