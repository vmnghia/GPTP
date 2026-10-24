namespace Manifold.Core.Model;

/// <summary>
/// The Repack files dialog's rows (QoL spec §6): their ticks, the filter by path, Select all
/// (over the rows shown that carry no warning) and the count. Rows start with their default.
/// </summary>
public sealed class RepackSelection(IReadOnlyList<RepackFile> files)
{
    readonly bool[] ticked = files.Select(f => f.TickedByDefault).ToArray();

    public IReadOnlyList<RepackFile> Files { get; } = files;

    /// <summary>Rows whose path in the MPQ holds this text show; the others keep their ticks.</summary>
    public string Filter { get; set; } = "";

    public bool IsTicked(int index) => ticked[index];
    public void SetTicked(int index, bool value) => ticked[index] = value;

    public IReadOnlyList<int> Visible =>
        Enumerable.Range(0, Files.Count)
            .Where(i => Files[i].MpqPath.Contains(Filter.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();

    /// <summary>The rows Select all acts on: shown, without a warning (those are ticked one at a time).</summary>
    IReadOnlyList<int> Selectable => Visible.Where(i => Files[i].Warning is null).ToArray();

    public bool CanSelectAll => Selectable.Count > 0;

    /// <summary>True when every row it acts on is ticked, false when none is (or there are none), null when some are.</summary>
    public bool? SelectAllState
    {
        get
        {
            var rows = Selectable;
            int on = rows.Count(i => ticked[i]);
            return on == 0 ? false : on == rows.Count ? true : null;
        }
    }

    /// <summary>All ticked: unticks them; otherwise ticks them.</summary>
    public void ClickSelectAll()
    {
        bool value = SelectAllState != true;
        foreach (int i in Selectable) ticked[i] = value;
    }

    public string CountText => $"{ticked.Count(t => t)} of {Files.Count} ticked";

    public IReadOnlyList<RepackFile> Ticked => Files.Where((_, i) => ticked[i]).ToArray();
}
