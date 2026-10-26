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

/// <summary>One thing a preview draws, in PyDAT's order.</summary>
public abstract record PreviewLayer;

/// <summary>A picture placed by its centre, as Tk's centre anchor: its top-left is (X - Width/2, Y - Height/2).</summary>
public sealed record PictureLayer(PreviewPicture Picture, int X, int Y) : PreviewLayer;

/// <summary>
/// A Tk rectangle: the outline covers the pixels on its four edges, X1..X2 and Y1..Y2
/// inclusive; a fill covers every pixel in that range.
/// </summary>
public sealed record BoxLayer(int X1, int Y1, int X2, int Y2, uint Outline, uint? Fill = null) : PreviewLayer;

/// <summary>What a preview shows: its layers, and why anything is missing (each reason once).</summary>
public sealed record PreviewScene(IReadOnlyList<PreviewLayer> Layers, IReadOnlyList<string> Reasons)
{
    public static readonly PreviewScene Empty = new([], []);

    /// <summary>The 257x257 square, row-major ARGB: black, then the layers in order, clipped to the square.</summary>
    public uint[] Render()
    {
        const int size = DatPreview.Size;
        var square = new uint[size * size];
        Array.Fill(square, 0xFF000000u);
        void Set(int x, int y, uint colour)
        {
            if (x >= 0 && x < size && y >= 0 && y < size) square[y * size + x] = colour;
        }
        foreach (var layer in Layers)
        {
            switch (layer)
            {
                case PictureLayer { Picture: var p } picture:
                    int left = picture.X - p.Width / 2, top = picture.Y - p.Height / 2;
                    for (int y = Math.Max(0, -top); y < p.Height && top + y < size; y++)
                        for (int x = Math.Max(0, -left); x < p.Width && left + x < size; x++)
                            if (p.Pixels[y * p.Width + x] is var c and not 0) Set(left + x, top + y, c);
                    break;
                case BoxLayer box:
                    int x1 = Math.Max(box.X1, 0), x2 = Math.Min(box.X2, size - 1);
                    int y1 = Math.Max(box.Y1, 0), y2 = Math.Min(box.Y2, size - 1);
                    if (box.Fill is { } fill)
                        for (int y = y1; y <= y2; y++)
                            for (int x = x1; x <= x2; x++) Set(x, y, fill);
                    for (int x = x1; x <= x2; x++) { Set(x, box.Y1, box.Outline); Set(x, box.Y2, box.Outline); }
                    for (int y = y1; y <= y2; y++) { Set(box.X1, y, box.Outline); Set(box.X2, y, box.Outline); }
                    break;
            }
        }
        return square;
    }
}

/// <summary>What the Units Graphics preview adds to the picture (PyDAT's checkboxes).</summary>
public readonly record struct UnitPreviewOptions(bool Placement, bool Dimensions, bool Addon, int AddonParent);

/// <summary>
/// PyDAT's previews (previews spec §2): frame 0 of an image's GRP, read through
/// <paramref name="read"/> (the exe's MPQ, then vanilla), with PyDAT's palette and draw
/// function rules. Frames are cached per path, palette and draw function; make one per open exe.
/// </summary>
public sealed class DatPreview(Func<string, byte[]?> read, PreviewPalettes palettes)
{
    const int UseRemapping = 9, Shadow = 10, SelectionCircle = 13;
    public const uint CircleColour = 0xFF249824, ShadowColour = 0xFF323232;
    public const uint PlacementColour = 0xFFFF0000, DimensionsColour = 0xFF00FF00, AddonColour = 0xFFFFFF00;
    public const uint BarColour = 0xFF000000, BarBoxColour = 0xFF008000;
    /// <summary>The square's side, and its centre (PyDAT's 257x257 canvas, drawn about (130,130)).</summary>
    public const int Size = 257, Centre = 130;

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

    // ---- PyDAT's geometry (Python 2: integers divide rounding down) ----

    /// <summary>Units Graphics' placement box: GraphicsUnitsTab.drawboxes, halves of w / 2.0 floored and ceiled.</summary>
    public static BoxLayer PlacementBox(int width, int height) =>
        new(Centre - width / 2, Centre - height / 2, Centre - 1 + (width + 1) / 2, Centre - 1 + (height + 1) / 2, PlacementColour);

    /// <summary>StarEdit's placement box: StarEditUnitsTab divides the integers, so both halves round down.</summary>
    public static BoxLayer StarEditPlacementBox(int width, int height) =>
        new(Centre - width / 2, Centre - height / 2, Centre - 1 + width / 2, Centre - 1 + height / 2, PlacementColour);

    /// <summary>The unit's extents about the centre.</summary>
    public static BoxLayer DimensionsBox(int left, int up, int right, int down) =>
        new(Centre - left, Centre - up, Centre + right, Centre + down, DimensionsColour);

    /// <summary>The add-on's parent: its box, and the point its picture is centred on (draw_addon_preview).</summary>
    public static (BoxLayer Box, int X, int Y) AddonBox(int width, int height, int horizontal, int vertical, int parentWidth, int parentHeight)
    {
        int x = Centre - 1 - width / 2 - horizontal, y = Centre - 1 - height / 2 - vertical;
        return (new BoxLayer(x, y, x + parentWidth, y + parentHeight, AddonColour), x + parentWidth / 2, y + parentHeight / 2);
    }

    /// <summary>
    /// SpritesTab's health bar under a selection circle drawn at (130, <paramref name="circleY"/>):
    /// a black box 3*boxes wide, then a green box every 3 pixels; boxes = max(1, (health - 1) / 3).
    /// </summary>
    public static IReadOnlyList<BoxLayer> HealthBar(int circleY, PreviewPicture circle, int health)
    {
        int boxes = health <= 3 ? 1 : (health - 1) / 3;
        int w = 3 * boxes;
        int x = Centre - w / 2, y = circleY + 6 + (circle.Bottom - circle.Top) / 2;
        var bar = new List<BoxLayer> { new(x, y, x + w, y + 4, BarColour, BarColour) };
        for (int i = 0; i < boxes; i++)
            bar.Add(new(x + 1 + 3 * i, y + 1, x + 2 + 3 * i, y + 3, BarBoxColour, BarBoxColour));
        return bar;
    }

    // ---- The scenes ----

    /// <summary>Images: the image's frame at the centre.</summary>
    public PreviewScene ImageScene(Func<DatId, DatFile?> dat, StringTable? imagesTbl, int image)
    {
        var scene = new SceneBuilder();
        if (scene.Picture(this, dat, imagesTbl, image) is { } picture) scene.Add(new PictureLayer(picture, Centre, Centre));
        return scene.Build();
    }

    /// <summary>Sprites: the selection circle and health bar where the file has them, then the sprite's image.</summary>
    public PreviewScene SpriteScene(Func<DatId, DatFile?> dat, StringTable? imagesTbl, int sprite)
    {
        var scene = new SceneBuilder();
        if (scene.Entry(dat, DatId.Sprites, "sprite", sprite) is not { } sprites) return scene.Build();
        if (sprites.Covers(sprite, "selection_circle_image"))
        {
            int circleImage = 561 + (int)sprites.Get(sprite, "selection_circle_image")!.Value;
            if (scene.Picture(this, dat, imagesTbl, circleImage) is { } circle)
            {
                int y = Centre + (int)(sprites.Get(sprite, "selection_circle_offset") ?? 0);
                scene.Add(new PictureLayer(circle, Centre, y));
                foreach (var box in HealthBar(y, circle, (int)(sprites.Get(sprite, "health_bar") ?? 0))) scene.Add(box);
            }
        }
        if (scene.Picture(this, dat, imagesTbl, (int)sprites.Get(sprite, "image")!.Value) is { } picture)
            scene.Add(new PictureLayer(picture, Centre, Centre));
        return scene.Build();
    }

    /// <summary>Units Graphics: the add-on's parent, the unit, then the yellow, red and green boxes that are on.</summary>
    public PreviewScene UnitGraphicsScene(Func<DatId, DatFile?> dat, StringTable? imagesTbl, int unit, UnitPreviewOptions options)
    {
        var scene = new SceneBuilder();
        if (scene.Entry(dat, DatId.Units, "unit", unit) is not { } units) return scene.Build();
        var (width, height) = Size2(units, unit);
        BoxLayer? addonBox = null;
        if (options.Addon && units.Covers(unit, "addon_position"))
        {
            ulong position = units.Get(unit, "addon_position")!.Value;
            int horizontal = DatValues.Part(position, 0), vertical = DatValues.Part(position, 1);
            if ((horizontal != 0 || vertical != 0) && scene.Entry(dat, DatId.Units, "unit", options.AddonParent) is not null)
            {
                var (parentWidth, parentHeight) = Size2(units, options.AddonParent);
                var (box, x, y) = AddonBox(width, height, horizontal, vertical, parentWidth, parentHeight);
                if (scene.UnitPicture(this, dat, imagesTbl, options.AddonParent) is { } parent) scene.Add(new PictureLayer(parent, x, y));
                addonBox = box;
            }
        }
        if (scene.UnitPicture(this, dat, imagesTbl, unit) is { } picture) scene.Add(new PictureLayer(picture, Centre, Centre));
        if (addonBox is not null) scene.Add(addonBox);
        if (options.Placement) scene.Add(PlacementBox(width, height));
        if (options.Dimensions)
        {
            ulong extents = units.Get(unit, "unit_extents") ?? 0;
            scene.Add(DimensionsBox(DatValues.Part(extents, 0), DatValues.Part(extents, 1), DatValues.Part(extents, 2), DatValues.Part(extents, 3)));
        }
        return scene.Build();
    }

    /// <summary>Units StarEdit: the unit, then its placement box.</summary>
    public PreviewScene UnitStarEditScene(Func<DatId, DatFile?> dat, StringTable? imagesTbl, int unit)
    {
        var scene = new SceneBuilder();
        if (scene.Entry(dat, DatId.Units, "unit", unit) is not { } units) return scene.Build();
        if (scene.UnitPicture(this, dat, imagesTbl, unit) is { } picture) scene.Add(new PictureLayer(picture, Centre, Centre));
        var (width, height) = Size2(units, unit);
        scene.Add(StarEditPlacementBox(width, height));
        return scene.Build();
    }

    /// <summary>The sound's file in the MPQ, sound\ + its sfxdata.tbl string; null for none (PyDAT's SoundsTab.play).</summary>
    public static string? SoundPath(DatFile sfxdata, StringTable? sfxdataTbl, int sound)
    {
        if (sound < 0 || sound >= sfxdata.Count) return null;
        ulong file = sfxdata.Get(sound, "sound_file") ?? 0;
        return file != 0 && DatNames.TblString(sfxdataTbl, file) is { } path ? "sound\\" + path : null;
    }

    static (int Width, int Height) Size2(DatFile units, int unit)
    {
        ulong size = units.Get(unit, "staredit_placement_size") ?? 0;
        return (DatValues.Part(size, 0), DatValues.Part(size, 1));
    }

    /// <summary>A scene's layers and reasons as they are found.</summary>
    sealed class SceneBuilder
    {
        readonly List<PreviewLayer> layers = new();
        readonly List<string> reasons = new();

        public void Add(PreviewLayer layer) => layers.Add(layer);

        public PreviewScene Build() => new(layers, reasons);

        void Reason(string reason)
        {
            if (!reasons.Contains(reason)) reasons.Add(reason);
        }

        /// <summary>The file, when it is there and has the entry; else null and why.</summary>
        public DatFile? Entry(Func<DatId, DatFile?> dat, DatId id, string word, int entry)
        {
            string name = DatFormats.Of(id).FileName;
            if (dat(id) is not { } file) { Reason($"{name} not found"); return null; }
            if (entry < 0 || entry >= file.Count) { Reason($"{word} {entry} is past {name}'s end"); return null; }
            return file;
        }

        public PreviewPicture? Picture(DatPreview preview, Func<DatId, DatFile?> dat, StringTable? imagesTbl, int image)
        {
            if (dat(DatId.Images) is not { } images) { Reason($"{DatFormats.Of(DatId.Images).FileName} not found"); return null; }
            var (picture, reason) = preview.Image(images, imagesTbl, image);
            if (reason is not null) Reason(reason);
            return picture;
        }

        /// <summary>The unit's picture: graphics, then the flingy's sprite, then the sprite's image.</summary>
        public PreviewPicture? UnitPicture(DatPreview preview, Func<DatId, DatFile?> dat, StringTable? imagesTbl, int unit)
        {
            if (Entry(dat, DatId.Units, "unit", unit) is not { } units) return null;
            int flingy = (int)(units.Get(unit, "graphics") ?? 0);
            if (Entry(dat, DatId.Flingy, "flingy", flingy) is not { } flingies) return null;
            int sprite = (int)(flingies.Get(flingy, "sprite") ?? 0);
            if (Entry(dat, DatId.Sprites, "sprite", sprite) is not { } sprites) return null;
            return Picture(preview, dat, imagesTbl, (int)(sprites.Get(sprite, "image") ?? 0));
        }
    }
}
