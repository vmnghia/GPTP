using Manifold.Core.Data;
using Manifold.Core.Editor;
using Manifold.Core.Model;

namespace Manifold.Core.Tests;

public class SetCopyTests
{
    static EditorState State(ButtonSet[] sets, int selected) =>
        new(new ButtonSetDocument(sets, Make.EmptySets()), "SCManifold.exe", Fixtures.Conditions(), Fixtures.Actions())
        { SelectedSetId = selected };

    [Fact]
    public void Copy_set_to_units_is_one_step_and_keeps_their_connected_units()
    {
        var sets = Make.EmptySets();
        sets[0] = Make.Set(Make.Button(1, icon: 7), Make.Button(11, icon: 8));
        sets[16] = new ButtonSet([Make.Button(2)], 1);                 // a hero connected to unit 1
        var state = State(sets, 0);

        Assert.Equal(2, state.CopySetTo([10, 16, 0, 10, 300]));      // itself, a repeat and a bad id are skipped
        Assert.Equal(sets[0].Buttons, state.Document[10].Buttons);
        Assert.Equal(sets[0].Buttons, state.Document[16].Buttons);
        Assert.Equal(1u, state.Document[16].ConnectedUnit);
        Assert.Equal(0, state.CopySetTo([10]));                       // already the same: no step

        state.SelectedSetId = 50;
        Assert.True(state.Undo());
        Assert.Equal(10, state.SelectedSetId);                        // shows the first set it changed
        Assert.Empty(state.Document[10].Buttons);
        Assert.Equal([Make.Button(2)], state.Document[16].Buttons);
        Assert.False(state.Document.IsDirty);
    }

    [Fact]
    public void Reimport_replaces_every_set_that_differs_in_one_step()
    {
        var current = Make.EmptySets();
        current[3] = Make.Set(Make.Button(5));
        current[7] = Make.Set(Make.Button(6));
        var state = State(current, 7);
        var imported = Make.EmptySets();
        imported[7] = Make.Set(Make.Button(6));
        imported[9] = Make.Set(Make.Button(1));

        Assert.Equal([3, 9], state.ReplaceAllSets(imported, "Re-import", apply: false));
        Assert.False(state.Document.IsDirty);
        Assert.Equal([3, 9], state.ReplaceAllSets(imported, "Re-import", apply: true));
        Assert.Empty(state.Document[3].Buttons);
        Assert.Equal(imported[9].Buttons, state.Document[9].Buttons);
        Assert.True(state.Undo());
        Assert.Equal(current[3].Buttons, state.Document[3].Buttons);
        Assert.Empty(state.Document[9].Buttons);
    }

    [Fact]
    public void Reimport_from_the_exe_reads_its_FireGraft_project_again()
    {
        const string fgp = "Firegraft\\SCManifold.fgp";
        var exe = new FakeArchive("SCManifold.exe", new() { [fgp] = Fixtures.Bytes("SCManifold.fgp") });
        var status = new List<string>();
        var sets = EditorSession.ImportFireGraft(exe, fgp, Make.EmptySets(), Fixtures.Conditions(), Fixtures.Actions(), status);
        Assert.Equal(Card.SetCount, sets.Length);
        Assert.Equal(6, sets[0].Buttons.Count);
        Assert.Empty(sets[11].Buttons);
        Assert.Equal([$"Imported FireGraft's button sets for 64 sets from {fgp}"], status);
    }
}
