using Manifold.Core.Data;

namespace Manifold.Core.Tests;

/// <summary>FireGraft's requirement sections, requirements.bin, the import and StarCraft.exe's tables (requirements spec §2, §4).</summary>
public class ReqFileTests
{
    static FgpProject Project() => FgpProject.Parse(Fixtures.Bytes("SCManifold.fgp"));
    static Requirements Vanilla() => VanillaRequirements.FromBlock(Fixtures.Bytes("requirement-tables.bin"));

    static Requirements Imported() => RequirementsImport.FromProject(Project(), Vanilla(), new List<string>());

    /// <summary>A section of the given records: u16 count, then id, word count, words, used.</summary>
    static byte[] Section(params (int Id, ushort[] Words, byte Used)[] records)
    {
        var bytes = new List<byte>();
        bytes.AddRange(BitConverter.GetBytes((ushort)records.Length));
        foreach (var (id, words, used) in records)
        {
            bytes.Add((byte)id);
            bytes.Add((byte)words.Length);
            foreach (var w in words) bytes.AddRange(BitConverter.GetBytes(w));
            bytes.Add(used);
        }
        return bytes.ToArray();
    }

    [Fact]
    public void ProjectSectionsRoundTrip()
    {
        var project = Project();
        foreach (var table in ReqTables.All)
        {
            var section = project.Sections[ReqTables.SectionTag(table)];
            var parsed = ReqSections.Parse(table, section, out int blanks);
            Assert.Equal(0, blanks);
            Assert.Equal(section, ReqSections.Write(parsed));
        }
    }

    [Fact]
    public void BlanksDropped()
    {
        var table = ReqSections.Parse(ReqTable.Units, Section((0, [0xFF02, 0xFF00, 0x006F, 0xFFFF], 1)), out int blanks);
        Assert.Equal(1, blanks);
        Assert.Equal([new ReqLine(0xFF02, 0x6F)], table.Records[0].Lines);
    }

    [Fact]
    public void SectionErrors()
    {
        var cut = Section((0, [0xFF05, 0xFFFF], 1));
        Assert.Contains("cut short", Assert.Throws<InvalidDataException>(() => ReqSections.Parse(ReqTable.Units, cut[..^2], out _)).Message);
        var unended = Section((0, [0xFF05, 0x0001], 1));
        Assert.Contains("a record does not end with 0xFFFF",
            Assert.Throws<InvalidDataException>(() => ReqSections.Parse(ReqTable.Units, unended, out _)).Message);
        var trailing = Section((0, [0xFF05, 0xFFFF], 1)).Append((byte)0).ToArray();
        Assert.Contains("data after the last record",
            Assert.Throws<InvalidDataException>(() => ReqSections.Parse(ReqTable.Units, trailing, out _)).Message);
    }

    [Fact]
    public void WriteRefusesPastLimits()
    {
        var bigId = new RequirementTable(ReqTable.Units, [new ReqRecord(256, true, [], [])]);
        Assert.Contains("256", Assert.Throws<InvalidOperationException>(() => ReqSections.Write(bigId)).Message);
        var lines = Enumerable.Repeat(new ReqLine(0xFF05, null), 255).ToArray();   // 255 lines + 0xFFFF = 256 words
        var long_ = new RequirementTable(ReqTable.Units, [new ReqRecord(3, true, lines, [])]);
        Assert.Contains("3", Assert.Throws<InvalidOperationException>(() => ReqSections.Write(long_)).Message);
    }

    [Fact]
    public void FileRoundTrips()
    {
        var requirements = Imported();
        var bytes = RequirementsFile.Write(requirements);
        Assert.Equal("FgPa"u8.ToArray(), bytes[..4]);
        Assert.Equal(7u, BitConverter.ToUInt32(bytes, 4));
        Assert.Equal("1.16.1\0"u8.ToArray(), bytes[8..15]);
        var project = FgpProject.Parse(bytes);
        Assert.Equal(["UntR", "UpgR", "TecR", "TecU", "OrdR"], project.Sections.Keys);
        Assert.True(RequirementsFile.Read(bytes, Vanilla(), new List<string>()).SameAs(requirements));
    }

    [Fact]
    public void EmptyRecordInFile()
    {
        var requirements = Vanilla();
        requirements = requirements.With(requirements[ReqTable.Units].With(new ReqRecord(91, true, [], [])));
        var read = RequirementsFile.Read(RequirementsFile.Write(requirements), Vanilla(), new List<string>());
        var record = read[ReqTable.Units].Find(91)!;
        Assert.Empty(record.Lines);
        Assert.Equal([(ushort)0xFFFF], record.ToWords());
    }

    [Fact]
    public void FileMissingSectionIsVanilla()
    {
        var bytes = new List<byte>();
        bytes.AddRange("FgPa"u8.ToArray());
        bytes.AddRange(BitConverter.GetBytes(7u));
        bytes.AddRange("1.16.1\0"u8.ToArray());
        var section = Section((0, [0xFF05, 0xFFFF], 1));
        bytes.AddRange("UntR"u8.ToArray());
        bytes.AddRange(BitConverter.GetBytes((uint)section.Length));
        bytes.AddRange(section);
        var notes = new List<string>();
        var read = RequirementsFile.Read(bytes.ToArray(), Vanilla(), notes);
        Assert.Single(read[ReqTable.Units].Records);
        Assert.True(read[ReqTable.Orders].SameAs(Vanilla()[ReqTable.Orders]));
        Assert.Contains(notes, n => n.Contains("OrdR"));
    }

    [Fact]
    public void VanillaMatchesProjectExceptHealing()
    {
        var vanilla = Vanilla();
        var project = Imported();
        Assert.Equal([91, 51, 26, 33, 112], ReqTables.All.Select(t => vanilla[t].Records.Count));
        foreach (var table in ReqTables.All)
        {
            Assert.Equal(vanilla[table].Records.Select(r => r.Id), project[table].Records.Select(r => r.Id));
            foreach (var record in vanilla[table].Records)
            {
                bool healing = table == ReqTable.TechUse && record.Id is 24 or 34;
                Assert.Equal(!healing, record.SameAs(project[table].Find(record.Id)!));
            }
        }
    }

    [Fact]
    public void VanillaRecordsRoundTrip()
    {
        var block = Fixtures.Bytes("requirement-tables.bin");
        var vanilla = VanillaRequirements.FromBlock(block);
        foreach (var table in ReqTables.All)
        {
            // Word 0 at the table's address, then records: their words are where the block holds them.
            int at = (int)(ReqTables.Address(table) - VanillaRequirements.Start) + 2;
            foreach (var record in vanilla[table].Records)
            {
                Assert.Equal(record.Id, BitConverter.ToUInt16(block, at));
                var words = record.ToWords();
                for (int i = 0; i < words.Count; i++) Assert.Equal(words[i], BitConverter.ToUInt16(block, at + 2 + 2 * i));
                at += 2 + 2 * words.Count;
            }
            Assert.Equal(0xFFFF, BitConverter.ToUInt16(block, at));
        }
    }

    [SkippableFact]
    public void RealExeMatchesFixture()
    {
        Skip.If(Fixtures.StarCraftDir is null, "MANIFOLD_SC_DIR is not set");
        var exe = new PeImage(File.ReadAllBytes(System.IO.Path.Combine(Fixtures.StarCraftDir!, "StarCraft.exe")));
        Assert.True(VanillaRequirements.Read(exe).SameAs(Vanilla()));
    }
}
