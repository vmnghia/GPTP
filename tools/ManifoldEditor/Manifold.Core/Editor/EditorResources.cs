using Manifold.Core.Data;

namespace Manifold.Core.Editor;

/// <summary>
/// The game files the editor shows: strings, icons, and units.dat. Each is looked up
/// through the resolver (the mod exe first, then the vanilla MPQs); the icons' palette is
/// PyMS's Icons.pal, passed in (ticon.pcx only recolours highlighted icons). One
/// that is missing or unreadable is left null and noted: the editor still opens, showing
/// ids and numbers in its place (spec §6).
/// </summary>
public sealed class EditorResources
{
    public const string StatTxtPath = StringTable.ArchivePath;
    public const string IconsPath = "unit\\cmdbtns\\cmdicons.grp";
    public const string UnitsDatPath = "arr\\units.dat";
    public const string TfontgamPath = "game\\tfontgam.pcx";

    public StatTxt? Strings { get; private set; }
    public Grp? Icons { get; private set; }
    public uint[]? IconPalette { get; private set; }
    public UnitsDat? Units { get; private set; }
    /// <summary>The preview's colour for each text code; vanilla's when tfontgam.pcx can't be read.</summary>
    public uint[] TextColours { get; private set; } = TextColors.Default;
    /// <summary>"path: archive", for the status bar.</summary>
    public List<string> Sources { get; } = new();
    /// <summary>What is missing or could not be read.</summary>
    public List<string> Notes { get; } = new();

    public static EditorResources Load(ResourceResolver resolver, uint[]? iconPalette)
    {
        var r = new EditorResources { IconPalette = iconPalette };
        if (iconPalette is null) r.Notes.Add("Icons.pal: not found; icon numbers are shown instead of icons");
        r.Strings = r.Get(resolver, StatTxtPath, StatTxt.Parse, "string ids are shown instead of text");
        r.Icons = r.Get(resolver, IconsPath, bytes => new Grp(bytes), "icon numbers are shown instead of icons");
        r.Units = r.Get(resolver, UnitsDatPath, UnitsDat.Parse, "the set list is not grouped by race");
        if (r.Get(resolver, TfontgamPath, bytes => TextColors.FromTfontgam(new Pcx(bytes)),
                "string previews use vanilla's colours") is { } colours)
            r.TextColours = colours;
        return r;
    }

    T? Get<T>(ResourceResolver resolver, string path, Func<byte[], T> parse, string consequence) where T : class
    {
        if (resolver.Find(path) is not { } found)
        {
            Notes.Add($"{path}: not found; {consequence}");
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
