using Manifold.Core.Data;

namespace Manifold.Core.Editor;

/// <summary>Which table a copied requirement record can be pasted into (requirements UX spec §4).</summary>
public static class ReqPaste
{
    /// <summary>
    /// Why the record can't go into <paramref name="target"/>: levels outside Upgrades, or the
    /// first opcode <paramref name="source"/>'s parser reads and the target's doesn't. Null when it fits.
    /// </summary>
    public static string? Refusal(ReqRecord record, ReqTable source, ReqTable target)
    {
        if (record.Levels.Count > 0 && target != ReqTable.Upgrades) return "Only upgrades have levels";
        var fromSource = ReqOpcodes.Offered(source);
        var inTarget = ReqOpcodes.Offered(target);
        foreach (var line in record.Lines.Concat(record.Levels.SelectMany(l => l.Lines)))
        {
            if (line.Word < 0xFF00) continue;
            if (inTarget.Any(o => o.Word == line.Word && o.Word != ReqOpcodes.MustHave)) continue;
            if (fromSource.FirstOrDefault(o => o.Word == line.Word && o.Word != ReqOpcodes.MustHave) is not { } opcode) continue;
            return $"{ReqTables.Title(target)} can't read {line.Word:X4} ({opcode.Name})";
        }
        return null;
    }
}
