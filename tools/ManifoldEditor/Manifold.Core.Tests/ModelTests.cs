using Manifold.Core.Data;
using Manifold.Core.Model;

namespace Manifold.Core.Tests;

public class ModelTests
{
    static readonly Button Siege = Make.Button(1, icon: 236);
    static readonly Button Tank = Make.Button(2, icon: 237);
    static readonly Button Alt = Make.Button(1, icon: 238);

    static ButtonSetDocument Document(ButtonSet set5)
    {
        var sets = Make.EmptySets();
        sets[5] = set5;
        return new ButtonSetDocument(sets, Make.EmptySets());
    }

    public static TheoryData<string> Edits => new() { "move", "swap", "copy", "paste", "delete", "replace", "pasteSet", "revertVanilla" };

    static Func<ButtonSet, ButtonSet> EditNamed(string name) => name switch
    {
        "move" => s => CardEdits.MoveCell(s, 2, 12),
        "swap" => s => CardEdits.MoveCell(s, 1, 2),
        "copy" => s => CardEdits.CopyCell(s, 1, 15),
        "paste" => s => CardEdits.Paste(s, Tank, 9),
        "delete" => s => CardEdits.Delete(s, 0),
        "replace" => s => CardEdits.Replace(s, 1, Tank with { Icon = 99 }),
        "pasteSet" => s => CardEdits.PasteSet(s, Make.Set(Make.Button(4))),
        "revertVanilla" => _ => ButtonSet.Empty,
        _ => throw new ArgumentException(name),
    };

    [Theory, MemberData(nameof(Edits))]
    public void Every_edit_undoes_to_the_exact_set_and_redoes(string name)
    {
        var start = Make.Set(Siege, Tank, Alt);
        var document = Document(start);
        Assert.True(document.Apply(5, EditNamed(name), name));
        var after = document[5];
        Assert.False(after.SameAs(start));
        Assert.Equal(5, document.Undo()?.SetId);
        Assert.True(document[5].SameAs(start));
        Assert.Equal(5, document.Redo()?.SetId);
        Assert.True(document[5].SameAs(after));
    }

    [Fact]
    public void Moving_a_cell_moves_every_button_there_and_keeps_their_order()
    {
        var moved = CardEdits.MoveCell(Make.Set(Siege, Tank, Alt), 1, 11);
        Assert.Equal(new ushort[] { 11, 2, 11 }, moved.Buttons.Select(b => b.Position));
        var swapped = CardEdits.MoveCell(Make.Set(Siege, Tank, Alt), 1, 2);
        Assert.Equal(new ushort[] { 2, 1, 2 }, swapped.Buttons.Select(b => b.Position));
        Assert.Throws<ArgumentOutOfRangeException>(() => CardEdits.MoveCell(Make.Set(Siege), 1, 16));
    }

    [Fact]
    public void Copy_and_paste_add_after_the_other_buttons()
    {
        var copied = CardEdits.CopyCell(Make.Set(Siege, Tank, Alt), 1, 15);
        Assert.Equal(new ushort[] { 1, 2, 1, 15, 15 }, copied.Buttons.Select(b => b.Position));
        Assert.Equal(new ushort[] { 236, 238 }, copied.Buttons.Skip(3).Select(b => b.Icon));
        var pasted = CardEdits.Paste(Make.Set(Siege), Tank, 7);
        Assert.Equal(Tank with { Position = 7 }, pasted.Buttons[1]);
    }

    [Fact]
    public void Paste_set_keeps_the_targets_connected_unit()
    {
        var target = new ButtonSet(new[] { Siege }, 30);
        var result = CardEdits.PasteSet(target, new ButtonSet(new[] { Tank }, 5));
        Assert.Equal(30u, result.ConnectedUnit);
        Assert.Equal(new[] { Tank }, result.Buttons);
    }

    [Fact]
    public void History_spans_sets_and_tracks_unsaved_changes()
    {
        var document = Document(Make.Set(Siege));
        Assert.False(document.IsDirty);
        document.Apply(5, s => CardEdits.MoveCell(s, 1, 11), "move");
        document.Apply(7, s => CardEdits.Paste(s, Tank, 3), "paste");
        Assert.True(document.IsDirty);
        Assert.True(document.DiffersFromVanilla(7));
        Assert.Equal(7, document.Undo()?.SetId);
        Assert.Equal(5, document.Undo()?.SetId);
        Assert.False(document.IsDirty);
        Assert.Null(document.Undo()?.SetId);

        document.Redo();
        document.MarkSaved();
        Assert.False(document.IsDirty);
        Assert.True(document.CanRedo);           // saving keeps the history
        document.Undo();
        Assert.True(document.IsDirty);
        document.Apply(9, s => CardEdits.Paste(s, Tank, 1), "paste");
        Assert.False(document.CanRedo);          // a new edit drops the redo branch
        Assert.True(document.IsDirty);           // and the saved state can't come back by undoing
        document.Undo();
        Assert.True(document.IsDirty);
    }

    [Fact]
    public void An_edit_that_changes_nothing_is_not_a_step()
    {
        var document = Document(Make.Set(Siege));
        Assert.False(document.Apply(5, s => CardEdits.MoveCell(s, 3, 3), "move"));
        Assert.False(document.RevertToOpened(5));
        Assert.False(document.CanUndo);
    }

    [Fact]
    public void Revert_goes_to_the_opened_version_or_to_vanilla()
    {
        var document = Document(Make.Set(Siege));
        document.Apply(5, s => CardEdits.Delete(s, 0), "delete");
        Assert.True(document.RevertToOpened(5));
        Assert.True(document[5].SameAs(Make.Set(Siege)));
        Assert.True(document.RevertToVanilla(5));
        Assert.Empty(document[5].Buttons);
    }
}
