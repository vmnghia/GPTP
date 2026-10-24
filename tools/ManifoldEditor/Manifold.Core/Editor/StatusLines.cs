namespace Manifold.Core.Editor;

/// <summary>The status bar's lines.</summary>
public static class StatusLines
{
    /// <summary>
    /// After Save as: the lines that name the old exe as where a file came from ("path: exe")
    /// name the new one, which is a copy holding the same files. Other lines stay.
    /// </summary>
    public static IReadOnlyList<string> Renamed(IReadOnlyList<string> lines, string oldName, string newName) =>
        lines.Select(line => line.EndsWith(": " + oldName, StringComparison.OrdinalIgnoreCase)
            ? line[..^oldName.Length] + newName
            : line).ToArray();
}
