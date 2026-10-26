using Manifold.Core.Data;

namespace Manifold.Core.Editor;

/// <summary>The Condition and Action boxes' filter as you type (QoL spec §4).</summary>
public static class FunctionSearch
{
    /// <summary>
    /// The functions whose name or hex address contains the query (case-insensitive; a
    /// leading 0x is ignored for the address), in the table's order. A blank query lists all.
    /// </summary>
    public static IReadOnlyList<GameFunction> Filter(FunctionTable table, string query)
    {
        string q = query.Trim();
        if (q.Length == 0) return table.Functions;
        string hex = q.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? q[2..] : q;
        return table.Functions.Where(f =>
            f.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            hex.Length > 0 && f.Address.ToString("X").Contains(hex, StringComparison.OrdinalIgnoreCase)).ToArray();
    }
}
