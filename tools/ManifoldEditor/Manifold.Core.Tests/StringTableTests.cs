using System.Text;
using Manifold.Core.Data;

namespace Manifold.Core.Tests;

/// <summary>Builds .tbl files byte by byte, for tables no real file needs to provide.</summary>
static class Tbl
{
    public static byte[] Latin1(string text) => Encoding.Latin1.GetBytes(text);

    /// <summary>A table whose strings are the given bytes, laid out in order, each at its own offset.</summary>
    public static byte[] Build(params string[] segments) =>
        BuildAt(segments.Select((_, i) => i).ToArray(), segments);

    /// <summary>
    /// A table of the given distinct segments; id i points at segment
    /// <paramref name="segmentOfId"/>[i], so two ids can share one.
    /// </summary>
    public static byte[] BuildAt(int[] segmentOfId, params string[] segments)
    {
        int dataStart = 2 + 2 * segmentOfId.Length;
        var starts = new List<int>();
        var body = new List<byte>();
        foreach (var segment in segments)
        {
            starts.Add(dataStart + body.Count);
            body.AddRange(Latin1(segment));
        }
        var bytes = new List<byte>(BitConverter.GetBytes((ushort)segmentOfId.Length));
        foreach (int s in segmentOfId) bytes.AddRange(BitConverter.GetBytes((ushort)starts[s]));
        bytes.AddRange(body);
        return bytes.ToArray();
    }
}

public class StringTableTests
{
    static string Text(StringTable table, int id) => Encoding.Latin1.GetString(table.Segment(id));

    [Fact]
    public void Segments_run_to_the_next_distinct_offset_with_every_nul()
    {
        var table = StringTable.Parse(Tbl.Build("Terran Marine\0*\0Ground Units\0", "m\0\u0003M\u0001ove\0", "last\0junk"));
        Assert.Equal(3, table.Count);
        Assert.Equal("Terran Marine\0*\0Ground Units\0", Text(table, 1));
        Assert.Equal("m\0\u0003M\u0001ove\0", Text(table, 2));
        Assert.Equal("last\0junk", Text(table, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.Segment(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.Segment(4));
    }

    [Fact]
    public void Writing_an_unedited_table_gives_back_its_bytes()
    {
        var files = new[]
        {
            Tbl.Build("Terran Marine\0*\0Ground Units\0", "m\0\u0003M\u0001ove\0", "a\0b\0c\0", "trailing\0\0\0"),
            Tbl.BuildAt([0, 1, 0, 2], "shared\0", "own\0", "end\0"),   // ids 1 and 3 share one offset
            Tbl.Build("runs on ", "into this\0", "x\0"),               // id 1 has no NUL
            Tbl.Build(),
        };
        foreach (var bytes in files)
        {
            var table = StringTable.Parse(bytes);
            Assert.Equal(bytes, table.Write());
            Assert.Null(table.CheckWritten(table.Write()));
        }
    }

    [Fact]
    public void Offsets_out_of_order_keep_their_order_when_written()
    {
        // id 1's string comes after id 2's in the file.
        var bytes = Tbl.BuildAt([1, 0], "second\0", "first\0");
        var table = StringTable.Parse(bytes);
        Assert.Equal("first\0", Text(table, 1));
        Assert.Equal(bytes, table.Write());
    }

    [Fact]
    public void An_edit_changes_that_id_only()
    {
        var table = StringTable.Parse(Tbl.BuildAt([0, 1, 0, 2], "shared\0", "own\0", "a\0b\0"));
        var edited = table.With(1, Tbl.Latin1("new text"));
        Assert.Equal("new text\0", Text(edited, 1));         // the final NUL is added
        Assert.Equal("shared\0", Text(edited, 3));           // the id it shared with keeps it
        Assert.Equal("own\0", Text(edited, 2));
        Assert.Equal("a\0b\0", Text(edited, 4));
        Assert.True(edited.IsEdited(1));
        Assert.False(edited.IsEdited(3));
        Assert.Equal("shared\0", Text(table, 1));            // the old table is unchanged

        var written = edited.Write();
        Assert.Null(edited.CheckWritten(written));
        var read = StringTable.Parse(written);
        for (int id = 1; id <= edited.Count; id++)
            Assert.Equal(edited.Segment(id), read.Segment(id));
    }

    [Fact]
    public void A_string_running_on_into_an_edited_one_keeps_its_text()
    {
        var table = StringTable.Parse(Tbl.Build("one ", "two ", "three\0", "four\0"));
        var edited = table.With(3, Tbl.Latin1("3\0"));
        Assert.Equal("one two three\0", Text(edited, 1));
        Assert.Equal("two three\0", Text(edited, 2));
        Assert.Equal("3\0", Text(edited, 3));
        Assert.Equal("four\0", Text(edited, 4));
        var read = StringTable.Parse(edited.Write());
        Assert.Equal("one two three\0", Text(read, 1));
        Assert.Equal("four\0", Text(read, 4));
    }

    [Fact]
    public void A_string_running_on_into_an_unedited_one_stays_joined()
    {
        var table = StringTable.Parse(Tbl.Build("one ", "two\0", "three\0"));
        var edited = table.With(3, Tbl.Latin1("3"));
        Assert.False(edited.IsEdited(1));
        Assert.Equal("one ", Text(StringTable.Parse(edited.Write()), 1));
        Assert.Null(edited.CheckWritten(edited.Write()));
    }

    [Fact]
    public void Added_strings_take_the_next_ids()
    {
        var table = StringTable.Parse(Tbl.Build("a\0", "b\0"));
        var (added, id) = table.Add(Tbl.Latin1("c"));
        Assert.Equal(3, id);
        Assert.Equal(2, table.Count);
        var read = StringTable.Parse(added.Write());
        Assert.Equal(3, read.Count);
        // Edited and added strings go after the unedited ones, shortest first.
        var longer = added.With(1, Tbl.Latin1("a longer one"));
        var written = longer.Write();
        Assert.True(BitConverter.ToUInt16(written, 2) > BitConverter.ToUInt16(written, 6));
        Assert.Equal("a\0", Text(read, 1));
        Assert.Equal("c\0", Text(read, 3));
    }

    [Fact]
    public void A_table_past_the_16_bit_limit_is_refused()
    {
        var table = StringTable.Parse(Tbl.Build("a\0", "b\0", "c\0"));
        Assert.Equal(StringTable.Limit - 1 - 12, table.BytesFree);   // the last string starts at 12
        byte[] Long() => Enumerable.Repeat((byte)'x', 33_000).ToArray();
        // Two long strings fit: only the last has to start below 65,536.
        var two = table.With(1, Long()).With(2, Long());
        Assert.Null(two.FirstOverLimit());
        Assert.True(two.Write().Length > StringTable.Limit);
        var three = two.With(3, Long());
        Assert.Equal(3, three.FirstOverLimit());
        var e = Assert.Throws<InvalidOperationException>(() => three.Write());
        Assert.Contains("string 3", e.Message);
        Assert.True(three.BytesFree < 0);
    }

    [Fact]
    public void The_self_check_names_a_string_that_reads_back_different()
    {
        var table = StringTable.Parse(Tbl.Build("a\0", "b\0"));
        var other = Tbl.Build("a\0", "c\0");
        Assert.Equal("string 2 reads back different", table.CheckWritten(other));
        Assert.Contains("1 strings", table.CheckWritten(Tbl.Build("a\0")));
        Assert.Contains("doesn't read back", table.CheckWritten([5]));
    }

    [Fact]
    public void Offsets_outside_the_file_are_refused()
    {
        var bytes = Tbl.Build("a\0");
        bytes[2] = 200;
        Assert.Throws<InvalidDataException>(() => StringTable.Parse(bytes));
        Assert.Throws<InvalidDataException>(() => StringTable.Parse([3, 0, 0]));
    }
}

public class TblTextTests
{
    [Theory]
    // PyTBL's documentation (Docs/pytbl.txt).
    [InlineData("a\0\u0003A\u0001ttack\0", "a<0><3>A<1>ttack<0>")]
    [InlineData("m\u0001Train \u0003M\u0001arine\0", "m<1>Train <3>M<1>arine<0>")]
    [InlineData("t\u0003Psionic S\u0003t\u0001orm\0", "t<3>Psionic S<3>t<1>orm<0>")]
    [InlineData("Something1 Requires:\n   One Requirement\0", "Something1 Requires:<10>   One Requirement<0>")]
    [InlineData("Marine\0# <x>", "Marine<0><35> <60>x<62>")]
    [InlineData("é ÿ", "é ÿ")]
    public void Text_is_written_as_PyTBL_writes_it(string bytes, string text)
    {
        Assert.Equal(text, TblText.Decompile(Tbl.Latin1(bytes)));
        Assert.Equal(Tbl.Latin1(bytes), TblText.Compile(text, out var error));
        Assert.Null(error);
    }

    [Fact]
    public void Every_byte_survives_the_round_trip()
    {
        var all = Enumerable.Range(0, 256).Select(b => (byte)b).ToArray();
        Assert.Equal(all, TblText.Compile(TblText.Decompile(all), out _));
    }

    [Theory]
    [InlineData("<256>", "<256>")]
    [InlineData("<>", "<>")]
    [InlineData("<a>", "<a>")]
    [InlineData("a < b", "a < b")]
    [InlineData("<3", "<3")]
    [InlineData("<003>", "\u0003")]
    [InlineData("<99999999999>", "<99999999999>")]
    public void Anything_but_a_code_from_0_to_255_is_literal(string text, string bytes) =>
        Assert.Equal(Tbl.Latin1(bytes), TblText.Compile(text, out _));

    [Fact]
    public void A_character_with_no_byte_is_refused()
    {
        Assert.Null(TblText.Compile("snow ☃", out var error));
        Assert.Contains("☃", error);
    }

    [Fact]
    public void The_edit_text_hides_only_the_final_nul()
    {
        Assert.Equal("Terran Marine<0>*<0>Ground Units", TblText.EditText(Tbl.Latin1("Terran Marine\0*\0Ground Units\0")));
        Assert.Equal("runs on", TblText.EditText(Tbl.Latin1("runs on")));
    }

    [Fact]
    public void A_hotkey_string_splits_into_hotkey_type_and_text()
    {
        var move = HotkeyString.Split(Tbl.Latin1("m\0\u0003M\u0001ove\0"))!;
        Assert.Equal((byte)'m', move.Hotkey);
        Assert.Equal(0, move.Type);
        Assert.Equal("<3>M<1>ove", TblText.Decompile(move.Text));
        Assert.Equal(Tbl.Latin1("m\0\u0003M\u0001ove\0"), move.Join());
        Assert.Equal(Tbl.Latin1("x\u0004\0"), HotkeyString.Split(Tbl.Latin1("x\u0004"))!.Join());
        Assert.Null(HotkeyString.Split(Tbl.Latin1("\0")));
        Assert.Null(HotkeyString.Split(Tbl.Latin1("x")));
        Assert.Equal("<3> Energy (spells)", HotkeyString.TypeLabel(3));
        Assert.Contains("not a PyTBL type", HotkeyString.TypeLabel(9));
    }
}
