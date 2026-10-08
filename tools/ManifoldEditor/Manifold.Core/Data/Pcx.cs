namespace Manifold.Core.Data;

/// <summary>An 8-bit, one-plane PCX image (game\tfontgam.pcx), decoded as PyMS's PCX.py does.</summary>
public sealed class Pcx
{
    public int Width { get; }
    public int Height { get; }
    /// <summary>Palette indexes, row by row.</summary>
    public byte[] Pixels { get; }
    /// <summary>The image's own palette, ARGB.</summary>
    public uint[] Palette { get; }

    public Pcx(byte[] bytes)
    {
        if (bytes.Length < 128 + 769 || bytes[0] != 0x0A || bytes[2] != 1 || bytes[3] != 8)
            throw new InvalidDataException("not an 8-bit RLE PCX");
        if (bytes[65] != 1) throw new InvalidDataException($"{bytes[65]} colour planes; only 1 is read");
        Width = BitConverter.ToUInt16(bytes, 8) - BitConverter.ToUInt16(bytes, 4) + 1;
        Height = BitConverter.ToUInt16(bytes, 10) - BitConverter.ToUInt16(bytes, 6) + 1;
        int bytesPerLine = BitConverter.ToUInt16(bytes, 66);
        if (Width <= 0 || Height <= 0 || bytesPerLine < Width) throw new InvalidDataException("bad PCX size");
        Palette = PcxPalette.Read(bytes);

        var lines = new byte[bytesPerLine * Height];
        int at = 128, n = 0, end = bytes.Length - 769;
        while (at < end && n < lines.Length)
        {
            byte b = bytes[at++];
            if ((b & 0xC0) == 0xC0)
            {
                if (at >= end) break;
                byte value = bytes[at++];
                for (int k = b & 0x3F; k > 0 && n < lines.Length; k--) lines[n++] = value;
            }
            else lines[n++] = b;
        }
        Pixels = new byte[Width * Height];
        for (int y = 0; y < Height; y++)
            Array.Copy(lines, y * bytesPerLine, Pixels, y * Width, Width);
    }

    public byte this[int x, int y] => Pixels[y * Width + x];
}
