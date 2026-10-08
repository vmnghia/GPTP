using Manifold.Core.Data;

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
        FunctionTable conditions, FunctionTable actions, StringTable? strings = null, StringTable? savedStrings = null)
    {
        var status = new List<string>();
        if (modExe.TryRead(ButtonSetFile.ArchivePath) is { } file)
        {
            var sets = ButtonSetFile.Read(file, out var error);
            if (sets is not null)
            {
                status.Add($"Button sets from {ButtonSetFile.ArchivePath}");
                return new Opened(new ButtonSetDocument(sets, vanilla, strings, savedStrings), status);
            }
            status.Add($"{ButtonSetFile.ArchivePath}: {error}; opened vanilla and FireGraft's sets instead");
        }
        var opened = vanilla.ToArray();
        if (modExe.TryRead(fgpPath) is { } fgpBytes)
        {
            var report = new List<string>();
            var project = FgpProject.Parse(fgpBytes);
            if (project.Sections.TryGetValue("Buts", out var buts))
            {
                var imported = ButsImport.ToSets(ButsImport.ParseButs(buts), conditions, actions,
                    id => vanilla[id].ConnectedUnit, report);
                foreach (var (id, set) in imported) opened[id] = set;
                status.Add($"Imported {imported.Count} sets from {fgpPath}");
            }
            status.AddRange(report);
        }
        else
            status.Add($"No {fgpPath}: vanilla sets");
        return new Opened(new ButtonSetDocument(opened, vanilla, strings, savedStrings), status);
    }

    /// <summary>
    /// Writes the 250 sets, and stat_txt.tbl when the strings changed, in one operation:
    /// both or neither. The table must pass its self-check first (spec §3). On a busy exe or
    /// a failed check nothing is written, the document stays dirty, and the reason is
    /// returned. <paramref name="stringsWritten"/> is the table written, for the to-repack
    /// copy; null when the strings were not saved.
    /// </summary>
    public static string? Save(IWritableArchive modExe, ButtonSetDocument document, out byte[]? stringsWritten)
    {
        stringsWritten = null;
        var files = new List<(string, byte[])> { (ButtonSetFile.ArchivePath, ButtonSetFile.Write(document.Sets)) };
        if (document.StringsDirty && document.Strings is StringTable strings)
        {
            byte[] table;
            try { table = strings.Write(); }
            catch (InvalidOperationException e) { return $"{StringTable.ArchivePath}: {e.Message}. Nothing was saved."; }
            if (strings.CheckWritten(table) is string error) return $"{StringTable.ArchivePath}: {error}. Nothing was saved.";
            files.Add((StringTable.ArchivePath, table));
            stringsWritten = table;
        }
        try
        {
            modExe.Write(files);
        }
        catch (ArchiveBusyException e)
        {
            stringsWritten = null;
            return e.Message;
        }
        document.MarkSaved();
        return null;
    }

    public static string? Save(IWritableArchive modExe, ButtonSetDocument document) => Save(modExe, document, out _);
}
