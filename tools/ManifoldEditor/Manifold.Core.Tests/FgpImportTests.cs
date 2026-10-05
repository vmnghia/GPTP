using Manifold.Core.Data;

namespace Manifold.Core.Tests;

public class FgpImportTests
{
    [Fact]
    public void Function_lists_have_FireGrafts_counts_and_addresses()
    {
        var conditions = Fixtures.Conditions();
        var actions = Fixtures.Actions();
        Assert.Equal(67, conditions.Functions.Count);
        Assert.Equal(60, actions.Functions.Count);
        Assert.Equal(new GameFunction(6, "Mixed Group - Move/Patrol/Hold Position", 0x428DA0), conditions.ByIndex(6));
        Assert.Equal(new GameFunction(9, "Move", 0x424440), actions.ByIndex(9));
        Assert.Null(conditions.ByIndex(67));
    }

    [Fact]
    public void The_users_project_has_its_sections_and_18_button_sets()
    {
        var project = FgpProject.Parse(Fixtures.Bytes("SCManifold.fgp"));
        Assert.Equal(7u, project.Version);
        Assert.Equal("1.16.1", project.GameVersion);
        Assert.Equal(2438, project.Sections["Buts"].Length);
        Assert.Contains("Unit", project.Sections.Keys);
        var sets = ButsImport.ParseButs(project.Sections["Buts"]);
        Assert.Equal(new[] { 11, 12, 13, 15, 22, 67, 68, 69, 70, 73, 74, 75, 76, 77, 78, 79, 80, 90 },
            sets.Select(s => s.SetId));
        Assert.All(sets.SelectMany(s => s.Buttons), b => Assert.InRange(b.Position, 1, 9));
    }

    [Fact]
    public void Set_11s_first_button_is_vanilla_Move()
    {
        var project = FgpProject.Parse(Fixtures.Bytes("SCManifold.fgp"));
        var report = new List<string>();
        var sets = ButsImport.ToSets(ButsImport.ParseButs(project.Sections["Buts"]),
            Fixtures.Conditions(), Fixtures.Actions(), _ => 0, report);
        Assert.Empty(report);
        Assert.Equal(18, sets.Count);
        Assert.Equal(new Button(1, 228, 0x428DA0, 0x424440, 0, 0, 664, 0), sets[11].Buttons[0]);
    }

    [Fact]
    public void A_set_that_cannot_be_imported_is_reported_and_left_out()
    {
        var buts = new List<byte> { 2, 0 };
        void AddSet(byte id, ushort position, uint condition)
        {
            buts.AddRange(new byte[] { id, 1 });
            var b = new byte[20];
            new Button(position, 1, condition, 9, 0, 0, 1, 0).Write(b);
            buts.AddRange(b);
        }
        AddSet(30, 1, 67);   // condition 67: in reqlist.txt but with no address
        AddSet(31, 16, 6);   // position 16
        var report = new List<string>();
        var sets = ButsImport.ToSets(ButsImport.ParseButs(buts.ToArray()),
            Fixtures.Conditions(), Fixtures.Actions(), _ => 0, report);
        Assert.Empty(sets);
        Assert.Equal(new[] { "set 30: condition 67 has no address; kept vanilla", "set 31: position 16; kept vanilla" }, report);
    }
}
