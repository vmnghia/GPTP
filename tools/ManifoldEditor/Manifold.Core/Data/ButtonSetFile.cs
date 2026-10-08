using System.Text;

namespace Manifold.Core.Data;

/// <summary>
/// <c>Manifold\buttonsets.bin</c> (spec §4): "MBTS", u16 version 1, u16 setCount 250,
/// then per set u16 buttonCount, u16 reserved 0, u32 connectedUnit and its buttons.
/// </summary>
public static class ButtonSetFile
{
    public const string ArchivePath = "Manifold\\buttonsets.bin";
    public const ushort Version = 1;
    static readonly byte[] Magic = Encoding.ASCII.GetBytes("MBTS");

    /// <summary>
    /// The order the game needs: by position, keeping the order of buttons that share one.
    /// The card's draw loop (updateButtonSet_Sub4591D0, 0x4591D0) walks slots 1-15 and the
    /// set's buttons together, placing a button when the slot reaches its position; a button
    /// listed after a higher position lands in a later slot. Vanilla's and FireGraft's sets
    /// are always sorted; the editor's moves only change positions (found 2026-10-09).
    /// </summary>
    public static IEnumerable<Button> InGameOrder(IEnumerable<Button> buttons) =>
        buttons.OrderBy(b => b.Position);   // OrderBy is stable

    public static byte[] Write(IReadOnlyList<ButtonSet> sets)
    {
        if (sets.Count != Card.SetCount)
            throw new ArgumentException($"expected {Card.SetCount} sets, got {sets.Count}");
        int size = 8 + sets.Sum(s => 8 + s.Buttons.Count * Button.Size);
        var bytes = new byte[size];
        var span = bytes.AsSpan();
        Magic.CopyTo(span);
        BitConverter.TryWriteBytes(span[4..], Version);
        BitConverter.TryWriteBytes(span[6..], (ushort)Card.SetCount);
        int at = 8;
        foreach (var set in sets)
        {
            BitConverter.TryWriteBytes(span[at..], checked((ushort)set.Buttons.Count));
            BitConverter.TryWriteBytes(span[(at + 4)..], set.ConnectedUnit);
            at += 8;
            foreach (var button in InGameOrder(set.Buttons))
            {
                button.Write(span[at..]);
                at += Button.Size;
            }
        }
        return bytes;
    }

    /// <summary>
    /// Reads and checks the file as the GPTP loader does. codeStart/codeEnd bound the
    /// condition and action addresses (StarCraft.exe's code section); null skips that check.
    /// Returns null and a reason when the file is refused.
    /// </summary>
    public static IReadOnlyList<ButtonSet>? Read(ReadOnlySpan<byte> bytes, out string? error,
        uint? codeStart = null, uint? codeEnd = null)
    {
        error = null;
        if (bytes.Length < 8 || !bytes[..4].SequenceEqual(Magic)) { error = "not a button set file"; return null; }
        if (BitConverter.ToUInt16(bytes[4..]) != Version) { error = "unknown version"; return null; }
        if (BitConverter.ToUInt16(bytes[6..]) != Card.SetCount) { error = "set count is not 250"; return null; }
        var sets = new ButtonSet[Card.SetCount];
        int at = 8;
        for (int s = 0; s < Card.SetCount; s++)
        {
            if (at + 8 > bytes.Length) { error = $"set {s} is cut short"; return null; }
            int count = BitConverter.ToUInt16(bytes[at..]);
            uint unit = BitConverter.ToUInt32(bytes[(at + 4)..]);
            at += 8;
            if (at + count * Button.Size > bytes.Length) { error = $"set {s} is cut short"; return null; }
            var buttons = new Button[count];
            for (int i = 0; i < count; i++, at += Button.Size)
            {
                var b = Button.Read(bytes[at..]);
                if (b.Position < 1 || b.Position > Card.MaxPosition) { error = $"set {s}: position {b.Position}"; return null; }
                if (codeStart is uint lo && codeEnd is uint hi &&
                    (b.Condition < lo || b.Condition >= hi || b.Action < lo || b.Action >= hi))
                { error = $"set {s}: address outside the code"; return null; }
                buttons[i] = b;
            }
            sets[s] = new ButtonSet(buttons, unit);
        }
        if (at != bytes.Length) { error = "data after the last set"; return null; }
        return sets;
    }
}
