using System.Text.Json;

namespace Manifold.Core.Editor;

/// <summary>
/// The editor's settings: the StarCraft folder, the last exe opened, and per exe where its
/// strings are also written (the to-repack copy, spec §7).
/// </summary>
public sealed record EditorSettings
{
    public string? StarCraftDir { get; init; }
    public string? LastExe { get; init; }
    /// <summary>Exe path (lower case) to the to-repack copy of its stat_txt.tbl; "" for none.</summary>
    public Dictionary<string, string>? StringMirrors { get; init; }

    static string Key(string exePath) => Path.GetFullPath(exePath).ToLowerInvariant();

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
