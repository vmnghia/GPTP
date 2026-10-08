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
    public void FireGrafts_sets_go_to_the_units_its_Unit_section_names()
    {
        var project = FgpProject.Parse(Fixtures.Bytes("SCManifold.fgp"));
        var links = ButsImport.ParseUnit(project.Sections["Unit"]);
        Assert.Equal(64, links.Count);
        Assert.Equal(new FgpUnitLink(0, 6, 68, 0xFFFF), links[0]);            // Marine: [68] Marine/Firebat + Heroes
        Assert.Equal(new FgpUnitLink(16, 8, 74, 1), links.Single(l => l.Entry == 16)); // Sarah Kerrigan, connected to the Ghost
        Assert.Equal(new FgpUnitLink(244, 5, 11, 0xFFFF), links.Single(l => l.Entry == 244)); // the mixed-group card: [11]

        var vanilla = Make.EmptySets();
        vanilla[16] = new ButtonSet([], 1);
        var report = new List<string>();
        var sets = ButsImport.Import(project, Fixtures.Conditions(), Fixtures.Actions(), vanilla, report);
        Assert.Empty(report);
        Assert.Equal(64, sets.Count);
        // One set per unit: Marine, Gui Montag, Firebat and the Firebat hero each get a copy of [68].
        var marine = ButsImport.ToButtons(ButsImport.ParseButs(project.Sections["Buts"]), Fixtures.Conditions(),
            Fixtures.Actions(), report)[68];
        foreach (int unit in new[] { 0, 10, 20, 32 })
            Assert.Equal(marine, sets[unit].Buttons);
        Assert.Equal(sets[5].Buttons, sets[30].Buttons);                    // Siege Tank, both modes: [76]
        Assert.Equal(new Button(1, 228, 0x428DA0, 0x424440, 0, 0, 664, 0), sets[244].Buttons[0]);
        Assert.False(sets.ContainsKey(11));                                   // the Dropship keeps vanilla's
        Assert.Equal(sets[3].Buttons, sets[68].Buttons);                      // Goliath and unit 68 share [12] Basic Commands
        Assert.Equal(1u, sets[16].ConnectedUnit);
        Assert.Equal(0u, sets[0].ConnectedUnit);                              // 0xFFFF keeps vanilla's
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
        var sets = ButsImport.ToButtons(ButsImport.ParseButs(buts.ToArray()), Fixtures.Conditions(), Fixtures.Actions(), report);
        Assert.Empty(sets);
        Assert.Equal(new[] { "FireGraft set 30: condition 67 has no address; not imported", "FireGraft set 31: position 16; not imported" }, report);
    }

    [Fact]
    public void Links_to_unchanged_sets_and_unused_sets_are_reported()
    {
        // Unit: 2 records: entry 3 -> FireGraft set 40 (not in Buts); entry 250 -> set 5.
        byte[] unit = [2, 0, 3, 5, 41, 0xFF, 0xFF, 1, 3, 1, 0, 250, 1, 6, 0xFF, 0xFF, 0];
        var links = ButsImport.ParseUnit(unit);
        var fgSets = new Dictionary<int, IReadOnlyList<Button>> { [5] = [Make.Button(1)], [7] = [Make.Button(1)] };
        var report = new List<string>();
        var sets = ButsImport.ToSets(fgSets, links, Make.EmptySets(), report);
        Assert.Empty(sets);
        Assert.Equal([
            "set 3: uses FireGraft set 40, which FireGraft didn't change or couldn't be imported; kept vanilla",
            "Unit: entry 250: no such set",
            "FireGraft set 7: no unit uses it; not imported"], report);
        Assert.Throws<InvalidDataException>(() => ButsImport.ParseUnit([1, 0, 3, 5, 41, 0xFF, 0xFF, 2, 0]));
    }
}
