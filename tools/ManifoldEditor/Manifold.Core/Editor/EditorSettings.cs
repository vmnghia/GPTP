using System.Text.Json;

namespace Manifold.Core.Editor;

/// <summary>The editor's settings: the StarCraft folder and the last exe opened.</summary>
public sealed record EditorSettings
{
    public string? StarCraftDir { get; init; }
    public string? LastExe { get; init; }

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
