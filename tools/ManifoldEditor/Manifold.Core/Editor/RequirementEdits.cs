using Manifold.Core.Data;

namespace Manifold.Core.Editor;

/// <summary>Where a line is: Level 0 for the record's own lines, 1-3 for an upgrade's level block.</summary>
public readonly record struct ReqLineAt(int Level, int Index);

/// <summary>One row of the record's view: a line (with where it is), or a level's header.</summary>
public sealed record ReqRow(ReqLineAt? At, int? LevelHeader, ReqLine? Line);

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

    /// <summary>The next level block (1, then 2, then 3), empty. Throws past level 3.</summary>
    public static ReqRecord AddLevel(ReqRecord r)
    {
        int next = r.Levels.Count == 0 ? 1 : r.Levels[^1].Level + 1;
        if (next > 3) throw new InvalidOperationException("an upgrade has 3 levels at most");
        return new ReqRecord(r.Id, r.Used, r.Lines, [.. r.Levels, new ReqLevel(next, [])]);
    }

    /// <summary>The last level block removed, with its lines.</summary>
    public static ReqRecord RemoveLastLevel(ReqRecord r) =>
        r.Levels.Count == 0 ? r : new ReqRecord(r.Id, r.Used, r.Lines, r.Levels.Take(r.Levels.Count - 1).ToArray());

    /// <summary>The record's lines, then per level its header and its lines.</summary>
    public static IReadOnlyList<ReqRow> Rows(ReqRecord r)
    {
        var rows = r.Lines.Select((line, i) => new ReqRow(new ReqLineAt(0, i), null, line)).ToList();
        foreach (var level in r.Levels)
        {
            rows.Add(new ReqRow(null, level.Level, null));
            rows.AddRange(level.Lines.Select((line, i) => new ReqRow(new ReqLineAt(level.Level, i), null, line)));
        }
        return rows;
    }
}
