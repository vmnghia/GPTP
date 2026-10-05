namespace Manifold.Core.Data;

/// <summary>
/// A GRP image (unit\cmdbtns\cmdicons.grp): u16 frameCount, u16 width, u16 height, then
/// per frame u8 x, u8 y, u8 w, u8 h, u32 offset. At the offset, h u16 row offsets
/// (relative to it), each row run-length coded: 0x80|n skips n pixels, 0x40|n repeats
/// the next byte n times, n copies n bytes.
/// </summary>
public sealed class Grp
{
    readonly byte[] bytes;
    public int FrameCount { get; }
    public int Width { get; }
    public int Height { get; }

    public Grp(byte[] bytes)
    {
        this.bytes = bytes;
        FrameCount = BitConverter.ToUInt16(bytes, 0);
        Width = BitConverter.ToUInt16(bytes, 2);
        Height = BitConverter.ToUInt16(bytes, 4);
    }

    /// <summary>The frame as Width x Height palette indexes, 0 where transparent.</summary>
    public byte[] DecodeFrame(int frame)
    {
        if (frame < 0 || frame >= FrameCount) throw new ArgumentOutOfRangeException(nameof(frame));
        int header = 6 + frame * 8;
        int x = bytes[header], y = bytes[header + 1], w = bytes[header + 2], h = bytes[header + 3];
        int offset = BitConverter.ToInt32(bytes, header + 4);
        var pixels = new byte[Width * Height];
        for (int row = 0; row < h; row++)
        {
            int at = offset + BitConverter.ToUInt16(bytes, offset + row * 2);
            int col = 0;
            while (col < w)
            {
                byte c = bytes[at++];
                if ((c & 0x80) != 0) col += c & 0x7F;
                else if ((c & 0x40) != 0)
                {
                    byte value = bytes[at++];
                    for (int i = 0; i < (c & 0x3F) && col < w; i++) Set(col++, value);
                }
                else
                    for (int i = 0; i < c && col < w; i++) Set(col++, bytes[at++]);
            }

            void Set(int c, byte value)
            {
                int px = x + c, py = y + row;
                if (px < Width && py < Height) pixels[py * Width + px] = value;
            }
        }
        return pixels;
    }
}

/// <summary>The 256-colour palette at the end of a PCX file (0x0C, then 768 RGB bytes).</summary>
public static class PcxPalette
{
    /// <summary>ARGB, opaque, 256 entries.</summary>
    public static uint[] Read(byte[] pcx)
    {
        int at = pcx.Length - 769;
        if (at < 128 || pcx[at] != 0x0C) throw new InvalidDataException("no 256-colour palette");
        var palette = new uint[256];
        for (int i = 0; i < 256; i++)
            palette[i] = 0xFF000000u | (uint)pcx[at + 1 + i * 3] << 16 | (uint)pcx[at + 2 + i * 3] << 8 | pcx[at + 3 + i * 3];
        return palette;
    }

    /// <summary>A decoded frame as ARGB, index 0 transparent.</summary>
    public static uint[] ToArgb(byte[] indexes, uint[] palette) =>
        indexes.Select(i => i == 0 ? 0u : palette[i]).ToArray();
}
