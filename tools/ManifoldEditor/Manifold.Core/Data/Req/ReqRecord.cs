namespace Manifold.Core.Data;

/// <summary>
/// One requirement: an opcode (0xFF01-0xFFFE) with its parameter when it takes one, or a
/// unit id (below 0xFF00) on its own, which the game reads as "Must have" that unit.
/// </summary>
public readonly record struct ReqLine(ushort Word, ushort? Param);

/// <summary>An upgrade's level block (1-3): the lines that apply at that level.</summary>
public sealed record ReqLevel(int Level, IReadOnlyList<ReqLine> Lines);

/// <summary>
/// One record of a requirement table: its id, FireGraft's "Used" flag (off: left out of the
/// game's table), its lines, and an upgrade's level blocks (requirements spec §3).
/// </summary>
public sealed class ReqRecord
{
    public const ushort End = 0xFFFF;
    /// <summary>The level markers, FF1F for level 1 to FF21 for level 3.</summary>
    public const ushort FirstLevel = 0xFF1F, LastLevel = 0xFF21;

    public int Id { get; }
    public bool Used { get; }
    public IReadOnlyList<ReqLine> Lines { get; }
    public IReadOnlyList<ReqLevel> Levels { get; }

    public ReqRecord(int id, bool used, IReadOnlyList<ReqLine> lines, IReadOnlyList<ReqLevel> levels)
    {
        Id = id;
        Used = used;
        Lines = lines.ToArray();
        Levels = levels.ToArray();
    }

    public ReqRecord WithUsed(bool used) => new(Id, used, Lines, Levels);

    static bool IsLevel(ushort word) => word is >= FirstLevel and <= LastLevel;

    /// <summary>
    /// The record from its words (the last 0xFFFF included). Lines run to 0xFFFF, or, in an
    /// upgrade, to a level marker; each level runs to its own 0xFFFF. Throws
    /// <see cref="InvalidDataException"/> for words that don't end where they should.
    /// </summary>
    public static ReqRecord FromWords(ReqTable table, int id, bool used, IReadOnlyList<ushort> words)
    {
        int at = 0;
        var lines = ReadLines(table, words, ref at, levelsEnd: table == ReqTable.Upgrades);
        var levels = new List<ReqLevel>();
        if (at < words.Count && words[at] == End)
            at++;
        else
        {
            while (at < words.Count && IsLevel(words[at]))
            {
                int level = words[at] - FirstLevel + 1;
                at++;
                var levelLines = ReadLines(table, words, ref at, levelsEnd: false);
                if (at >= words.Count) throw new InvalidDataException($"record {id}: level {level} does not end with 0xFFFF");
                at++;
                levels.Add(new ReqLevel(level, levelLines));
            }
            if (levels.Count == 0) throw new InvalidDataException($"record {id} does not end with 0xFFFF");
        }
        if (at != words.Count) throw new InvalidDataException($"record {id}: words after its end");
        return new ReqRecord(id, used, lines, levels);
    }

    static List<ReqLine> ReadLines(ReqTable table, IReadOnlyList<ushort> words, ref int at, bool levelsEnd)
    {
        var lines = new List<ReqLine>();
        while (at < words.Count && words[at] != End && !(levelsEnd && IsLevel(words[at])))
        {
            ushort word = words[at++];
            ushort? param = null;
            if (ReqTables.ParamOf(table, word) != ReqParam.None)
            {
                if (at >= words.Count) throw new InvalidDataException($"0x{word:X4} has no parameter");
                param = words[at++];
            }
            lines.Add(new ReqLine(word, param));
        }
        return lines;
    }

    /// <summary>The words the game reads: lines, then 0xFFFF, or each level's marker, lines and 0xFFFF.</summary>
    public IReadOnlyList<ushort> ToWords()
    {
        var words = new List<ushort>();
        void Put(IEnumerable<ReqLine> lines)
        {
            foreach (var line in lines)
            {
                words.Add(line.Word);
                if (line.Param is ushort p) words.Add(p);
            }
        }
        Put(Lines);
        if (Levels.Count == 0) words.Add(End);
        foreach (var level in Levels)
        {
            words.Add((ushort)(FirstLevel + level.Level - 1));
            Put(level.Lines);
            words.Add(End);
        }
        return words;
    }

    /// <summary>Same id, Used flag and words.</summary>
    public bool SameAs(ReqRecord other) =>
        Id == other.Id && Used == other.Used && ToWords().SequenceEqual(other.ToWords());
}
