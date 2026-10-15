using Manifold.Core.Data;

namespace Manifold.Core.Model;

public enum RepackState { New, Changed, Same }

/// <summary>A file of the to-repack folder, as it would go into the exe's MPQ.</summary>
public sealed record RepackFile(string MpqPath, byte[] Data, RepackState State, string? Warning)
{
    public bool TickedByDefault => State != RepackState.Same && Warning is null;
}

/// <summary>
/// Repack (spec 2026-10-14): the plugin and the to-repack folder's files into the mod exe,
/// in place of FireGraft and PyMPQ.
/// </summary>
public static class Repack
{
    /// <summary>Files the editor saves: a different to-repack copy would replace its work.</summary>
    static readonly Dictionary<string, string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        [StringTable.ArchivePath] = "would replace the strings saved by the editor",
        [ButtonSetFile.ArchivePath] = "would replace the editor's button sets",
    };

    /// <summary>
    /// Every file in a subfolder of <paramref name="folder"/> but *.bak, by its path in the MPQ
    /// (relative, with '\'), compared with the exe's. A missing folder has none.
    /// </summary>
    public static IReadOnlyList<RepackFile> Scan(string folder, IArchive exe)
    {
        if (!Directory.Exists(folder)) return [];
        var files = new List<RepackFile>();
        foreach (string path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            var parts = Path.GetRelativePath(folder, path).Split('/', '\\');
            if (parts.Length < 2 || path.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) continue;
            string mpqPath = string.Join('\\', parts);
            byte[] data = File.ReadAllBytes(path);
            byte[]? current = exe.TryRead(mpqPath);
            var state = current is null ? RepackState.New
                : current.AsSpan().SequenceEqual(data) ? RepackState.Same : RepackState.Changed;
            string? warning = state == RepackState.Changed && Protected.TryGetValue(mpqPath, out var w) ? w : null;
            files.Add(new RepackFile(mpqPath, data, state, warning));
        }
        files.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.MpqPath, b.MpqPath));
        return files;
    }

    /// <summary>The plugin file's bytes, or why it can't be packed (missing, unreadable, not a DLL).</summary>
    public static (byte[]? Bytes, string? Problem) LoadPlugin(string path)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return (null, $"{path} not found");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (null, $"{path} can't be read: {e.Message}");
        }
        return FireGraftExe.DllProblem(bytes) is string problem ? (null, $"{Path.GetFileName(path)}: {problem}") : (bytes, null);
    }

    /// <summary>The status line: the plugin in the exe against the plugin file.</summary>
    public static string PluginStatus(byte[] exe, string exePath, string pluginPath)
    {
        FireGraftExe? layout;
        try { layout = FireGraftExe.Parse(exe); }
        catch (InvalidDataException e) { return $"Plugin header not understood: {e.Message}"; }
        if (layout is null) return "No FireGraft plugin header: Repack adds files only";

        string inExe = $"Plugin in the exe: {layout.PluginSize:N0} bytes";
        string file = Path.GetFileName(pluginPath);
        byte[] plugin;
        try { plugin = File.ReadAllBytes(pluginPath); }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { return $"{inExe}; {pluginPath} not found"; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return $"{inExe}; {pluginPath} can't be read"; }
        if (plugin.AsSpan().SequenceEqual(exe.AsSpan(layout.PluginOffset, layout.PluginSize))) return $"{inExe}, the same as {file}";
        string age = File.GetLastWriteTimeUtc(pluginPath) > File.GetLastWriteTimeUtc(exePath) ? "newer" : "older";
        return $"{inExe}; {file} differs ({plugin.Length:N0} bytes, {age} than the exe)";
    }
}
