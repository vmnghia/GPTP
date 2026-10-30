using Manifold.Core.Data;
using Manifold.Core.Editor;
using Manifold.Core.Model;

namespace Manifold.Core.Tests;

public class ButtonTemplateTests
{
    static string? Text(ushort id) => id switch { 5 => "SStim Packs", 6 => "RRally", _ => null };

    [Fact]
    public void Blank_comes_first_with_its_fields()
    {
        var blank = Assert.Single(ButtonTemplates.Build(Make.EmptySets(), Make.EmptySets(), _ => null));
        Assert.Equal("Blank", blank.Name);
        Assert.Equal(0, blank.Uses);
        Assert.Equal(new Button(0, 0, 0x4282D0, 0x424440, 0, 0, 0, 0), blank.Button);
    }

    [Fact]
    public void Same_button_in_two_slots_is_one_row_counting_sets()
    {
        var sets = Make.EmptySets();
        sets[0] = Make.Set(Make.Button(1, icon: 7, enabledString: 5), Make.Button(4, icon: 7, enabledString: 5));
        sets[1] = Make.Set(Make.Button(9, icon: 7, enabledString: 5));
        var templates = ButtonTemplates.Build(sets, Make.EmptySets(), Text);
        Assert.Equal(2, templates.Count);
        Assert.Equal("Stim Packs", templates[1].Name);
        Assert.Equal(2, templates[1].Uses);
        Assert.Equal(0, templates[1].Button.Position);
    }

    [Fact]
    public void Vanilla_only_buttons_are_listed_with_no_uses()
    {
        var vanilla = Make.EmptySets();
        vanilla[7] = Make.Set(Make.Button(1, icon: 11));
        var templates = ButtonTemplates.Build(Make.EmptySets(), vanilla, _ => null);
        Assert.Equal(0, templates[1].Uses);
        Assert.Equal("(no tooltip) icon 11", templates[1].Name);
    }

    [Fact]
    public void Blank_itself_and_duplicates_with_vanilla_are_left_out_and_rows_sorted_by_name()
    {
        var sets = Make.EmptySets();
        sets[0] = Make.Set(new Button(3, 0, 0x4282D0, 0x424440, 0, 0, 0, 0), Make.Button(2, icon: 2, enabledString: 5));
        var vanilla = Make.EmptySets();
        vanilla[0] = Make.Set(Make.Button(5, icon: 2, enabledString: 5), Make.Button(6, icon: 3, action: 0x4244A0, enabledString: 6));
        var templates = ButtonTemplates.Build(sets, vanilla, Text);
        Assert.Equal(["Blank", "Rally", "Stim Packs"], templates.Select(t => t.Name));
        Assert.Equal(1, templates[2].Uses);
    }

    [Fact]
    public void Filter_by_name_action_condition_and_icon_keeps_blank_first()
    {
        var sets = Make.EmptySets();
        sets[0] = Make.Set(Make.Button(1, icon: 2, enabledString: 5), Make.Button(2, icon: 3, action: 0x4244A0, enabledString: 6));
        var all = ButtonTemplates.Build(sets, Make.EmptySets(), Text);
        string[] icons = ["Marine", "Ghost", "Vulture", "Wraith"];
        IEnumerable<string> Names(string q) =>
            ButtonTemplates.Filter(all, q, Fixtures.Conditions(), Fixtures.Actions(), icons).Select(t => t.Name);
        Assert.Equal(["Blank", "Stim Packs"], Names("stim"));
        Assert.Equal(["Blank", "Rally"], Names("rally point"));
        Assert.Equal(["Blank", "Rally"], Names("wraith"));
        Assert.Equal(["Blank", "Rally"], Names(" 3 "));
        Assert.Equal(["Blank", "Rally", "Stim Packs"], Names("always"));
        Assert.Equal(["Blank", "Rally", "Stim Packs"], Names(""));
        Assert.Equal(["Blank"], Names("zzz"));
    }

    static EditorState State()
    {
        var sets = Make.EmptySets();
        sets[0] = Make.Set(Make.Button(1, icon: 9));
        return new EditorState(new ButtonSetDocument(sets, Make.EmptySets()), "SCManifold.exe", Fixtures.Conditions(), Fixtures.Actions());
    }

    [Fact]
    public void AddButton_on_empty_and_filled_slots_is_one_undo_step_each()
    {
        var state = State();
        Assert.True(state.AddButton(2, ButtonTemplates.Blank));
        Assert.Equal(ButtonTemplates.Blank with { Position = 2 }, state.SelectedSet.Buttons[^1]);
        Assert.True(state.AddButton(1, Make.Button(7, icon: 4)));
        Assert.Equal([1, 2, 1], state.SelectedSet.Buttons.Select(b => (int)b.Position));
        Assert.Equal(4, state.SelectedSet.Buttons[^1].Icon);
        state.Undo();
        Assert.Equal([1, 2], state.SelectedSet.Buttons.Select(b => (int)b.Position));
        Assert.False(state.AddButton(16, ButtonTemplates.Blank));
    }
}
