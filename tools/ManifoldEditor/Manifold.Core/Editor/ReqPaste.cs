using Manifold.Core.Data;

namespace Manifold.Core.Editor;

/// <summary>Which table a copied requirement record can be pasted into (requirements UX spec §4).</summary>
public static class ReqPaste
{
    /// <summary>
    /// Why the record can't go into <paramref name="target"/>: levels outside Upgrades, or a word
    /// the two tables' parsers read differently: an opcode only the source reads ("can't read"),
    /// or a word unknown to the source that the target reads as an opcode ("would read", which
    /// covers a parameter the target would take). Null when it fits.
    /// </summary>
    public static string? Refusal(ReqRecord record, ReqTable source, ReqTable target)
    {
        if (record.Levels.Count > 0 && target != ReqTable.Upgrades) return "Only upgrades have levels";
        static ReqOpcode? Read(ReqTable table, ushort word) =>
            ReqOpcodes.Offered(table).FirstOrDefault(o => o.Word == word && o.Word != ReqOpcodes.MustHave);
        foreach (var line in record.Lines.Concat(record.Levels.SelectMany(l => l.Lines)))
        {
            if (line.Word < 0xFF00) continue;
            var there = Read(target, line.Word);
            var here = Read(source, line.Word);
            if (here is not null && there is null)
                return $"{ReqTables.Title(target)} can't read {line.Word:X4} ({here.Name})";
            if (here is null && there is not null)
                return $"{ReqTables.Title(target)} would read {line.Word:X4} as {there.Name}";
        }
        return null;
    }
}
