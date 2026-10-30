using System.ComponentModel;
using Manifold.Core.Data;

namespace Manifold.Core.Model;

public enum RepackState { New, Changed, Same }

/// <summary>A file of the to-repack folder, as it would go into the exe's MPQ.</summary>
public sealed record RepackFile(string MpqPath, byte[] Data, RepackState State, string? Warning)
{
    public bool TickedByDefault => State != RepackState.Same && Warning is null;
}

/// <summary>
/// What waits to be repacked (QoL spec §7): the plugin file differs from the exe's (and is
/// newer than the exe), how many to-repack files Repack files would tick, and the status line.
/// </summary>
public sealed record RepackPending(bool PluginDiffers, bool PluginNewer, int Files, string PluginLine);

/// <summary>What Repack needs from StormLib: adding files to an MPQ, and reading one.</summary>
public interface IRepackHost
{
    /// <summary>Adds or replaces the files in the MPQ inside <paramref name="file"/> (imploded).</summary>
    void AddFiles(string file, IReadOnlyList<(string Path, byte[] Data)> files);
    IArchive Open(string file);
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

    const int ERROR_ACCESS_DENIED = 5, ERROR_SHARING_VIOLATION = 32, ERROR_LOCK_VIOLATION = 33;

    /// <summary>Another program holds the exe: .NET's file errors, or StormLib's Win32 error codes.</summary>
    internal static bool IsBusy(Exception e) =>
        e is UnauthorizedAccessException ||
        e is IOException && (e.HResult & 0xFFFF) is ERROR_SHARING_VIOLATION or ERROR_LOCK_VIOLATION or ERROR_ACCESS_DENIED ||
        e is Win32Exception { NativeErrorCode: ERROR_SHARING_VIOLATION or ERROR_LOCK_VIOLATION or ERROR_ACCESS_DENIED };

    static bool IsFileError(Exception e) => e is IOException or UnauthorizedAccessException;

    /// <summary>
    /// Repacks the exe (spec §6): copies it to &lt;exe&gt;.repacking, adds the files to the copy,
    /// rebuilds the copy around the plugin (when given), checks it, and swaps it in keeping
    /// &lt;exe&gt;.bak. Null on success; else what failed, with the exe as it was.
    /// </summary>
    public static string? Run(string exePath, IReadOnlyList<(string Path, byte[] Data)> files, byte[]? plugin, IRepackHost host)
    {
        if (files.Count == 0 && plugin is null) return null;
        string name = Path.GetFileName(exePath), work = exePath + ".repacking";
        string busy = $"{name} is in use by another program (the game, or FireGraft). Nothing was changed.";

        // The editor's own files, unless replaced on purpose: they must come through unchanged.
        var kept = new Dictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var before = host.Open(exePath);
            foreach (string path in Protected.Keys)
                if (!files.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
                    kept[path] = before.TryRead(path);
        }
        catch (Exception e)
        {
            return IsBusy(e) ? busy : $"Reading the exe: {e.Message}";
        }

        bool swapped = false;
        try
        {
            try { File.Copy(exePath, work, overwrite: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return IsBusy(e) ? busy : $"Copying the exe: {e.Message}";
            }

            if (files.Count > 0)
            {
                try { host.AddFiles(work, files); }
                catch (Exception e) { return $"Adding the files: {e.Message}"; }
            }

            if (plugin is not null)
            {
                try { File.WriteAllBytes(work, FireGraftExe.WithPlugin(File.ReadAllBytes(work), plugin)); }
                catch (Exception e) when (e is InvalidDataException || IsFileError(e)) { return $"Packing the plugin: {e.Message}"; }
            }

            if (Check(work, files, plugin, kept, host) is string problem) return $"Checking the result: {problem}";

            try { File.Replace(work, exePath, exePath + ".bak"); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return IsBusy(e) ? busy : $"Replacing the exe: {e.Message}";
            }
            swapped = true;
            return null;
        }
        finally
        {
            // Best effort: a work copy left behind is overwritten by the next repack.
            if (!swapped)
                try { File.Delete(work); }
                catch (Exception e) when (IsFileError(e)) { }
        }
    }

    /// <summary>Step 4: the new file parses with the plugin, its MPQ opens, and every file reads back.</summary>
    static string? Check(string work, IReadOnlyList<(string Path, byte[] Data)> files, byte[]? plugin,
        IReadOnlyDictionary<string, byte[]?> kept, IRepackHost host)
    {
        if (plugin is not null)
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(work); }
            catch (Exception e) when (IsFileError(e)) { return $"the new exe can't be read: {e.Message}"; }
            FireGraftExe? layout;
            try { layout = FireGraftExe.Parse(bytes); }
            catch (InvalidDataException e) { return $"the new exe doesn't read back: {e.Message}"; }
            if (layout is null || !bytes.AsSpan(layout.PluginOffset, layout.PluginSize).SequenceEqual(plugin))
                return "the plugin doesn't read back the same";
        }
        IArchive after;
        try { after = host.Open(work); }
        catch (Exception e) { return $"its MPQ doesn't open: {e.Message}"; }
        try
        {
            foreach (var (path, data) in files)
                if (after.TryRead(path) is not { } read || !read.AsSpan().SequenceEqual(data))
                    return $"{path} doesn't read back the same";
            foreach (var (path, before) in kept)
            {
                var read = after.TryRead(path);
                if (before is null ? read is not null : read is null || !read.AsSpan().SequenceEqual(before))
                    return $"{path} changed, and Repack must leave it as the editor saved it";
            }
        }
        catch (Exception e) { return $"its MPQ can't be read: {e.Message}"; }
        return null;
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

    /// <summary>
    /// The toolbar's markers and Launch's check. A missing plugin or folder is nothing
    /// pending; a busy exe or folder throws, and the caller keeps what it showed.
    /// </summary>
    public static RepackPending Pending(byte[] exe, string exePath, string pluginPath, string folder, IArchive archive)
    {
        string line = PluginStatus(exe, exePath, pluginPath);
        bool differs = false;
        FireGraftExe? layout;
        try { layout = FireGraftExe.Parse(exe); }
        catch (InvalidDataException) { layout = null; }
        bool newer = false;
        if (layout is not null && File.Exists(pluginPath))
        {
            byte[] file = File.ReadAllBytes(pluginPath), packed = layout.Plugin(exe);
            // A file the dialog refuses (not a DLL, a build still being written) is nothing to pack.
            differs = FireGraftExe.DllProblem(file) is null && !file.AsSpan().SequenceEqual(packed);
            // Newer by link time, not by file dates: every save rewrites the exe, so a plugin
            // built before the last save would look older than the exe (the review of 2026-10-24).
            newer = differs && LinkTime(file) > LinkTime(packed);
        }
        return new RepackPending(differs, newer, Scan(folder, archive).Count(f => f.TickedByDefault), line);
    }

    /// <summary>
    /// A PE's link time (its header's TimeDateStamp): the plugin's build time, as the
    /// project's linker runs without /Brepro. 0 when the header can't be read.
    /// </summary>
    static uint LinkTime(byte[] pe)
    {
        if (pe.Length < 0x40) return 0;
        int at = BitConverter.ToInt32(pe, 0x3C);
        return at < 0 || at > pe.Length - 12 ? 0 : BitConverter.ToUInt32(pe, at + 8);
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
