namespace Manifold.Core.Data;

/// <summary>
/// A FireGraft project's requirement section (UntR, UpgR, TecR, TecU, OrdR): u16 count, then
/// per record u8 id, u8 word count, the words (the last 0xFFFF; an upgrade's levels hold inner
/// ones too), u8 "Used". The plugin's parser (<c>fgreq::buildTable</c>) reads the same layout.
/// </summary>
public static class ReqSections
{
    /// <summary>FireGraft's "None" item, left in a record when its Remove Item fails: no opcode or parameter.</summary>
    public const ushort Blank = 0xFF00;

    /// <summary>
    /// The section's records; blank items are left out and counted in <paramref name="blanks"/>.
    /// Throws <see cref="InvalidDataException"/> with the plugin's reason.
    /// </summary>
    public static RequirementTable Parse(ReqTable table, byte[] section, out int blanks)
    {
        blanks = 0;
        if (section.Length < 2) throw new InvalidDataException("cut short");
        int count = BitConverter.ToUInt16(section, 0);
        var records = new List<ReqRecord>();
        int at = 2;
        for (int r = 0; r < count; r++)
        {
            if (section.Length - at < 2) throw new InvalidDataException("cut short");
            int id = section[at], n = section[at + 1];
            at += 2;
            if (n == 0 || section.Length - at < n * 2 + 1) throw new InvalidDataException("cut short");
            if (BitConverter.ToUInt16(section, at + (n - 1) * 2) != ReqRecord.End)
                throw new InvalidDataException("a record does not end with 0xFFFF");
            var words = new List<ushort>();
            for (int i = 0; i < n; i++)
            {
                ushort word = BitConverter.ToUInt16(section, at + i * 2);
                if (word == Blank) blanks++;
                else words.Add(word);
            }
            bool used = section[at + n * 2] != 0;
            records.Add(ReqRecord.FromWords(table, id, used, words));
            at += n * 2 + 1;
        }
        if (at != section.Length) throw new InvalidDataException("data after the last record");
        return new RequirementTable(table, records);
    }

    /// <summary>The section's bytes. Throws <see cref="InvalidOperationException"/> naming a record past the format's limits.</summary>
    public static byte[] Write(RequirementTable table)
    {
        var bytes = new List<byte>();
        bytes.AddRange(BitConverter.GetBytes(checked((ushort)table.Records.Count)));
        foreach (var record in table.Records)
        {
            var words = record.ToWords();
            if (record.Id is < 0 or > 255)
                throw new InvalidOperationException($"{ReqTables.SectionTag(table.Table)} record {record.Id}: ids end at 255");
            if (words.Count > 255)
                throw new InvalidOperationException($"{ReqTables.SectionTag(table.Table)} record {record.Id}: {words.Count} words, more than 255");
            bytes.Add((byte)record.Id);
            bytes.Add((byte)words.Count);
            foreach (var word in words) bytes.AddRange(BitConverter.GetBytes(word));
            bytes.Add(record.Used ? (byte)1 : (byte)0);
        }
        return bytes.ToArray();
    }
}
