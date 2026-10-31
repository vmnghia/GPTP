namespace Manifold.Core.Data;

/// <summary>
/// StarCraft.exe 1.16.1's own requirement tables (requirements spec §2): five tables of u16
/// words at 0x514178-0x51521C, each word 0, records (id, words to 0xFFFF; an upgrade's level
/// blocks go on past it), then 0xFFFF.
/// </summary>
public static class VanillaRequirements
{
    public const uint Start = 0x514178, End = 0x51521C;

    public static Requirements Read(PeImage exe) => FromBlock(exe.Read(Start, (int)(End - Start)));

    /// <summary>The tables from the bytes at <see cref="Start"/>-<see cref="End"/>. Throws <see cref="InvalidDataException"/> when one runs off the block.</summary>
    public static Requirements FromBlock(ReadOnlySpan<byte> block)
    {
        if (block.Length != End - Start) throw new InvalidDataException($"expected {End - Start} bytes at 0x{Start:X}");
        var words = new ushort[block.Length / 2];
        for (int i = 0; i < words.Length; i++) words[i] = BitConverter.ToUInt16(block[(i * 2)..]);
        var tables = new List<RequirementTable>();
        foreach (var table in ReqTables.All)
        {
            int at = (int)(ReqTables.Address(table) - Start) / 2 + 1;   // past word 0
            var records = new List<ReqRecord>();
            while (Word(words, at, table) is var id && id != ReqRecord.End)
            {
                int first = ++at;
                while (true)
                {
                    ushort w = Word(words, at++, table);
                    if (w != ReqRecord.End) continue;
                    if (at < words.Length && words[at] is >= ReqRecord.FirstLevel and <= ReqRecord.LastLevel) continue;
                    break;
                }
                records.Add(ReqRecord.FromWords(table, id, true, words[first..at]));
            }
            tables.Add(new RequirementTable(table, records));
        }
        return new Requirements(tables);
    }

    static ushort Word(ushort[] words, int at, ReqTable table) =>
        at < words.Length ? words[at] : throw new InvalidDataException($"the {ReqTables.Title(table)} table runs past 0x{End:X}");
}
