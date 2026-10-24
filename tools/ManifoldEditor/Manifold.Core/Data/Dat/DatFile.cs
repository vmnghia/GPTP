namespace Manifold.Core.Data;

/// <summary>
/// One .dat file, parsed: a value per property per entry, each property one array. Immutable:
/// <see cref="With"/> returns a new file that shares the arrays it did not touch. A file of
/// the vanilla size is read in the vanilla layout (a property may cover only some entries);
/// otherwise a whole number of expanded entries, within the format's limits, is read in the
/// expanded layout, where every property covers every entry.
/// </summary>
public sealed class DatFile
{
    readonly ulong[][] values;
    readonly int[] offsets;

    DatFile(DatFormat format, int count, bool isExpanded, ulong[][] values, int[] offsets)
    {
        Format = format;
        Count = count;
        IsExpanded = isExpanded;
        this.values = values;
        this.offsets = offsets;
    }

    public DatFormat Format { get; }
    public int Count { get; }
    public bool IsExpanded { get; }

    public static DatFile Parse(DatFormat format, byte[] bytes)
    {
        bool expanded = false;
        int count = format.Entries;
        if (bytes.Length != format.VanillaSize)
        {
            if (format.ExpandedEntrySize is not { } entrySize)
                throw new InvalidDataException($"{format.FileName} is {bytes.Length} bytes: not {format.VanillaSize}");
            if (bytes.Length % entrySize != 0)
                throw new InvalidDataException(
                    $"{format.FileName} is {bytes.Length} bytes: not {format.VanillaSize}, nor a whole number of {entrySize}-byte entries");
            expanded = true;
            count = bytes.Length / entrySize;
            int min = format.ExpandedMin ?? 1, max = format.ExpandedMax!.Value;
            if (count < min || count > max)
                throw new InvalidDataException(
                    $"{format.FileName} has {count} entries: an expanded file has {min} to {max}");
            if (format.ExpandedMultiple is { } multiple && count % multiple != 0)
                throw new InvalidDataException(
                    $"{format.FileName} has {count} entries: an expanded file has a multiple of {multiple}");
        }

        var props = format.Properties;
        var values = new ulong[props.Count][];
        var offsets = new int[props.Count];
        int at = 0;
        for (int i = 0; i < props.Count; i++)
        {
            var p = props[i];
            int bytesEach = DatTypes.Bytes(p.TypeIn(expanded));
            int length = expanded ? count : p.VanillaCount ?? format.Entries;
            offsets[i] = expanded ? 0 : p.VanillaFirst ?? 0;
            var array = new ulong[length];
            for (int e = 0; e < length; e++)
            {
                ulong v = 0;
                for (int b = bytesEach - 1; b >= 0; b--) v = (v << 8) | bytes[at + b];
                array[e] = v;
                at += bytesEach;
            }
            values[i] = array;
        }
        return new DatFile(format, count, expanded, values, offsets);
    }

    /// <summary>The raw value, or null when the property has no value on the entry (vanilla layout only).</summary>
    public ulong? Get(int entry, string property)
    {
        CheckEntry(entry);
        int i = Format.IndexOf(property);
        int at = entry - offsets[i];
        return at >= 0 && at < values[i].Length ? values[i][at] : null;
    }

    public bool Covers(int entry, string property) =>
        entry >= 0 && entry < Count && Format[property].Covers(entry, IsExpanded);

    /// <summary>A copy with the property's value on the entry replaced.</summary>
    public DatFile With(int entry, string property, ulong value)
    {
        CheckEntry(entry);
        int i = Format.IndexOf(property);
        var p = Format.Properties[i];
        if (!p.Covers(entry, IsExpanded))
            throw new ArgumentOutOfRangeException(nameof(entry), entry, $"{property} has no value on entry {entry}");
        var type = p.TypeIn(IsExpanded);
        if (value > DatTypes.Max(type))
            throw new ArgumentOutOfRangeException(nameof(value), value, $"{property} is a {type.ToString().ToLowerInvariant()}: at most {DatTypes.Max(type)}");
        var copy = (ulong[][])values.Clone();
        copy[i] = (ulong[])values[i].Clone();
        copy[i][entry - offsets[i]] = value;
        return new DatFile(Format, Count, IsExpanded, copy, offsets);
    }

    /// <summary>
    /// A copy with every property of the entry taken from the same entry of another file of
    /// the same format; a property one of the two does not cover on it is left alone.
    /// </summary>
    public DatFile WithEntryFrom(int entry, DatFile source)
    {
        if (source.Format.Id != Format.Id)
            throw new ArgumentException($"{source.Format.FileName} is not {Format.FileName}", nameof(source));
        CheckEntry(entry);
        source.CheckEntry(entry);
        var result = this;
        foreach (var p in Format.Properties)
        {
            if (!Covers(entry, p.Name) || !source.Covers(entry, p.Name)) continue;
            ulong value = source.Get(entry, p.Name)!.Value;
            if (value != result.Get(entry, p.Name)) result = result.With(entry, p.Name, value);
        }
        return result;
    }

    /// <summary>Whether the two files have the same layout and every value equal.</summary>
    public bool SameAs(DatFile other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other.Format.Id != Format.Id || other.IsExpanded != IsExpanded || other.Count != Count) return false;
        for (int i = 0; i < values.Length; i++)
            if (!ReferenceEquals(values[i], other.values[i]) && !values[i].AsSpan().SequenceEqual(other.values[i]))
                return false;
        return true;
    }

    /// <summary>Whether the entry has the same values in both files (an entry the other file lacks differs).</summary>
    public bool EntrySameAs(int entry, DatFile other)
    {
        if (other.Format.Id != Format.Id) return false;
        CheckEntry(entry);
        if (entry >= other.Count) return false;
        for (int i = 0; i < values.Length; i++)
        {
            int a = entry - offsets[i], b = entry - other.offsets[i];
            bool hasA = a >= 0 && a < values[i].Length, hasB = b >= 0 && b < other.values[i].Length;
            if (hasA != hasB) return false;
            if (hasA && values[i][a] != other.values[i][b]) return false;
        }
        return true;
    }

    /// <summary>The file's bytes in its own layout: a file read and not edited saves identical to its input.</summary>
    public byte[] Save()
    {
        int size = 0;
        for (int i = 0; i < values.Length; i++)
            size += values[i].Length * DatTypes.Bytes(Format.Properties[i].TypeIn(IsExpanded));
        var bytes = new byte[size];
        int at = 0;
        for (int i = 0; i < values.Length; i++)
        {
            int bytesEach = DatTypes.Bytes(Format.Properties[i].TypeIn(IsExpanded));
            foreach (var v in values[i])
                for (int b = 0; b < bytesEach; b++) bytes[at++] = (byte)(v >> (8 * b));
        }
        return bytes;
    }

    void CheckEntry(int entry)
    {
        if (entry < 0 || entry >= Count)
            throw new ArgumentOutOfRangeException(nameof(entry), entry, $"{Format.FileName} has {Count} entries");
    }
}
