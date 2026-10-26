using Manifold.Core.Data;

namespace Manifold.Core.Editor;

/// <summary>
/// The palettes PyDAT draws images with (PyMS's Palettes\*.pal, shipped in the app's Data\):
/// Units for most, Terrain for thingy\tileset\, and the three fire remappings. A missing
/// or unreadable one is null; the previews that need it say so.
/// </summary>
public sealed record PreviewPalettes(uint[]? Units, uint[]? Terrain, uint[]? Ofire, uint[]? Gfire, uint[]? Bfire)
{
    public static readonly PreviewPalettes None = new(null, null, null, null, null);

    static readonly string[] Names = ["Units.pal", "Terrain.pal", "ofire.pal", "gfire.pal", "bfire.pal"];

    public static PreviewPalettes Load(string folder)
    {
        uint[]? Read(string name)
        {
            try { return RawPalette.Read(File.ReadAllBytes(Path.Combine(folder, name))); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException) { return null; }
        }
        return new(Read(Names[0]), Read(Names[1]), Read(Names[2]), Read(Names[3]), Read(Names[4]));
    }

    /// <summary>The files that are missing or unreadable, e.g. "Units.pal".</summary>
    public IEnumerable<string> Missing =>
        new[] { Units, Terrain, Ofire, Gfire, Bfire }.Select((p, i) => (p, i)).Where(x => x.p is null).Select(x => Names[x.i]);

    internal (uint[]? Palette, string Name) Of(int index) => index switch
    {
        1 => (Terrain, Names[1]),
        2 => (Ofire, Names[2]),
        3 => (Gfire, Names[3]),
        4 => (Bfire, Names[4]),
        _ => (Units, Names[0]),
    };
}

/// <summary>
/// A frame as Width x Height ARGB pixels, 0 where transparent. Top and Bottom are PyMS's
/// frame bounds: the first drawn row and the last drawn row + 1 (both -1 when nothing is drawn).
/// </summary>
public sealed record PreviewPicture(uint[] Pixels, int Width, int Height, int Top, int Bottom);

/// <summary>
/// PyDAT's previews (previews spec §2): frame 0 of an image's GRP, read through
/// <paramref name="read"/> (the exe's MPQ, then vanilla), with PyDAT's palette and draw
/// function rules. Frames are cached per path, palette and draw function; make one per open exe.
/// </summary>
public sealed class DatPreview(Func<string, byte[]?> read, PreviewPalettes palettes)
{
    const int UseRemapping = 9, Shadow = 10, SelectionCircle = 13;
    public const uint CircleColour = 0xFF249824, ShadowColour = 0xFF323232;

    readonly Dictionary<(string Path, int Palette, int Draw), (PreviewPicture?, string?)> cache = new();

    /// <summary>Frame 0 of the image, or null and why it can't be drawn.</summary>
    public (PreviewPicture? Picture, string? Reason) Image(DatFile images, StringTable? imagesTbl, int image)
    {
        if (image < 0 || image >= images.Count) return (null, $"image {image} is past images.dat's end");
        ulong grp = images.Get(image, "grp_file") ?? 0;
        if (grp == 0) return (null, $"image {image} has no GRP file");
        if (DatNames.TblString(imagesTbl, grp) is not { } tblPath) return (null, $"images.tbl has no string {grp}");
        int draw = (int)(images.Get(image, "draw_function") ?? 0);
        int remap = (int)(images.Get(image, "remapping") ?? 0);
        int palette = tblPath.StartsWith("thingy\\tileset\\", StringComparison.Ordinal) ? 1
            : draw == UseRemapping && remap is >= 1 and <= 3 ? 1 + remap
            : 0;
        if (draw is not (Shadow or SelectionCircle)) draw = 0;
        string path = "unit\\" + tblPath;
        var key = (path, palette, draw);
        if (!cache.TryGetValue(key, out var result))
            cache[key] = result = Decode(path, palette, draw);
        return result;
    }

    (PreviewPicture?, string?) Decode(string path, int paletteIndex, int draw)
    {
        var (palette, paletteName) = palettes.Of(paletteIndex);
        if (palette is null) return (null, $"{paletteName} not found");
        if (read(path) is not { } bytes) return (null, $"{path} not found");
        byte[] indexes;
        int width, height;
        try
        {
            if (bytes.Length < 6) return (null, $"{path} is damaged");
            var grp = new Grp(bytes);
            if (grp.FrameCount == 0) return (null, $"{path} has no frames");
            if (bytes.Length < 6 + 8 * grp.FrameCount) return (null, $"{path} is damaged");
            if (grp.Width == 0 || grp.Height == 0) return (null, $"{path} has an empty frame");
            indexes = grp.DecodeFrame(0);
            (width, height) = (grp.Width, grp.Height);
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException)
        {
            return (null, $"{path} is damaged");
        }

        var pixels = new uint[indexes.Length];
        int top = -1, bottom = -1;
        for (int i = 0; i < indexes.Length; i++)
        {
            byte index = indexes[i];
            if (index == 0) continue;
            pixels[i] = draw switch
            {
                Shadow => ShadowColour,
                SelectionCircle when index <= 9 => CircleColour,
                _ => palette[index],
            };
            int row = i / width;
            if (top < 0) top = row;
            bottom = row + 1;
        }
        return (new PreviewPicture(pixels, width, height, top, bottom), null);
    }
}
