using Manifold.Core.Data;

namespace Manifold.Core.Tests;

public class ButtonSetFileTests
{
    static ButtonSet[] Sample()
    {
        var sets = Make.EmptySets();
        sets[5] = new ButtonSet(new[] { Make.Button(11, icon: 236), Make.Button(12, icon: 237), Make.Button(11, icon: 238) }, 5);
        sets[249] = new ButtonSet(new[] { Make.Button(1) }, 0xE4);
        return sets;
    }

    [Fact]
    public void Button_is_20_bytes_in_GPTP_order()
    {
        var b = new Button(1, 228, 0x428DA0, 0x424440, 2, 3, 664, 0x1234);
        var bytes = new byte[20];
        b.Write(bytes);
        Assert.Equal("0100E400A08D420040444200020003009802" + "3412", Convert.ToHexString(bytes));
        Assert.Equal(b, Button.Read(bytes));
    }

    [Fact]
    public void Round_trips_byte_for_byte()
    {
        var bytes = ButtonSetFile.Write(Sample());
        var sets = ButtonSetFile.Read(bytes, out var error);
        Assert.Null(error);
        Assert.Equal(bytes, ButtonSetFile.Write(sets!));
        // Read back in the game's order: by position, shared slots in their order.
        Assert.Equal(ButtonSetFile.InGameOrder(Sample()[5].Buttons), sets![5].Buttons);
        Assert.Equal(8 + 250 * 8 + 4 * 20, bytes.Length);
    }

    [Fact]
    public void Writes_all_250_sets_with_the_header()
    {
        var bytes = ButtonSetFile.Write(Make.EmptySets());
        Assert.Equal("4D425453" + "0100" + "FA00", Convert.ToHexString(bytes[..8]));
        Assert.Equal(8 + 250 * 8, bytes.Length);
    }

    [Theory]
    [InlineData("magic", "not a button set file")]
    [InlineData("version", "unknown version")]
    [InlineData("count", "set count is not 250")]
    [InlineData("truncated", "set 249 is cut short")]
    [InlineData("position0", "set 5: position 0")]
    [InlineData("position16", "set 5: position 16")]
    [InlineData("trailing", "data after the last set")]
    public void Refuses_a_bad_file_with_its_reason(string fault, string reason)
    {
        var bytes = ButtonSetFile.Write(Sample());
        int firstButtonOfSet5 = 8 + 5 * 8 + 8;
        switch (fault)
        {
            case "magic": bytes[0] = (byte)'X'; break;
            case "version": bytes[4] = 2; break;
            case "count": bytes[6] = 249; break;
            case "truncated": bytes = bytes[..^1]; break;
            case "position0": bytes[firstButtonOfSet5] = 0; break;
            case "position16": bytes[firstButtonOfSet5] = 16; break;
            case "trailing": bytes = bytes.Append((byte)0).ToArray(); break;
        }
        Assert.Null(ButtonSetFile.Read(bytes, out var error));
        Assert.Equal(reason, error);
    }

    [Fact]
    public void Refuses_an_address_outside_the_code_section()
    {
        var sets = Sample();
        sets[5] = Make.Set(Make.Button(1, condition: 0x00600000));
        var bytes = ButtonSetFile.Write(sets);
        Assert.Null(ButtonSetFile.Read(bytes, out var error, codeStart: 0x401000, codeEnd: 0x500000));
        Assert.Equal("set 5: address outside the code", error);
        Assert.NotNull(ButtonSetFile.Read(bytes, out _));
    }
}

public class InGameOrderTests
{
    [Fact]
    public void Sets_are_written_sorted_by_position_keeping_shared_slots_in_order()
    {
        // As the Zergling was after moves: Move 1, Stop 2, Hold 5, Patrol 4, Attack 3, then
        // Unburrow and Burrow sharing 14 (Unburrow listed first).
        var zergling = Make.Set(Make.Button(1, icon: 228), Make.Button(2, icon: 229), Make.Button(5, icon: 230),
            Make.Button(4, icon: 254), Make.Button(3, icon: 255), Make.Button(14, icon: 260), Make.Button(14, icon: 259));
        var sets = Make.EmptySets();
        sets[37] = zergling;
        var read = ButtonSetFile.Read(ButtonSetFile.Write(sets), out var error)!;
        Assert.Null(error);
        Assert.Equal([1, 2, 3, 4, 5, 14, 14], read[37].Buttons.Select(b => (int)b.Position));
        Assert.Equal([228, 229, 255, 254, 230, 260, 259], read[37].Buttons.Select(b => (int)b.Icon));
    }
}
