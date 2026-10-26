namespace Manifold.Core.Data;

/// <summary>
/// What the .dat editor does with a raw value: parsing a field's text, and splitting the
/// compound types (hit points, supply, sizes, positions, extents) into the parts PyDAT shows.
/// </summary>
public static class DatValues
{
    /// <summary>A field box's text: decimal digits only (trimmed), within what the type holds. Null otherwise.</summary>
    public static ulong? ParseNumber(string text, DatType type)
    {
        string t = text.Trim();
        if (t.Length == 0 || !t.All(char.IsAsciiDigit)) return null;
        return ulong.TryParse(t, out ulong value) && value <= DatTypes.Max(type) ? value : null;
    }

    /// <summary>Jump to ID's text: decimal, or hex after "0x". Null when it is neither.</summary>
    public static ulong? ParseId(string text)
    {
        string t = text.Trim();
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return ulong.TryParse(t.AsSpan(2), System.Globalization.NumberStyles.AllowHexSpecifier, null, out ulong hex) ? hex : null;
        return t.Length > 0 && t.All(char.IsAsciiDigit) && ulong.TryParse(t, out ulong dec) ? dec : null;
    }

    /// <summary>Hit points are a 24.8 fixed-point number: the whole part is the value shifted down 8 bits.</summary>
    public static (uint Whole, byte Fraction) HitPoints(ulong value) => ((uint)(value >> 8), (byte)value);

    public static ulong HitPoints(uint whole, byte fraction) => ((ulong)whole << 8) | fraction;

    /// <summary>Supply is counted in halves: the whole part is the value over two, the odd bit a half.</summary>
    public static (byte Whole, bool Half) Supply(ulong value) => ((byte)(value >> 1), (value & 1) != 0);

    public static ulong Supply(byte whole, bool half) => ((ulong)whole << 1) | (half ? 1UL : 0);

    /// <summary>Part <paramref name="index"/> of a size, position or extents value: 16 bits each, the lowest first.</summary>
    public static ushort Part(ulong value, int index) => (ushort)(value >> (16 * index));

    public static ulong WithPart(ulong value, int index, ushort part)
    {
        int shift = 16 * index;
        return (value & ~(0xFFFFUL << shift)) | ((ulong)part << shift);
    }

    /// <summary>Game frames as seconds at the Fastest speed, 24 frames to the second.</summary>
    public static double Seconds(ulong frames) => frames / 24.0;

    /// <summary>A speed or distance in pixels: the raw value is in 1/256 pixels.</summary>
    public static double Pixels(ulong value) => value / 256.0;
}
