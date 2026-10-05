using Manifold.Core.Data;
using Manifold.Core.Editor;
using Manifold.Core.Model;

namespace Manifold.Core.Tests;

public class EditorTests
{
    static byte[] UnitsDat(Action<byte[]> fill)
    {
        var bytes = new byte[Data.UnitsDat.FileSize];
        fill(bytes);
        return bytes;
    }

    [Fact]
    public void Units_dat_gives_races_and_heroes()
    {
        var bytes = UnitsDat(b =>
        {
            b[Data.UnitsDat.GroupFlagsOffset + 0] = 0x02 | 0x08;   // Marine: Terran, men
            b[Data.UnitsDat.GroupFlagsOffset + 37] = 0x01;         // Zergling
            b[Data.UnitsDat.GroupFlagsOffset + 60] = 0x04;         // Corsair
            b[Data.UnitsDat.GroupFlagsOffset + 20] = 0x02;         // Jim Raynor (Marine), a hero
            BitConverter.TryWriteBytes(b.AsSpan(Data.UnitsDat.BasePropertyOffset + 20 * 4), 0x40u);
        });
        var units = Data.UnitsDat.Parse(bytes);
        Assert.Equal(Race.Terran, units.RaceOf(0));
        Assert.Equal(Race.Zerg, units.RaceOf(37));
        Assert.Equal(Race.Protoss, units.RaceOf(60));
        Assert.Equal(Race.None, units.RaceOf(100));
        Assert.True(units.IsHero(20));
        Assert.False(units.IsHero(0));
        Assert.Throws<InvalidDataException>(() => Data.UnitsDat.Parse(new byte[100]));
    }

    static StatTxt Strings(params (int Id, string Text)[] entries)
    {
        int count = entries.Max(e => e.Id);
        var table = new string[count];
        for (int i = 0; i < count; i++) table[i] = "";
        foreach (var (id, text) in entries) table[id - 1] = text;
        var body = new List<byte>();
        var offsets = new List<int>();
        int start = 2 + 2 * count;
        foreach (var s in table)
        {
            offsets.Add(start + body.Count);
            body.AddRange(System.Text.Encoding.Latin1.GetBytes(s));
            body.Add(0);
        }
        var bytes = new List<byte> { (byte)count, (byte)(count >> 8) };
        foreach (var o in offsets) { bytes.Add((byte)o); bytes.Add((byte)(o >> 8)); }
        bytes.AddRange(body);
        return StatTxt.Parse(bytes.ToArray());
    }

    [Fact]
    public void The_set_list_groups_and_filters()
    {
        var units = Data.UnitsDat.Parse(UnitsDat(b =>
        {
            b[Data.UnitsDat.GroupFlagsOffset + 0] = 0x02;
            b[Data.UnitsDat.GroupFlagsOffset + 37] = 0x01;
            b[Data.UnitsDat.GroupFlagsOffset + 20] = 0x02;
            BitConverter.TryWriteBytes(b.AsSpan(Data.UnitsDat.BasePropertyOffset + 20 * 4), 0x40u);
        }));
        var strings = Strings((1, "Terran Marine"), (21, "Jim Raynor (Marine)"), (38, "Zerg Zergling"));
        var catalog = SetCatalog.Build(id => strings.Get(id), units);
        Assert.Equal(Card.SetCount, catalog.Entries.Count);
        Assert.Equal(new SetEntry(0, "Terran Marine", SetGroup.Terran), catalog.Entries[0]);
        Assert.Equal(SetGroup.NeutralAndHeroes, catalog.Entries[20].Group);
        Assert.Equal(SetGroup.Zerg, catalog.Entries[37].Group);
        Assert.Equal(SetGroup.Menus, catalog.Entries[230].Group);
        Assert.Equal("Unit 5", catalog.Entries[5].Name);         // no string: the id
        Assert.Equal("(empty)", catalog.Entries[228].Name);

        var groups = catalog.Filter("marine");
        Assert.Equal(new[] { SetGroup.Terran, SetGroup.NeutralAndHeroes }, groups.Select(g => g.Group));
        Assert.Equal(new[] { 0 }, groups[0].Entries.Select(e => e.Id));
        Assert.Equal(new[] { 37 }, catalog.Filter("37").SelectMany(g => g.Entries).Select(e => e.Id));
        Assert.Equal(new[] { SetGroup.Terran, SetGroup.Zerg, SetGroup.NeutralAndHeroes, SetGroup.Menus },
            catalog.Filter("").Select(g => g.Group));
        // Without units.dat: one group for every unit set.
        var flat = SetCatalog.Build(id => strings.Get(id), null);
        Assert.Equal(SetGroup.Units, flat.Entries[37].Group);
    }

    [Fact]
    public void The_card_has_15_cells_with_badges()
    {
        var set = Make.Set(Make.Button(1, icon: 10), Make.Button(12, icon: 20), Make.Button(1, icon: 30));
        var cells = CardLayout.Cells(set);
        Assert.Equal(15, cells.Count);
        Assert.Equal(new[] { 0, 2 }, cells[0].ButtonIndexes);
        Assert.Equal(10, cells[0].Icon);
        Assert.Equal(2, cells[0].Count);
        Assert.Equal((2, 1), (cells[11].Row, cells[11].Column));
        Assert.Equal(20, cells[11].Icon);
        Assert.Null(cells[5].Icon);
        Assert.True(cells[11].HiddenInReplays);
        Assert.False(cells[8].HiddenInReplays);
    }

    [Fact]
    public void Button_text_drops_the_hotkey_and_control_codes()
    {
        Assert.Equal("Move", ButtonText.Display("m\u0001Move"));
        Assert.Equal("Stim Packs", ButtonText.Display("t\u0004Stim Packs"));
        Assert.Equal("Requires: Academy", ButtonText.Display("x\u0001Requires:\u0002 Academy"));
        Assert.Equal("", ButtonText.Display(null));
    }

    static EditorState State(ButtonSet set5)
    {
        var sets = Make.EmptySets();
        sets[5] = set5;
        var strings = Strings((1, "o\u0001Siege Mode"), (2, "t\u0001Tank Mode"), (3, "o\u0001Other"));
        return new EditorState(new ButtonSetDocument(sets, Make.EmptySets()), "SCManifold.exe",
            id => strings.Get(id), Fixtures.Conditions(), Fixtures.Actions());
    }

    [Fact]
    public void Commands_edit_the_selected_set_and_undo_selects_the_set_it_changed()
    {
        var state = State(Make.Set(Make.Button(1, enabledString: 1), Make.Button(2, enabledString: 2)));
        state.SelectedSetId = 5;
        Assert.Equal("Manifold Editor - SCManifold.exe", state.Title);
        Assert.True(state.Drop(1, 11, copy: false));
        Assert.Equal("Manifold Editor - SCManifold.exe *", state.Title);
        Assert.True(state.Drop(2, 12, copy: true));
        Assert.Equal(new ushort[] { 11, 2, 12 }, state.SelectedSet.Buttons.Select(b => b.Position));

        state.CopyButton(0);
        Assert.True(state.PasteButton(15));
        Assert.Equal(15, state.SelectedSet.Buttons[3].Position);
        Assert.True(state.DeleteButton(3));

        state.CopySet();
        state.SelectedSetId = 7;
        Assert.True(state.PasteSet());
        Assert.Equal(3, state.SelectedSet.Buttons.Count);
        state.SelectedSetId = 0;
        Assert.True(state.Undo());
        Assert.Equal(7, state.SelectedSetId);                  // undo shows the set it changed
        Assert.True(state.Undo());
        Assert.Equal(5, state.SelectedSetId);

        Assert.True(state.Revert(toVanilla: true));
        Assert.Empty(state.SelectedSet.Buttons);
        Assert.False(state.Revert(toVanilla: true));           // nothing left to change
        Assert.False(state.PasteButton(0));                    // no such position
    }

    [Fact]
    public void Editing_a_button_is_one_step()
    {
        var state = State(Make.Set(Make.Button(1, icon: 3)));
        state.SelectedSetId = 5;
        var edited = state.SelectedSet.Buttons[0] with { Icon = 99, ActionVar = 4 };
        Assert.True(state.EditButton(0, edited));
        Assert.Equal(99, state.SelectedSet.Buttons[0].Icon);
        Assert.False(state.EditButton(0, edited));             // the same again: no step
        Assert.True(state.Undo());
        Assert.Equal(3, state.SelectedSet.Buttons[0].Icon);
    }

    [Fact]
    public void The_check_box_lists_hotkeys_clashes_and_replay_hidden_buttons()
    {
        var state = State(Make.Set(Make.Button(1, enabledString: 1), Make.Button(12, enabledString: 3),
            Make.Button(2, enabledString: 2)));
        state.SelectedSetId = 5;
        Assert.Equal(new[]
        {
            "O: Siege Mode (1), Other (12)",
            "Not shown in replays: Other (12)",
        }, state.CheckLines());
        Assert.Equal(new[] { "1  O  Siege Mode", "12  O  Other", "2  T  Tank Mode" }, state.ButtonLines());
    }

    [Fact]
    public void Functions_show_name_and_address()
    {
        var state = State(Make.Set());
        Assert.Equal("Move (0x424440)", state.ActionLabel(0x424440));
        Assert.Equal("Mixed Group - Move/Patrol/Hold Position (0x428DA0)", state.ConditionLabel(0x428DA0));
        Assert.Equal("0x00500000", state.ConditionLabel(0x500000));
    }

    [Fact]
    public void Settings_round_trip_and_survive_a_bad_file()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"manifold-settings-{Guid.NewGuid():N}.json");
        try
        {
            Assert.Equal(new EditorSettings(), EditorSettings.Load(path));
            new EditorSettings { StarCraftDir = @"D:\Games\Starcraft 1.16.1", LastExe = @"D:\SC\SCManifold.exe" }.Save(path);
            var loaded = EditorSettings.Load(path);
            Assert.Equal(@"D:\Games\Starcraft 1.16.1", loaded.StarCraftDir);
            Assert.Equal(@"D:\SC\SCManifold.exe", loaded.LastExe);
            File.WriteAllText(path, "{ not json");
            Assert.Equal(new EditorSettings(), EditorSettings.Load(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Resources_record_where_each_came_from_and_what_is_missing()
    {
        var mod = new FakeArchive("SCManifold.exe");
        var stardat = new FakeArchive("StarDat.mpq", new()
        {
            [EditorResources.StatTxtPath] = BuildTbl("Terran Marine"),
            [EditorResources.UnitsDatPath] = new byte[Data.UnitsDat.FileSize],
        });
        var resources = EditorResources.Load(new ResourceResolver(new IArchive[] { mod, stardat }));
        Assert.Equal("Terran Marine", resources.Strings!.Get(1));
        Assert.NotNull(resources.Units);
        Assert.Null(resources.Icons);
        Assert.Contains($"{EditorResources.StatTxtPath}: StarDat.mpq", resources.Sources);
        Assert.Contains(resources.Notes, n => n.Contains(EditorResources.IconsPath));
    }

    static byte[] BuildTbl(string s)
    {
        var bytes = new List<byte> { 1, 0, 4, 0 };
        bytes.AddRange(System.Text.Encoding.Latin1.GetBytes(s));
        bytes.Add(0);
        return bytes.ToArray();
    }
}
