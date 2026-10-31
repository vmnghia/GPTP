using Manifold.Core.Data;

namespace Manifold.Core.Editor;

/// <summary>Where a line is: Level 0 for the record's own lines, 1-3 for an upgrade's level block.</summary>
public readonly record struct ReqLineAt(int Level, int Index);

/// <summary>What a row of the record's view is (requirements UX spec §3).</summary>
public enum ReqRowKind
{
    /// <summary>An opcode word; its parameter, when it takes one, is the next row.</summary>
    Opcode,
    /// <summary>An opcode's unit or tech: the game reads it as that, whatever it is.</summary>
    Param,
    /// <summary>A unit on its own: "Must have".</summary>
    MustHave,
    LevelHeader,
    /// <summary>"==End of List==": where a choice appends a line.</summary>
    End,
}

/// <summary>
/// One row per word, as FireGraft shows a record: <see cref="At"/> is the line it belongs to
/// (Opcode, Param, MustHave), <see cref="Level"/> 0 for the record's own rows and 1-3 inside a level.
/// </summary>
public sealed record ReqRow(ReqRowKind Kind, ReqLineAt? At, int Level, ReqLine? Line);

/// <summary>A requirement record's edits, each giving a new record (requirements spec §6).</summary>
public static class RequirementEdits
{
    static IReadOnlyList<ReqLine> LinesAt(ReqRecord r, int level) =>
        level == 0 ? r.Lines : r.Levels.FirstOrDefault(l => l.Level == level)?.Lines
            ?? throw new ArgumentOutOfRangeException(nameof(level), $"the record has no level {level}");

    static ReqRecord WithLines(ReqRecord r, int level, Func<List<ReqLine>, List<ReqLine>> edit)
    {
        var lines = edit(LinesAt(r, level).ToList());
        return level == 0
            ? new ReqRecord(r.Id, r.Used, lines, r.Levels)
            : new ReqRecord(r.Id, r.Used, r.Lines, r.Levels.Select(l => l.Level == level ? l with { Lines = lines } : l).ToArray());
    }

    /// <summary>The line put at the index (the count: at the end).</summary>
    public static ReqRecord Insert(ReqRecord r, ReqLineAt at, ReqLine line) =>
        WithLines(r, at.Level, lines => { lines.Insert(at.Index, line); return lines; });

    public static ReqRecord Replace(ReqRecord r, ReqLineAt at, ReqLine line) =>
        WithLines(r, at.Level, lines => { lines[at.Index] = line; return lines; });

    public static ReqRecord Remove(ReqRecord r, ReqLineAt at) =>
        WithLines(r, at.Level, lines => { lines.RemoveAt(at.Index); return lines; });

    /// <summary>The line moved by <paramref name="delta"/> within its own lines; the record as it is at an end.</summary>
    public static ReqRecord Move(ReqRecord r, ReqLineAt at, int delta)
    {
        int to = at.Index + delta;
        if (to < 0 || to >= LinesAt(r, at.Level).Count) return r;
        return WithLines(r, at.Level, lines => { (lines[at.Index], lines[to]) = (lines[to], lines[at.Index]); return lines; });
    }

    /// <summary>
    /// Levels 1-3, empty, for a record that has none. Levels come as a set, as every vanilla
    /// upgrade has them: the game's parser looks for the player's level's marker past FF1F, so
    /// a missing one would send it into the next record. Throws when the record has levels.
    /// </summary>
    public static ReqRecord AddLevels(ReqRecord r)
    {
        if (r.Levels.Count > 0) throw new InvalidOperationException("the record has its levels");
        return new ReqRecord(r.Id, r.Used, r.Lines, [new ReqLevel(1, []), new ReqLevel(2, []), new ReqLevel(3, [])]);
    }

    /// <summary>The three level blocks removed, with their lines.</summary>
    public static ReqRecord RemoveLevels(ReqRecord r) => new(r.Id, r.Used, r.Lines, []);

    /// <summary>The record's rows, then End; per level its header, its rows, and End.</summary>
    public static IReadOnlyList<ReqRow> Rows(ReqRecord r)
    {
        var rows = new List<ReqRow>();
        void Lines(int level, IReadOnlyList<ReqLine> lines)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                var at = new ReqLineAt(level, i);
                if (lines[i].Word < 0xFF00)
                {
                    rows.Add(new ReqRow(ReqRowKind.MustHave, at, level, lines[i]));
                    continue;
                }
                rows.Add(new ReqRow(ReqRowKind.Opcode, at, level, lines[i]));
                if (lines[i].Param is not null) rows.Add(new ReqRow(ReqRowKind.Param, at, level, lines[i]));
            }
            rows.Add(new ReqRow(ReqRowKind.End, null, level, null));
        }
        Lines(0, r.Lines);
        foreach (var level in r.Levels)
        {
            rows.Add(new ReqRow(ReqRowKind.LevelHeader, null, level.Level, null));
            Lines(level.Level, level.Lines);
        }
        return rows;
    }

    static ReqLine LineAt(ReqRecord r, ReqLineAt at) => LinesAt(r, at.Level)[at.Index];

    /// <summary>
    /// The line's opcode set: its parameter kept when the new opcode takes the same kind, 0 when
    /// it takes another or had none, dropped when it takes none.
    /// </summary>
    public static ReqRecord SetOpcode(ReqRecord r, ReqTable table, ReqLineAt at, ushort opcode)
    {
        var old = LineAt(r, at);
        var kind = ReqTables.ParamOf(table, opcode);
        ushort? param = kind == ReqParam.None ? null
            : old.Param is ushort p && old.Word >= 0xFF00 && ReqTables.ParamOf(table, old.Word) == kind ? p : (ushort)0;
        return Replace(r, at, new ReqLine(opcode, param));
    }

    /// <summary>An opcode line's parameter, or a bare line's unit.</summary>
    public static ReqRecord SetValue(ReqRecord r, ReqLineAt at, ushort value)
    {
        var old = LineAt(r, at);
        return Replace(r, at, old.Word < 0xFF00 ? new ReqLine(value, null) : old with { Param = value });
    }

    /// <summary>An opcode line made a unit on its own: its unit parameter, else unit 0.</summary>
    public static ReqRecord ToParameter(ReqRecord r, ReqTable table, ReqLineAt at)
    {
        var old = LineAt(r, at);
        ushort unit = old.Param is ushort p && ReqTables.ParamOf(table, old.Word) == ReqParam.Unit ? p : (ushort)0;
        return Replace(r, at, new ReqLine(unit, null));
    }

    /// <summary>A unit on its own made "Current unit is" that unit.</summary>
    public static ReqRecord ToOpcode(ReqRecord r, ReqLineAt at) =>
        Replace(r, at, new ReqLine(0xFF02, LineAt(r, at).Word));

    /// <summary>What an End or Insert row's choice makes: a unit on its own, or the opcode (with parameter 0 when it takes one).</summary>
    public static ReqLine NewLine(ReqTable table, ushort opcodeOrUnit, bool parameter) =>
        parameter ? new ReqLine(opcodeOrUnit, null)
        : new ReqLine(opcodeOrUnit, ReqTables.ParamOf(table, opcodeOrUnit) == ReqParam.None ? null : (ushort)0);
}
