namespace Manifold.Core.Data;

/// <summary>The five requirement tables, in the game's order (requirements spec §2).</summary>
public enum ReqTable { Units, Upgrades, TechResearch, TechUse, Orders }

/// <summary>What an opcode's parameter word is: none, a unit, or a tech.</summary>
public enum ReqParam { None, Unit, Tech }

public static class ReqTables
{
    public static IReadOnlyList<ReqTable> All { get; } =
        [ReqTable.Units, ReqTable.Upgrades, ReqTable.TechResearch, ReqTable.TechUse, ReqTable.Orders];

    /// <summary>The FireGraft project's section for the table.</summary>
    public static string SectionTag(ReqTable table) => table switch
    {
        ReqTable.Units => "UntR",
        ReqTable.Upgrades => "UpgR",
        ReqTable.TechResearch => "TecR",
        ReqTable.TechUse => "TecU",
        ReqTable.Orders => "OrdR",
        _ => throw new ArgumentOutOfRangeException(nameof(table)),
    };

    /// <summary>How many ids the game keeps an offset for (its .dat file's vanilla count).</summary>
    public static int IdCount(ReqTable table) => table switch
    {
        ReqTable.Units => 228,
        ReqTable.Upgrades => 61,
        ReqTable.TechResearch or ReqTable.TechUse => 44,
        ReqTable.Orders => 189,
        _ => throw new ArgumentOutOfRangeException(nameof(table)),
    };

    /// <summary>The .dat file whose entries name the table's ids.</summary>
    public static DatId NamesFrom(ReqTable table) => table switch
    {
        ReqTable.Units => DatId.Units,
        ReqTable.Upgrades => DatId.Upgrades,
        ReqTable.TechResearch or ReqTable.TechUse => DatId.Techdata,
        ReqTable.Orders => DatId.Orders,
        _ => throw new ArgumentOutOfRangeException(nameof(table)),
    };

    public static string Title(ReqTable table) => table switch
    {
        ReqTable.Units => "Units",
        ReqTable.Upgrades => "Upgrades",
        ReqTable.TechResearch => "Tech research",
        ReqTable.TechUse => "Tech use",
        ReqTable.Orders => "Orders",
        _ => throw new ArgumentOutOfRangeException(nameof(table)),
    };

    /// <summary>Where StarCraft.exe 1.16.1 holds the table (its word 0).</summary>
    public static uint Address(ReqTable table) => table switch
    {
        ReqTable.Units => 0x514178,
        ReqTable.Upgrades => 0x5145C0,
        ReqTable.TechResearch => 0x514908,
        ReqTable.TechUse => 0x514A48,
        ReqTable.Orders => 0x514CF8,
        _ => throw new ArgumentOutOfRangeException(nameof(table)),
    };

    /// <summary>
    /// What follows the word as its parameter, as the table's parser reads it: units
    /// (<c>canMakeUnit</c>, 0x46E1C0) read a unit after FF02, FF03 and FF04 and a tech after
    /// FF25; the other tables (0x46D610) a unit after FF02 and FF03.
    /// </summary>
    public static ReqParam ParamOf(ReqTable table, ushort word) => (table, word) switch
    {
        (_, 0xFF02 or 0xFF03) => ReqParam.Unit,
        (ReqTable.Units, 0xFF04) => ReqParam.Unit,
        (ReqTable.Units, 0xFF25) => ReqParam.Tech,
        _ => ReqParam.None,
    };
}
