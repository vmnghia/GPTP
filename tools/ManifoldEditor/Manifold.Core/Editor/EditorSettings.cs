using System.Text.Json;

using Manifold.Core.Data;

namespace Manifold.Core.Editor;

/// <summary>How a .dat file's entry names are shown: PyDAT's mode and its Simple switch.</summary>
public sealed record DatNameSetting(NamesMode Mode, bool Simple);

/// <summary>
/// The editor's settings: the StarCraft folder, the last exe opened, and per exe where its
/// strings are also written (the to-repack copy, spec §7) and where Repack takes the plugin
/// and the to-repack folder from (Repack spec §5).
/// </summary>
public sealed record EditorSettings
{
    public string? StarCraftDir { get; init; }
    public string? LastExe { get; init; }
    /// <summary>Exe path (lower case) to the to-repack copy of its stat_txt.tbl; "" for none.</summary>
    public Dictionary<string, string>? StringMirrors { get; init; }

    /// <summary>Exe path (lower case) to its to-repack folder, when not the default.</summary>
    public Dictionary<string, string>? RepackFolders { get; init; }
    /// <summary>Exe path (lower case) to the plugin Repack packs, when not the default.</summary>
    public Dictionary<string, string>? PluginPaths { get; init; }

    /// <summary>The window's restored bounds when it was last closed (QoL spec §10).</summary>
    public WindowRect? Window { get; init; }
    public bool WindowMaximized { get; init; }
    /// <summary>"strings" or "data" when that page was shown; null for the button sets.</summary>
    public string? LastPage { get; init; }
    /// <summary>The set selected when the window was closed.</summary>
    public int? LastSetId { get; init; }

    /// <summary>The .dat file shown on the Data page, a <see cref="DatId"/> name.</summary>
    public string? LastDat { get; init; }
    /// <summary>The units.dat tab shown on the Data page, a <see cref="DatTabId"/> name.</summary>
    public string? LastUnitsTab { get; init; }
    /// <summary>A <see cref="DatId"/> name to the entry last selected in that file.</summary>
    public Dictionary<string, int>? DatEntries { get; init; }
    /// <summary>A <see cref="DatId"/> name to how that file's entry names are shown.</summary>
    public Dictionary<string, DatNameSetting>? DatNames { get; init; }

    static string Key(string exePath) => Path.GetFullPath(exePath).ToLowerInvariant();
    static string ExeFolder(string exePath) => Path.GetDirectoryName(Path.GetFullPath(exePath)) ?? "";

    static string? Lookup(Dictionary<string, string>? map, string exePath) =>
        map is not null && map.TryGetValue(Key(exePath), out var value) ? value : null;

    static Dictionary<string, string> Set(Dictionary<string, string>? map, string exePath, string? value)
    {
        var result = new Dictionary<string, string>(map ?? new());
        if (value is null) result.Remove(Key(exePath));
        else result[Key(exePath)] = value;
        return result;
    }

    static Dictionary<string, T> Set<T>(Dictionary<string, T>? map, string key, T value) =>
        new(map ?? new()) { [key] = value };

    /// <summary>The entry last selected in the file; 0 when none was.</summary>
    public int EntryFor(DatId file) =>
        DatEntries is not null && DatEntries.TryGetValue(file.ToString(), out var entry) ? entry : 0;

    public EditorSettings WithEntry(DatId file, int entry) =>
        this with { DatEntries = Set(DatEntries, file.ToString(), entry) };

    /// <summary>How the file's entry names are shown; Basic, not simple, when never set.</summary>
    public DatNameSetting NamesFor(DatId file) =>
        DatNames is not null && DatNames.TryGetValue(file.ToString(), out var names) ? names : new DatNameSetting(NamesMode.Basic, false);

    public EditorSettings WithNames(DatId file, DatNameSetting names) =>
        this with { DatNames = Set(DatNames, file.ToString(), names) };

    /// <summary>The exe's to-repack folder: its override, else &lt;exe folder&gt;\to-repack.</summary>
    public string RepackFolderFor(string exePath) =>
        Lookup(RepackFolders, exePath) ?? Path.Combine(ExeFolder(exePath), "to-repack");

    /// <summary>The plugin Repack packs: its override, else &lt;exe folder&gt;\GPTP.qdp (where the build copies it).</summary>
    public string PluginPathFor(string exePath) =>
        Lookup(PluginPaths, exePath) ?? Path.Combine(ExeFolder(exePath), "GPTP.qdp");

    public bool IsDefaultRepackFolder(string exePath) => Lookup(RepackFolders, exePath) is null;
    public bool IsDefaultPluginPath(string exePath) => Lookup(PluginPaths, exePath) is null;

    /// <summary>The settings with the exe's to-repack folder set; null goes back to the default.</summary>
    public EditorSettings WithRepackFolder(string exePath, string? folder) =>
        this with { RepackFolders = Set(RepackFolders, exePath, folder) };

    /// <summary>The settings with the exe's plugin path set; null goes back to the default.</summary>
    public EditorSettings WithPluginPath(string exePath, string? path) =>
        this with { PluginPaths = Set(PluginPaths, exePath, path) };

    /// <summary>The exe's to-repack copy: a path, "" for none, null when not yet asked.</summary>
    public string? MirrorFor(string exePath) =>
        StringMirrors is not null && StringMirrors.TryGetValue(Key(exePath), out var path) ? path : null;

    /// <summary>The settings with the exe's to-repack copy set ("" for none).</summary>
    public EditorSettings WithMirror(string exePath, string mirrorPath) =>
        this with { StringMirrors = new(StringMirrors ?? new()) { [Key(exePath)] = mirrorPath } };

    /// <summary>The settings in the file, or the defaults when it is missing or unreadable.</summary>
    public static EditorSettings Load(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<EditorSettings>(File.ReadAllText(path)) ?? new EditorSettings()
                : new EditorSettings();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return new EditorSettings();
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
