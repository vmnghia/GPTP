namespace Manifold.Core.Model;

/// <summary>What Launch asks before starting the game (QoL spec §8): save first, and repack first with why.</summary>
public sealed record LaunchQuestions(bool AskSave, string? RepackText);

public static class LaunchCheck
{
    /// <summary>
    /// Unsaved edits ask to save. A plugin newer than the exe's, or pending to-repack files,
    /// ask to repack, with this text. An older plugin asks nothing; without a pending check
    /// (it failed) only the save is asked.
    /// </summary>
    public static LaunchQuestions Questions(bool dirty, RepackPending? pending, string pluginName)
    {
        string? plugin = pending is { PluginNewer: true } ? $"{pluginName} is newer than the exe's plugin" : null;
        string? files = pending is { Files: > 0 } p
            ? $"{p.Files} to-repack {(p.Files == 1 ? "file is" : "files are")} new or changed"
            : null;
        string? text = (plugin, files) switch
        {
            (null, null) => null,
            (not null, null) => plugin + ".",
            (null, not null) => char.ToUpperInvariant(files[0]) + files[1..] + ".",
            _ => $"{plugin}, and {files}.",
        };
        return new LaunchQuestions(dirty, text);
    }
}
