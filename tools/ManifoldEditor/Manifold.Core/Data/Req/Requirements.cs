namespace Manifold.Core.Data;

/// <summary>One requirement table: its records in the order the game's table lists them.</summary>
public sealed class RequirementTable
{
    public ReqTable Table { get; }
    public IReadOnlyList<ReqRecord> Records { get; }

    public RequirementTable(ReqTable table, IReadOnlyList<ReqRecord> records)
    {
        Table = table;
        Records = records.ToArray();
    }

    public ReqRecord? Find(int id) => Records.FirstOrDefault(r => r.Id == id);

    /// <summary>The record put in place of the one with its id, else added at the end.</summary>
    public RequirementTable With(ReqRecord record)
    {
        int at = Records.ToList().FindIndex(r => r.Id == record.Id);
        var records = Records.ToList();
        if (at >= 0) records[at] = record;
        else records.Add(record);
        return new RequirementTable(Table, records);
    }

    public RequirementTable Without(int id) => new(Table, Records.Where(r => r.Id != id).ToArray());

    /// <summary>The same records, in the same order.</summary>
    public bool SameAs(RequirementTable other) =>
        Table == other.Table && Records.Count == other.Records.Count &&
        Records.Zip(other.Records).All(p => p.First.SameAs(p.Second));
}

/// <summary>The five requirement tables.</summary>
public sealed class Requirements
{
    readonly RequirementTable[] tables;

    /// <param name="tables">Five, in <see cref="ReqTables.All"/>'s order.</param>
    public Requirements(IReadOnlyList<RequirementTable> tables)
    {
        if (tables.Count != ReqTables.All.Count || tables.Where((t, i) => t.Table != ReqTables.All[i]).Any())
            throw new ArgumentException("expected the five tables in order", nameof(tables));
        this.tables = tables.ToArray();
    }

    public RequirementTable this[ReqTable table] => tables[(int)table];

    public Requirements With(RequirementTable table)
    {
        var copy = tables.ToArray();
        copy[(int)table.Table] = table;
        return new Requirements(copy);
    }

    public bool SameAs(Requirements other) => tables.Zip(other.tables).All(p => p.First.SameAs(p.Second));
}
