using Manifold.Core.Data;
using Manifold.Core.Editor;
using Manifold.Core.Model;

namespace Manifold.Core.Tests;

public class SlotTests
{
    // Slot 1: Siege Mode (0) and Tank Mode (1) share it; slot 2: Other (2); slot 3: empty.
    static EditorState State()
    {
        var strings = StringTable.Parse(Tbl.Build("o\0Siege Mode\0", "t\0Tank Mode\0", "x\0Other\0"));
        var sets = Make.EmptySets();
        sets[5] = Make.Set(Make.Button(1, icon: 10, enabledString: 1), Make.Button(1, icon: 11, enabledString: 2),
            Make.Button(2, icon: 12, enabledString: 3));
        return new EditorState(new ButtonSetDocument(sets, Make.EmptySets(), strings), "SCManifold.exe",
            Fixtures.Conditions(), Fixtures.Actions()) { SelectedSetId = 5 };
    }

    static ushort[] Positions(EditorState s) => s.SelectedSet.Buttons.Select(b => b.Position).ToArray();

    [Fact]
    public void Dragging_moves_one_button_out_of_a_shared_slot()
    {
        var state = State();
        Assert.Equal(1, state.DropButton(1, 3, ButtonDrop.Move));
        Assert.Equal([1, 3, 2], Positions(state));                    // Tank Mode alone to 3; Siege Mode stays
        Assert.True(state.Undo());
        Assert.Equal([1, 1, 2], Positions(state));
    }

    [Fact]
    public void A_filled_slot_swaps_unless_Alt_stacks()
    {
        var state = State();
        Assert.Equal(2, state.DropButton(2, 1, ButtonDrop.Move));
        Assert.Equal([2, 2, 1], Positions(state));                    // Other to 1; the whole stack goes to 2
        state.Undo();
        Assert.Equal(2, state.DropButton(2, 1, ButtonDrop.Join));
        Assert.Equal([1, 1, 1], Positions(state));                    // Other joins the stack
        Assert.Null(state.DropButton(2, 1, ButtonDrop.Join));         // already there: no step
    }

    [Fact]
    public void Ctrl_copies_the_button_and_Shift_moves_or_copies_the_slot()
    {
        var state = State();
        Assert.Equal(3, state.DropButton(1, 3, ButtonDrop.Copy));
        Assert.Equal([1, 1, 2, 3], Positions(state));
        Assert.Equal(11, state.SelectedSet.Buttons[3].Icon);
        state.Undo();

        Assert.Equal(1, state.DropButton(1, 3, ButtonDrop.MoveSlot));
        Assert.Equal([3, 3, 2], Positions(state));
        state.Undo();

        Assert.Equal(4, state.DropButton(1, 3, ButtonDrop.CopySlot)); // Tank Mode's copy is the second one added
        Assert.Equal([1, 1, 2, 3, 3], Positions(state));
        Assert.Equal(11, state.SelectedSet.Buttons[4].Icon);
    }

    [Fact]
    public void The_slot_box_moves_one_button_and_says_what_shares_the_slot()
    {
        var state = State();
        Assert.StartsWith("Shares the slot with Siege Mode. The first in the list whose condition passes shows;", state.SlotNote(1, 1));
        Assert.Equal("", state.SlotNote(1, 4));
        Assert.Contains("Not shown in replays", state.SlotNote(1, 12));
        Assert.True(state.SetSlot(1, 2));                             // a taken slot: stacks, no swap
        Assert.Equal([1, 2, 2], Positions(state));
        Assert.False(state.SetSlot(1, 2));
        Assert.False(state.SetSlot(1, 16));
    }

    [Fact]
    public void A_stack_fans_out_with_the_first_button_in_front()
    {
        var cells = CardLayout.Cells(State().SelectedSet);
        var stack = CardLayout.StackIcons(cells[0], 64);
        Assert.Equal(2, stack.Count);
        Assert.Equal(new StackIcon(0, 6, 6, 52 * 0.68), stack[0]);
        Assert.True(stack[1].Left > stack[0].Left && stack[1].Top > stack[0].Top);
        Assert.Single(CardLayout.StackIcons(cells[1], 64));
        Assert.Empty(CardLayout.StackIcons(cells[2], 64));

        Assert.Equal(0, CardLayout.Hit(cells[0], 64, 10, 10));         // only the front icon is there
        Assert.Equal(0, CardLayout.Hit(cells[0], 64, 30, 30));         // overlap: the front one wins
        Assert.Equal(1, CardLayout.Hit(cells[0], 64, 55, 55));         // only the back icon is there
        Assert.Equal(0, CardLayout.Hit(cells[0], 64, 1, 1));           // the margin: the first button
        Assert.Null(CardLayout.Hit(cells[2], 64, 30, 30));
    }
}
