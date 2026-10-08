namespace Manifold.Core.Model;

/// <summary>
/// The copy of stat_txt.tbl in the mod's to-repack folder (spec §7), from which the user adds
/// files to the exe with PyMPQ. Saving strings writes it too, so a later PyMPQ add doesn't put
/// old strings back; opening compares it with the exe's.
/// </summary>
public static class StringMirror
{
    /// <summary>&lt;exe folder&gt;\to-repack\rez\stat_txt.tbl, the folder the user repacks from.</summary>
    public static string DefaultPath(string exePath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(exePath)) ?? "", "to-repack", "rez", "stat_txt.tbl");

    /// <summary>
    /// Replaces the file, keeping the previous one as &lt;name&gt;.bak. The new bytes go to a
    /// temporary file first, so a failure leaves the old file whole.
    /// </summary>
    public static void Write(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + ".saving";
        try
        {
            File.WriteAllBytes(temp, bytes);
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
            else File.Move(temp, path);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public enum State { Same, Differs, Missing }

    /// <summary>How the to-repack copy compares with the table the exe holds (null: none in the exe).</summary>
    public static State Compare(byte[]? exeTable, string path)
    {
        if (!File.Exists(path)) return State.Missing;
        return exeTable is not null && File.ReadAllBytes(path).AsSpan().SequenceEqual(exeTable) ? State.Same : State.Differs;
    }

    /// <summary>The to-repack copy was written after the exe (for the dialog on opening).</summary>
    public static bool IsNewerThan(string path, string exePath) =>
        File.GetLastWriteTimeUtc(path) > File.GetLastWriteTimeUtc(exePath);
}
