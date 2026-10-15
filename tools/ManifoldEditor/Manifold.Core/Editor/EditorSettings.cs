using System.Text.Json;

namespace Manifold.Core.Editor;

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
