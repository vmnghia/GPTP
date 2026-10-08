using System.Text;
using Manifold.Core.Data;

namespace Manifold.Core.Tests;

/// <summary>
/// The string editor's step 0 (spec §8): what the mod's stat_txt.tbl holds, written to
/// tools/ManifoldEditor/reports/stat_txt-report.txt for the user to commit. Runs only with
/// MANIFOLD_STAT_TXT set to a .tbl; with MANIFOLD_SC_DIR set too, it also checks the
/// tooltip type of every vanilla button's enabled string.
/// </summary>
public class StatTxtReport
{
    const int VanillaCount = 1547;

    static string EditorDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ManifoldEditor.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("ManifoldEditor.sln not found above the test output");
    }

    [SkippableFact]
    public void Writes_the_report()
    {
        string? path = Environment.GetEnvironmentVariable("MANIFOLD_STAT_TXT");
        Skip.If(path is null, "MANIFOLD_STAT_TXT is not set");
        var bytes = File.ReadAllBytes(path!);
        var table = StringTable.Parse(bytes);
        var report = new StringBuilder();
        void Line(string text = "") => report.Append(text).Append('\n');

        Line($"stat_txt.tbl report, {DateTime.Now:yyyy-MM-dd HH:mm}");
        Line($"file: {path}");
        Line($"strings: {table.Count} (vanilla {VanillaCount})");
        Line($"size: {bytes.Length} bytes; free: {table.BytesFree} of {StringTable.Limit}");
        var written = table.Write();
        Line($"written back unchanged: {(written.AsSpan().SequenceEqual(bytes) ? "yes" : "NO")}; " +
             $"self-check: {table.CheckWritten(written) ?? "passes"}");

        var offsets = Enumerable.Range(1, table.Count).Select(id => BitConverter.ToUInt16(bytes, 2 * id)).ToArray();
        var shared = Enumerable.Range(1, table.Count).GroupBy(id => offsets[id - 1]).Where(g => g.Count() > 1).ToArray();
        Line($"offsets in id order: {(offsets.Zip(offsets.Skip(1)).All(p => p.First <= p.Second) ? "yes" : "no")}");
        Line($"shared offsets: {shared.Length}" + string.Concat(shared.Take(20).Select(g => $"\n  {string.Join(", ", g)}")));
        var noNul = Enumerable.Range(1, table.Count).Where(id => table.Segment(id) is var s && (s.Length == 0 || s[^1] != 0)).ToArray();
        Line($"without a final NUL (run on into the next): {noNul.Length}" + string.Concat(noNul.Take(20).Select(id => $"\n  {id}")));
        int multi = Enumerable.Range(1, table.Count).Count(id => table.Segment(id).Count(b => b == 0) > 1);
        Line($"with more than one NUL: {multi}");

        if (Fixtures.StarCraftDir is { } scDir)
        {
            var sets = VanillaSets.Read(new PeImage(File.ReadAllBytes(Path.Combine(scDir, "StarCraft.exe"))));
            var ids = sets.SelectMany(s => s.Buttons).Select(b => b.EnabledString).Where(id => id >= 1 && id <= table.Count).Distinct().Order();
            var byType = ids.GroupBy(id => table.Segment(id) is var s && s.Length > 1 ? s[1] : -1).OrderBy(g => g.Key);
            Line();
            Line("vanilla buttons' enabled strings, by tooltip type (the second byte):");
            foreach (var g in byType)
                Line($"  {(g.Key < 0 ? "too short" : HotkeyString.TypeLabel((byte)g.Key))}: {g.Count()}" +
                     (g.Key is < 0 or > 5 ? " -> " + string.Join(", ", g) : ""));
        }
        else
            Line("\n(set MANIFOLD_SC_DIR too to check the vanilla buttons' tooltip types)");

        Line();
        Line($"strings past vanilla's {VanillaCount}: {Math.Max(0, table.Count - VanillaCount)}");
        for (int id = VanillaCount + 1; id <= table.Count; id++)
            Line($"  {id}: {TblText.EditText(table.Segment(id))}");

        string dir = Path.Combine(EditorDir(), "reports");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "stat_txt-report.txt"), report.ToString());
    }
}
