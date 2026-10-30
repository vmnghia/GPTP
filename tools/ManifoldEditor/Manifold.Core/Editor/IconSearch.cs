using Manifold.Core.Data;

namespace Manifold.Core.Editor;

/// <summary>The icon picker's search and its Unused filter (QoL spec §5).</summary>
public static class IconSearch
{
    /// <summary>Every icon some button in the sets uses.</summary>
    public static IReadOnlySet<int> Used(IEnumerable<ButtonSet> sets) =>
        sets.SelectMany(s => s.Buttons).Select(b => (int)b.Icon).ToHashSet();

    /// <summary>
    /// The frames 0..count-1 that match, in order. A query of digits matches its frame
    /// exactly; any other matches the names containing it (case-insensitive); a blank one
    /// matches all. Frames past the names have none. Unused drops the frames in use.
    /// </summary>
    public static IReadOnlyList<int> Filter(int count, IReadOnlyList<string> names, string query, bool unusedOnly, IReadOnlySet<int> used)
    {
        string q = query.Trim();
        bool number = q.Length > 0 && q.All(char.IsAsciiDigit);
        return Enumerable.Range(0, count).Where(frame =>
            (!unusedOnly || !used.Contains(frame)) &&
            (q.Length == 0
             || number && int.TryParse(q, out int n) && n == frame
             || !number && frame < names.Count && names[frame].Contains(q, StringComparison.OrdinalIgnoreCase))).ToArray();
    }
}
