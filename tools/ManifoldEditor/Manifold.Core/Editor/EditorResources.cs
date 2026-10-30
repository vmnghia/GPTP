using Manifold.Core.Data;

namespace Manifold.Core.Editor;

/// <summary>
/// The game files the editor shows: strings, icons, and the tables and iscript.bin the .dat tabs name things with (the .dat
/// files themselves are the document's, <see cref="Model.DatSources"/>). Each is looked up
/// through the resolver (the mod exe first, then the vanilla MPQs); the icons' palette is
/// PyMS's Icons.pal, passed in (ticon.pcx only recolours highlighted icons). One
/// that is missing or unreadable is left null and noted: the editor still opens, showing
/// ids and numbers in its place (spec §6).
/// </summary>
public sealed class EditorResources
{
    public const string StatTxtPath = StringTable.ArchivePath;
    public const string IconsPath = "unit\\cmdbtns\\cmdicons.grp";
    public const string ImagesTblPath = "arr\\images.tbl";
    public const string SfxdataTblPath = "arr\\sfxdata.tbl";
    public const string PortdataTblPath = "arr\\portdata.tbl";
    public const string MapdataTblPath = "arr\\mapdata.tbl";
    public const string UnitNamesPath = "rez\\unitnames.tbl";
    public const string IscriptPath = "scripts\\iscript.bin";
    public const string TfontgamPath = "game\\tfontgam.pcx";

    public StatTxt? Strings { get; private set; }
    public Grp? Icons { get; private set; }
    public uint[]? IconPalette { get; private set; }
    public StringTable? ImagesTbl { get; private set; }
    public StringTable? SfxdataTbl { get; private set; }
    public StringTable? PortdataTbl { get; private set; }
    public StringTable? MapdataTbl { get; private set; }
    /// <summary>rez\unitnames.tbl, when the exe has one: the TBL and Combined names modes read units' names from it instead of stat_txt.tbl.</summary>
    public StringTable? UnitNames { get; private set; }
    /// <summary>The ids iscript.bin defines, for the Images tab's script box.</summary>
    public IReadOnlyList<int>? IscriptIds { get; private set; }
    /// <summary>The preview's colour for each text code; vanilla's when tfontgam.pcx can't be read.</summary>
    public uint[] TextColours { get; private set; } = TextColors.Default;
    /// <summary>"path: archive", for the status bar.</summary>
    public List<string> Sources { get; } = new();
    /// <summary>What is missing or could not be read.</summary>
    public List<string> Notes { get; } = new();
    /// <summary>Where the resources were looked up (the exe, then vanilla): the previews read GRPs and sounds through it.</summary>
    public ResourceResolver? Resolver { get; private set; }
    /// <summary>The palettes the Data page's previews draw with (previews spec §2).</summary>
    public PreviewPalettes Palettes { get; private set; } = PreviewPalettes.None;

    public static EditorResources Load(ResourceResolver resolver, uint[]? iconPalette, PreviewPalettes? palettes = null)
    {
        var r = new EditorResources { IconPalette = iconPalette, Resolver = resolver, Palettes = palettes ?? PreviewPalettes.None };
        foreach (string missing in palettes?.Missing ?? []) r.Notes.Add($"{missing}: not found; previews that need it show why");
        if (iconPalette is null) r.Notes.Add("Icons.pal: not found; icon numbers are shown instead of icons");
        r.Strings = r.Get(resolver, StatTxtPath, StatTxt.Parse, "string ids are shown instead of text");
        r.Icons = r.Get(resolver, IconsPath, bytes => new Grp(bytes), "icon numbers are shown instead of icons");
        r.ImagesTbl = r.Get(resolver, ImagesTblPath, StringTable.Parse, "image names show numbers");
        r.SfxdataTbl = r.Get(resolver, SfxdataTblPath, StringTable.Parse, "sound names show numbers");
        r.PortdataTbl = r.Get(resolver, PortdataTblPath, StringTable.Parse, "portrait names show numbers");
        r.MapdataTbl = r.Get(resolver, MapdataTblPath, StringTable.Parse, "map names show numbers");
        r.UnitNames = r.Get(resolver, UnitNamesPath, StringTable.Parse, "", quietWhenMissing: true);
        r.IscriptIds = r.Get(resolver, IscriptPath, Data.IscriptIds.Read, "script ids show numbers");
        if (r.Get(resolver, TfontgamPath, bytes => TextColors.FromTfontgam(new Pcx(bytes)),
                "string previews use vanilla's colours") is { } colours)
            r.TextColours = colours;
        return r;
    }

    T? Get<T>(ResourceResolver resolver, string path, Func<byte[], T> parse, string consequence, bool quietWhenMissing = false) where T : class
    {
        if (resolver.Find(path) is not { } found)
        {
            if (!quietWhenMissing) Notes.Add($"{path}: not found; {consequence}");
            return null;
        }
        try
        {
            var value = parse(found.Data);
            Sources.Add($"{path}: {found.Source}");
            return value;
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentException or IndexOutOfRangeException)
        {
            Notes.Add($"{path} from {found.Source}: unreadable ({e.Message}); {consequence}");
            return null;
        }
    }

    public string? Text(ushort id) => Strings?.Get(id);

    /// <summary>An icon as ARGB pixels (Icons.Width x Icons.Height), or null without icons.</summary>
    public uint[]? IconPixels(int frame) =>
        Icons is not null && IconPalette is not null && frame >= 0 && frame < Icons.FrameCount
            ? PcxPalette.ToArgb(Icons.DecodeFrame(frame), IconPalette)
            : null;
}
