using System.Text;
using Manifold.Core.Data;
using Manifold.Core.Editor;

namespace Manifold.Core.Model;

/// <summary>Opening a mod exe's button sets, and saving them back (spec §4, §5, §6).</summary>
public static class EditorSession
{
    public sealed record Opened(ButtonSetDocument Document, IReadOnlyList<string> Status);

    /// <summary>
    /// The FireGraft project in the exe: the one named after the exe, else the first
    /// Firegraft\*.fgp it holds (a renamed copy keeps the original's project), else the
    /// exe's name, reported missing when opened.
    /// </summary>
    public static string FindFgp(IArchive modExe, string exeFileName)
    {
        string named = $"Firegraft\\{Path.GetFileNameWithoutExtension(exeFileName)}.fgp";
        if (modExe.TryRead(named) is not null) return named;
        return modExe.List("Firegraft\\*.fgp").FirstOrDefault() ?? named;
    }

    /// <summary>
    /// The sets of the mod exe: its <c>Manifold\buttonsets.bin</c> if it has one; else
    /// vanilla plus the FireGraft project's <c>Buts</c> sets. A file that fails its
    /// checks is reported and the sets fall back the same way.
    /// </summary>
    public static Opened Open(IArchive modExe, string fgpPath, IReadOnlyList<ButtonSet> vanilla,
        FunctionTable conditions, FunctionTable actions, StringTable? strings = null, StringTable? savedStrings = null, DatSet? dats = null)
    {
        var status = new List<string>();
        if (modExe.TryRead(ButtonSetFile.ArchivePath) is { } file)
        {
            var sets = ButtonSetFile.Read(file, out var error);
            if (sets is not null)
            {
                status.Add($"Button sets from {ButtonSetFile.ArchivePath}");
                return new Opened(new ButtonSetDocument(sets, vanilla, strings, savedStrings, dats), status);
            }
            status.Add($"{ButtonSetFile.ArchivePath}: {error}; opened vanilla and FireGraft's sets instead");
        }
        var opened = ImportFireGraft(modExe, fgpPath, vanilla, conditions, actions, status);
        return new Opened(new ButtonSetDocument(opened, vanilla, strings, savedStrings, dats), status);
    }

    /// <summary>
    /// Vanilla's sets with the FireGraft project's changes placed on the units that use
    /// them (spec §2 as corrected 2026-10-08): what an exe without buttonsets.bin opens as,
    /// and what Re-import FireGraft's sets gives back.
    /// </summary>
    public static ButtonSet[] ImportFireGraft(IArchive modExe, string fgpPath, IReadOnlyList<ButtonSet> vanilla,
        FunctionTable conditions, FunctionTable actions, List<string> status)
    {
        var sets = vanilla.ToArray();
        if (modExe.TryRead(fgpPath) is not { } fgpBytes)
        {
            status.Add($"No {fgpPath}: vanilla sets");
            return sets;
        }
        var report = new List<string>();
        var imported = ButsImport.Import(FgpProject.Parse(fgpBytes), conditions, actions, vanilla, report);
        foreach (var (id, set) in imported) sets[id] = set;
        status.Add($"Imported FireGraft's button sets for {imported.Count} sets from {fgpPath}");
        status.AddRange(report);
        return sets;
    }

    /// <summary>
    /// Writes the 250 sets, and stat_txt.tbl when the strings changed, in one operation:
    /// both or neither. The table must pass its self-check first (spec §3). On a busy exe or
    /// a failed check nothing is written, the document stays dirty, and the reason is
    /// returned. <paramref name="stringsWritten"/> is the table written, for the to-repack
    /// copy; null when the strings were not saved.
    /// </summary>
    public static string? Save(IWritableArchive modExe, ButtonSetDocument document, out byte[]? stringsWritten) =>
        Save(modExe, document, out stringsWritten, out _);

    /// <summary>
    /// <see cref="Save(IWritableArchive, ButtonSetDocument, out byte[])"/> that also writes the
    /// .dat files edited since the last save, and returns them (<paramref name="datsWritten"/>)
    /// for their to-repack copies.
    /// </summary>
    public static string? Save(IWritableArchive modExe, ButtonSetDocument document, out byte[]? stringsWritten,
        out IReadOnlyList<(DatId File, byte[] Bytes)> datsWritten)
    {
        stringsWritten = null;
        datsWritten = [];
        if (FilesToSave(document, out var files, out var table, out var dats) is string problem) return problem;
        try
        {
            modExe.Write(files);
        }
        catch (ArchiveBusyException e)
        {
            return e.Message;
        }
        document.MarkSaved();
        stringsWritten = table;
        datsWritten = dats;
        return null;
    }

    /// <summary>The edited .dat files that are extended where the plugin doesn't read the extension: the game refuses them.</summary>
    public static IReadOnlyList<DatId> RefusedExtended(ButtonSetDocument document) =>
        DatFormats.All.Select(f => f.Id)
            .Where(id => document.DatDirty(id) && document.Dat(id)!.IsExpanded && !DatFormats.PluginExtends(id)).ToArray();

    /// <summary>
    /// What a save writes: the 250 sets, and stat_txt.tbl when the strings changed, once the
    /// table passes its self-check (spec §3). The problem when it doesn't; null otherwise.
    /// </summary>
    static string? FilesToSave(ButtonSetDocument document, out List<(string, byte[])> files, out byte[]? table,
        out List<(DatId File, byte[] Bytes)> dats)
    {
        files = [(ButtonSetFile.ArchivePath, ButtonSetFile.Write(document.Sets))];
        table = null;
        dats = [];
        if (document.StringsDirty && document.Strings is StringTable strings)
        {
            try { table = strings.Write(); }
            catch (InvalidOperationException e) { table = null; return $"{StringTable.ArchivePath}: {e.Message}. Nothing was saved."; }
            if (strings.CheckWritten(table) is string error) { table = null; return $"{StringTable.ArchivePath}: {error}. Nothing was saved."; }
            files.Add((StringTable.ArchivePath, table));
        }
        foreach (var format in DatFormats.All)
        {
            if (!document.DatDirty(format.Id)) continue;
            var bytes = document.Dat(format.Id)!.Save();
            dats.Add((format.Id, bytes));
            files.Add((format.ArchivePath, bytes));
        }
        foreach (var format in DatFormats.All)
        {
            // Only the files whose overrides changed; emptied ones are written empty (an MPQ file can't be removed here).
            if (document.OverridesDirty(format.Id))
                files.Add((NameOverrides.ArchivePath(format.Id), document.Overrides(format.Id).ToBytes()));
        }
        return null;
    }

    /// <summary>Why Save as can't write to <paramref name="target"/> (QoL spec §9); null when it can.</summary>
    public static string? SaveAsProblem(string source, string target)
    {
        string from = Path.GetFullPath(source), to = Path.GetFullPath(target);
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) return "That is the open exe: use Save.";
        if (string.Equals(from + ".bak", to, StringComparison.OrdinalIgnoreCase)) return "That is the open exe's backup.";
        return null;
    }

    /// <summary>
    /// Save as (QoL spec §9): the open exe copied to &lt;target&gt;.saving, the save's files
    /// added to the copy, and the copy made the target (an existing target kept as .bak; an
    /// empty one, as the save picker may leave, counts as new). Any failure leaves the target
    /// as it was (a new or empty one removed) and the edits unsaved. Null on success, with
    /// the document marked saved; else why not.
    /// </summary>
    public static string? SaveAs(string source, string target, ButtonSetDocument document, IRepackHost host, out byte[]? stringsWritten) =>
        SaveAs(source, target, document, host, out stringsWritten, out _);

    /// <summary><see cref="SaveAs(string, string, ButtonSetDocument, IRepackHost, out byte[])"/> that also reports the .dat files it wrote.</summary>
    public static string? SaveAs(string source, string target, ButtonSetDocument document, IRepackHost host, out byte[]? stringsWritten,
        out IReadOnlyList<(DatId File, byte[] Bytes)> datsWritten)
    {
        stringsWritten = null;
        datsWritten = [];
        if (SaveAsProblem(source, target) is string refused) return refused;
        if (FilesToSave(document, out var files, out var table, out var dats) is string problem) return problem;
        string name = Path.GetFileName(target), work = target + ".saving";
        bool existed = File.Exists(target) && new FileInfo(target).Length > 0;
        bool done = false;
        try
        {
            File.Copy(source, work, overwrite: true);
            host.AddFiles(work, files);
            if (existed) File.Replace(work, target, target + ".bak");
            else File.Move(work, target, overwrite: true);
            done = true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return Repack.IsBusy(e) ? $"{name} is in use by another program. Nothing was saved." : $"Saving {name}: {e.Message}";
        }
        finally
        {
            // Best effort: a copy left behind is overwritten by the next Save as.
            try
            {
                if (File.Exists(work)) File.Delete(work);
                if (!done && !existed && File.Exists(target)) File.Delete(target);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        document.MarkSaved();
        stringsWritten = table;
        datsWritten = dats;
        return null;
    }

    public static string? Save(IWritableArchive modExe, ButtonSetDocument document) => Save(modExe, document, out _);
}
