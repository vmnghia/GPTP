namespace Manifold.Core.Data;

/// <summary>
/// One command card button: GPTP's <c>BUTTON</c> (SCBW/structures.h) field for field,
/// 20 bytes little-endian. Condition and Action are StarCraft.exe 1.16.1 addresses.
/// The string at 0x10 is the tooltip shown when the button is enabled (its first
/// character is the hotkey); the one at 0x12 is shown when it is disabled. GPTP names
/// them reqStringID and actStringID.
/// </summary>
public readonly record struct Button(
    ushort Position,
    ushort Icon,
    uint Condition,
    uint Action,
    ushort ConditionVar,
    ushort ActionVar,
    ushort EnabledString,
    ushort DisabledString)
{
    public const int Size = 20;

    public static Button Read(ReadOnlySpan<byte> b) => new(
        BitConverter.ToUInt16(b[0..]),
        BitConverter.ToUInt16(b[2..]),
        BitConverter.ToUInt32(b[4..]),
        BitConverter.ToUInt32(b[8..]),
        BitConverter.ToUInt16(b[12..]),
        BitConverter.ToUInt16(b[14..]),
        BitConverter.ToUInt16(b[16..]),
        BitConverter.ToUInt16(b[18..]));

    public void Write(Span<byte> b)
    {
        BitConverter.TryWriteBytes(b[0..], Position);
        BitConverter.TryWriteBytes(b[2..], Icon);
        BitConverter.TryWriteBytes(b[4..], Condition);
        BitConverter.TryWriteBytes(b[8..], Action);
        BitConverter.TryWriteBytes(b[12..], ConditionVar);
        BitConverter.TryWriteBytes(b[14..], ActionVar);
        BitConverter.TryWriteBytes(b[16..], EnabledString);
        BitConverter.TryWriteBytes(b[18..], DisabledString);
    }
}

/// <summary>A button set: its buttons in order, and the unit it belongs to.</summary>
public sealed record ButtonSet(IReadOnlyList<Button> Buttons, uint ConnectedUnit)
{
    public static readonly ButtonSet Empty = new(Array.Empty<Button>(), 0);

    public bool SameAs(ButtonSet other) =>
        ConnectedUnit == other.ConnectedUnit && Buttons.SequenceEqual(other.Buttons);
}

public static class Card
{
    /// <summary>The game's button set table holds 250 entries (0x5187E8).</summary>
    public const int SetCount = 250;
    /// <summary>The 5x3 card: positions 1-15. Replays show the 3x3 card, 1-9.</summary>
    public const int MaxPosition = 15;
    public const int ReplayMaxPosition = 9;
}
