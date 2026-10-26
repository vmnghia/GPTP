using Manifold.Core.Data;

namespace Manifold.Core.Model;

/// <summary>
/// Where the document's .dat files come from: the exe being edited first, StarCraft's own
/// as the fallback and as the "vanilla" the editor compares with.
/// </summary>
public static class DatSources
{
    /// <summary>
    /// Each file from <paramref name="exeFirst"/>, parsed. One the exe holds but the parser
    /// refuses opens as vanilla's copy, so it stays unchanged on a save. One found nowhere is
    /// absent (its tab is empty). <c>Vanilla</c> reads <paramref name="vanillaOnly"/> alone.
    /// </summary>
    public static DatSet Load(ResourceResolver exeFirst, ResourceResolver vanillaOnly, List<string> sources, List<string> notes)
    {
        var opened = new Dictionary<DatId, DatFile>();
        var vanilla = new Dictionary<DatId, DatFile>();
        foreach (var format in DatFormats.All)
        {
            string path = format.ArchivePath;
            DatFile? own = null;
            string? ownProblem = null;
            if (vanillaOnly.Find(path) is { } v)
            {
                try { own = DatFile.Parse(format, v.Data); }
                catch (InvalidDataException e) { ownProblem = e.Message; }
            }
            if (own is not null) vanilla[format.Id] = own;
            else if (ownProblem is not null) notes.Add($"{path}: vanilla's copy was refused ({ownProblem}); modified marks and revert to vanilla are off for it");

            if (exeFirst.Find(path) is not { } found)
            {
                if (own is not null) opened[format.Id] = own;   // the resolver's archives hold it only in vanilla's
                else notes.Add(ownProblem is null ? $"{path}: not found; its tab is empty" : $"{path}: its tab is empty");
                continue;
            }
            try
            {
                var file = DatFile.Parse(format, found.Data);
                opened[format.Id] = file;
                sources.Add($"{path}: {found.Source}");
                if (file.IsExpanded && !DatFormats.PluginExtends(format.Id))
                    notes.Add($"{path} is extended ({file.Count} entries): the plugin refuses it and the game will show its invalid-file warning");
            }
            catch (InvalidDataException e)
            {
                if (own is not null)
                {
                    opened[format.Id] = own;
                    notes.Add($"{path}: the exe's copy was refused ({e.Message}); vanilla's is shown");
                }
                else notes.Add($"{path}: refused ({e.Message}); its tab is empty");
            }
        }
        return new DatSet(opened, vanilla);
    }
}

/// <summary>A to-repack copy of a .dat file that differs from the one the document opened with.</summary>
public sealed record MirrorDiff(DatId File, string Path, bool Newer, DatFile Copy);

/// <summary>
/// The copies of the .dat files in the mod's to-repack folder (<c>arr\units.dat</c> and so
/// on), like <see cref="StringMirror"/>'s for stat_txt.tbl: saving writes them, so a later
/// add of the folder's files doesn't put old values back; opening compares them with the exe's.
/// </summary>
public static class DatMirror
{
    /// <summary>&lt;folder&gt;\arr\&lt;file&gt;.dat</summary>
    public static string PathIn(string repackFolder, DatId id) => Path.Combine(repackFolder, "arr", DatFormats.Of(id).FileName);

    /// <summary>Writes the copy, creating the folder and keeping the previous one as .bak.</summary>
    public static void Write(string repackFolder, DatId id, byte[] bytes) => StringMirror.Write(PathIn(repackFolder, id), bytes);

    /// <summary>
    /// The copies in the folder that parse and differ from what the document opened with.
    /// One that doesn't parse is added to <paramref name="notes"/> and left out.
    /// </summary>
    public static IReadOnlyList<MirrorDiff> Differing(ButtonSetDocument doc, string repackFolder, string exePath, List<string> notes)
    {
        var result = new List<MirrorDiff>();
        foreach (var format in DatFormats.All)
        {
            string path = PathIn(repackFolder, format.Id);
            if (!File.Exists(path)) continue;
            DatFile copy;
            try { copy = DatFile.Parse(format, File.ReadAllBytes(path)); }
            catch (InvalidDataException e)
            {
                notes.Add($"to-repack\\arr\\{format.FileName}: refused ({e.Message})");
                continue;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                notes.Add($"to-repack\\arr\\{format.FileName}: unreadable ({e.Message})");
                continue;
            }
            if (doc.OpenedDat(format.Id) is { } opened && opened.SameAs(copy)) continue;
            result.Add(new MirrorDiff(format.Id, path, StringMirror.IsNewerThan(path, exePath), copy));
        }
        return result;
    }
}
