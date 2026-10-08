using Manifold.Core.Data;
using Manifold.Core.Editor;

namespace Manifold.Core.Tests;

public class StringListTests
{
    static (StringTable Table, ButtonSet[] Sets) Data()
    {
        var table = StringTable.Parse(Tbl.Build("Terran Marine\0*\0Ground Units\0", "o\0\u0003S\u0001iege Mode\0", "Requires #1\0"));
        var sets = Make.EmptySets();
        sets[5] = Make.Set(Make.Button(11, enabledString: 2) with { DisabledString = 3 }, Make.Button(12, enabledString: 2));
        sets[30] = Make.Set(Make.Button(4, enabledString: 2));
        return (table, sets);
    }

    static string Name(int setId) => setId == 5 ? "Siege Tank" : $"Set {setId}";

    [Fact]
    public void Rows_show_the_PyTBL_text_and_the_uses()
    {
        var (table, sets) = Data();
        var rows = StringList.Rows(table, sets, "", false, StringFilter.All, Name);
        Assert.Equal(3, rows.Count);
        Assert.Equal(new StringRow(1, "Terran Marine<0>*<0>Ground Units", "unit name"), rows[0]);
        Assert.Equal(new StringRow(2, "o<0><3>S<1>iege Mode", "unit name; Siege Tank: 11, 12; Set 30: 4"), rows[1]);
        Assert.Equal("unit name; Siege Tank: 11 (disabled)", rows[2].Uses);   // ids 1-228 are unit names
        Assert.Contains("[unit name]", rows[0].ToString());
    }

    [Fact]
    public void Search_finds_words_codes_or_an_id()
    {
        var (table, sets) = Data();
        int[] Ids(string search, bool matchCase = false, StringFilter filter = StringFilter.All) =>
            StringList.Rows(table, sets, search, matchCase, filter, Name).Select(r => r.Id).ToArray();
        Assert.Equal([2], Ids("siege mode"));                   // readable text, codes left out
        Assert.Equal([2], Ids("<3>S"));                         // PyTBL text
        Assert.Empty(Ids("siege mode", matchCase: true));
        Assert.Equal([3], Ids("#3"));
        Assert.Equal([3], Ids("3"));
        Assert.Equal([3], Ids("<35>1"));                        // a literal # is <35> in PyTBL text
        Assert.Equal([1, 2, 3], Ids("", filter: StringFilter.UnitNames));
        Assert.Equal([2, 3], Ids("", filter: StringFilter.UsedByButtons));
        Assert.Empty(Ids("", filter: StringFilter.Edited));
        Assert.Equal([3], StringList.Rows(table.With(3, [1]), sets, "", false, StringFilter.Edited, Name).Select(r => r.Id));
    }

    [Fact]
    public void A_string_shown_by_an_enabled_button_is_a_hotkey_string()
    {
        var (_, sets) = Data();
        Assert.True(StringList.IsHotkeyString(sets, 2));
        Assert.False(StringList.IsHotkeyString(sets, 3));
        Assert.Contains(StringList.Codes, c => c.Code == 10 && c.Meaning == "Newline");
    }
}
