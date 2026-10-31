using Manifold.Core.Data;

namespace Manifold.Core.Editor;

/// <summary>An opcode the line dialog offers: its word, its name, and what its parameter is.</summary>
public sealed record ReqOpcode(ushort Word, string Name, ReqParam Param);

/// <summary>
/// The requirement opcodes' names, read from the game's two parsers (requirements spec §3):
/// units go through <c>canMakeUnit</c>'s own copy (0x46E1C0), the other tables through
/// 0x46D610. An opcode a table's parser doesn't read is not offered there.
/// </summary>
public static class ReqOpcodes
{
    /// <summary>The dialog's "Must have" choice: its line is the unit id on its own.</summary>
    public const ushort MustHave = 0;

    static readonly (ushort Word, string Name)[] Shared =
    [
        (0xFF01, "or"),
        (0xFF02, "Current unit is"),
        (0xFF03, "Must have, counting ones being made"),
        (0xFF07, "Is not training, researching or upgrading"),
        (0xFF08, "Is not constructing an add-on"),
        (0xFF09, "Is not researching"),
        (0xFF0A, "Is not upgrading"),
        (0xFF11, "Is not burrowed"),
        (0xFF22, "Shown but disabled"),
        (0xFF23, "Never available"),
        (0xFF24, "Brood War only"),
        (0xFF26, "Is burrowed"),
    ];

    static readonly (ushort Word, string Name)[] UnitsOnly =
    [
        (0xFF04, "Has add-on"),
        (0xFF05, "Is not lifted off"),
        (0xFF0B, "Is not constructing a building"),
        (0xFF0C, "Has no add-on"),
        (0xFF0E, "Can hold another Interceptor or Scarab"),
        (0xFF10, "Silo has no nuke"),
        (0xFF25, "Has researched"),
    ];

    static readonly (ushort Word, string Name)[] OthersOnly =
    [
        (0xFF05, "Is not lifted off"),
        (0xFF06, "Is lifted off"),
        (0xFF0D, "Has no Nydus exit"),
        (0xFF0F, "Has researched this tech"),
        (0xFF12, "Responds to Battle Orders"),
        (0xFF13, "Can set a rally point"),
        (0xFF14, "Can move"),
        (0xFF15, "Can attack"),
        (0xFF16, "Is a worker"),
        (0xFF17, "Can lift off"),
        (0xFF18, "Is a transport"),
        (0xFF19, "Is a powerup"),
        (0xFF1A, "Is a subunit"),
        (0xFF1B, "Has spider mines"),
        (0xFF1C, "Is a hero"),
        (0xFF1D, "Can hold position"),
        (0xFF1E, "Allow on hallucinations"),
    ];

    static readonly Dictionary<ReqTable, IReadOnlyList<ReqOpcode>> offered = ReqTables.All.ToDictionary(t => t, Build);

    static IReadOnlyList<ReqOpcode> Build(ReqTable table)
    {
        var own = table == ReqTable.Units ? UnitsOnly : OthersOnly;
        var list = new List<ReqOpcode> { new(MustHave, "Must have", ReqParam.Unit) };
        list.AddRange(Shared.Concat(own).OrderBy(o => o.Word)
            .Select(o => new ReqOpcode(o.Word, o.Name, ReqTables.ParamOf(table, o.Word))));
        return list;
    }

    /// <summary>"Must have" first, then the table's opcodes in word order; never the level markers.</summary>
    public static IReadOnlyList<ReqOpcode> Offered(ReqTable table) => offered[table];

    /// <summary>A line in words: "Current unit is Terran Barracks", "Must have Terran Academy", "Unknown 0xFF12".</summary>
    public static string Describe(ReqTable table, ReqLine line, Func<int, string> unitName, Func<int, string> techName)
    {
        if (line.Word < 0xFF00) return $"Must have {unitName(line.Word)}";
        var opcode = offered[table].FirstOrDefault(o => o.Word == line.Word && o.Word != MustHave);
        if (opcode is null) return $"Unknown 0x{line.Word:X4}";
        return line.Param is ushort p
            ? $"{opcode.Name} {(opcode.Param == ReqParam.Tech ? techName(p) : unitName(p))}"
            : opcode.Name;
    }
}
