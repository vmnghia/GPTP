using System.Text;
using Manifold.Core.Data;
using Manifold.Core.Editor;
using Manifold.Core.Model;

namespace Manifold.Core.Tests;

public class StringEditTests
{
    static string Text(StringTable table, int id) => Encoding.Latin1.GetString(table.Segment(id));

    /// <summary>
    /// Set 5: Siege Mode and Tank Mode, then a third button sharing Siege Mode's string;
    /// set 7: one more button sharing it. String 4 is a disabled text.
    /// </summary>
    static EditorState State()
    {
        var table = StringTable.Parse(Tbl.Build("o\0Siege Mode\0", "t\0Tank Mode\0", "x\0Other\0", "Requires Machine Shop\0"));
        var sets = Make.EmptySets();
        sets[5] = Make.Set(
            Make.Button(1, enabledString: 1) with { DisabledString = 4 },
            Make.Button(2, enabledString: 2),
            Make.Button(3, enabledString: 1));
        sets[7] = Make.Set(Make.Button(1, enabledString: 1));
        return new EditorState(new ButtonSetDocument(sets, Make.EmptySets(), table), "SCManifold.exe",
            Fixtures.Conditions(), Fixtures.Actions()) { SelectedSetId = 5 };
    }

    [Fact]
    public void A_string_edit_is_one_step_and_names_follow_it()
    {
        var state = State();
        Assert.Equal("Siege Mode", state.NameOf(0));
        Assert.True(state.EditString(1, Tbl.Latin1("s\0Siege Tank Mode"), out var refused));
        Assert.Null(refused);
        Assert.Equal("s\0Siege Tank Mode\0", Text(state.Strings!, 1));
        Assert.Equal("Siege Tank Mode", state.NameOf(0));
        Assert.Equal('S', CardChecks.Hotkeys(state.SelectedSet, state.Text)[0]);
        Assert.True(state.Document.IsDirty);
        Assert.True(state.Document.StringsDirty);

        // An edit that changes nothing is not a step.
        Assert.False(state.EditString(1, Tbl.Latin1("s\0Siege Tank Mode\0"), out _));

        state.SelectedSetId = 0;
        Assert.True(state.Undo());
        Assert.Equal(1, state.LastStringId);
        Assert.Equal(0, state.SelectedSetId);            // a string step leaves the set alone
        Assert.Equal("o\0Siege Mode\0", Text(state.Strings!, 1));
        Assert.False(state.Document.StringsDirty);
        Assert.False(state.Document.IsDirty);
        Assert.True(state.Redo());
        Assert.Equal("s\0Siege Tank Mode\0", Text(state.Strings!, 1));
    }

    [Fact]
    public void Shared_strings_list_the_other_buttons()
    {
        var state = State();
        Assert.Equal([new ButtonRef(5, 2, StringField.Enabled), new ButtonRef(7, 0, StringField.Enabled)],
            state.SharedWith(0, StringField.Enabled));
        Assert.Empty(state.SharedWith(1, StringField.Enabled));
        Assert.Empty(state.SharedWith(0, StringField.Disabled));
        Assert.Empty(state.SharedWith(1, StringField.Disabled));   // id 0: nothing
        Assert.Equal(1, StringUses.UnitOf(2));
        Assert.Null(StringUses.UnitOf(229));
    }

    [Fact]
    public void A_separate_copy_is_one_step_for_the_button_and_the_table()
    {
        var state = State();
        int? id = state.CopyStringForButton(0, StringField.Enabled, out var refused);
        Assert.Null(refused);
        Assert.Equal(5, id);
        Assert.Equal("o\0Siege Mode\0", Text(state.Strings!, 5));
        Assert.Equal(5, state.SelectedSet.Buttons[0].EnabledString);
        Assert.Equal(1, state.SelectedSet.Buttons[2].EnabledString);       // the others keep the old one
        Assert.Equal(1, state.Document[7].Buttons[0].EnabledString);
        Assert.Empty(state.SharedWith(0, StringField.Enabled));

        Assert.True(state.EditString(5, Tbl.Latin1("o\0Siege Mode (copy)"), out _));
        Assert.Equal("Siege Mode", state.NameOf(2));

        Assert.True(state.Undo());
        Assert.True(state.Undo());
        Assert.Equal(5, state.LastStringId);
        Assert.Equal(1, state.SelectedSet.Buttons[0].EnabledString);
        Assert.Equal(4, state.Strings!.Count);
    }

    [Fact]
    public void An_empty_field_gets_a_new_string()
    {
        var state = State();
        Assert.Null(state.NewStringForButton(0, StringField.Disabled, out _));    // has one already
        Assert.Equal(5, state.NewStringForButton(1, StringField.Disabled, out _));
        Assert.Equal("New string\0", Text(state.Strings!, 5));
        Assert.Equal(5, state.SelectedSet.Buttons[1].DisabledString);
        Assert.Equal(6, state.AddString(out _));
        Assert.Equal(6, state.Strings!.Count);
        var hotkey = HotkeyString.Split(EditorState.NewStringBytes(StringField.Enabled))!;
        Assert.Equal((byte)'?', hotkey.Hotkey);
        Assert.Equal(0, hotkey.Type);
    }

    [Fact]
    public void An_edit_past_the_limit_is_refused_and_changes_nothing()
    {
        var state = State();
        // Two long strings fit: only the last has to start below 65,536.
        Assert.True(state.EditString(1, Enumerable.Repeat((byte)'a', 40_000).ToArray(), out _));
        Assert.True(state.EditString(2, Enumerable.Repeat((byte)'a', 40_000).ToArray(), out _));
        Assert.False(state.EditString(4, Enumerable.Repeat((byte)'b', 40_001).ToArray(), out var refused));
        Assert.Contains("doesn't fit", refused);
        Assert.Contains("string 4", refused);
        Assert.Equal("Requires Machine Shop\0", Text(state.Strings!, 4));
        Assert.True(state.Undo());
        Assert.True(state.Undo());
        Assert.False(state.Document.IsDirty);
    }

    [Fact]
    public void Revert_string_gives_back_the_opened_bytes()
    {
        var state = State();
        state.EditString(2, Tbl.Latin1("t\0Tank"), out _);
        Assert.True(state.Strings!.IsEdited(2));
        Assert.True(state.RevertString(2));
        Assert.Equal("t\0Tank Mode\0", Text(state.Strings!, 2));
        Assert.False(state.Strings!.IsEdited(2));
        Assert.False(state.RevertString(2));                      // already as opened
        int added = state.AddString(out _)!.Value;
        Assert.False(state.RevertString(added));                  // not in the opened table
        Assert.Null(state.Strings!.CheckWritten(state.Strings.Write()));
    }

    [Fact]
    public void Saving_or_choosing_another_table_moves_the_dirty_mark()
    {
        var state = State();
        state.EditString(3, Tbl.Latin1("x\0Else"), out _);
        state.Document.MarkSaved();
        Assert.False(state.Document.StringsDirty);
        Assert.True(state.Undo());
        Assert.True(state.Document.StringsDirty);                 // the exe now holds the edit

        var exeTable = StringTable.Parse(Tbl.Build("a\0"));
        var repackTable = StringTable.Parse(Tbl.Build("b\0"));
        var document = new ButtonSetDocument(Make.EmptySets(), Make.EmptySets(), repackTable, exeTable);
        Assert.True(document.StringsDirty);
        Assert.True(document.IsDirty);
    }

    [Fact]
    public void Without_strings_the_string_commands_do_nothing()
    {
        var sets = Make.EmptySets();
        sets[0] = Make.Set(Make.Button(1, enabledString: 1));
        var state = new EditorState(new ButtonSetDocument(sets, Make.EmptySets()), "x.exe",
            Fixtures.Conditions(), Fixtures.Actions());
        Assert.Null(state.Text(1));
        Assert.Equal("button 1", state.NameOf(0));
        Assert.False(state.EditString(1, [1], out _));
        Assert.Null(state.CopyStringForButton(0, StringField.Enabled, out _));
        Assert.Null(state.AddString(out _));
    }
}
