using Manifold.Core.Data;

namespace Manifold.Core.Editor;

/// <summary>The .dat editor's entry search: by part of a name, or by id.</summary>
public static class EntrySearch
{
    /// <summary>
    /// The entries that match, in order. A blank query matches all; otherwise an entry matches
    /// when its name contains the query (any case) or its id is the number the query spells
    /// (decimal or 0x hex).
    /// </summary>
    public static IReadOnlyList<int> Filter(IReadOnlyList<string> names, string query)
    {
        string q = query.Trim();
        if (q.Length == 0) return Enumerable.Range(0, names.Count).ToArray();
        ulong? id = DatValues.ParseId(q);
        return Enumerable.Range(0, names.Count)
            .Where(i => names[i].Contains(q, StringComparison.OrdinalIgnoreCase) || (id.HasValue && (ulong)i == id.Value))
            .ToArray();
    }
}
