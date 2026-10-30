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

    /// <summary>
    /// The entry count growing to <paramref name="wanted"/> gives, by PyMS's <c>expanded_count</c>: a file not
    /// yet expanded goes up to the format's minimum, then the count rounds up to its step. Null when
    /// that is past the format's maximum, the format has no expanded layout, or <paramref name="wanted"/> is below 1.
    /// </summary>
    public int? ExpandedCount(long wanted)
    {
        if (Format.ExpandedMax is not { } max || wanted < 1 || wanted > max) return null;
        long count = wanted;
        if (!IsExpanded && Format.ExpandedMin is { } min && count < min) count = min;
        if (Format.ExpandedMultiple is { } step) count = (count + step - 1) / step * step;
        return count <= max ? (int)count : null;
    }

    /// <summary>Why <see cref="WithCount"/> refuses the count, or null when it doesn't: fewer entries than now, or past the maximum.</summary>
    public string? CountRefusal(long count)
    {
        if (count < Count) return $"{Format.FileName} has {Count} entries: it can't have fewer";
        if (count == Count || ExpandedCount(count) is not null) return null;
        return Format.ExpandedMax is { } max ? $"{Format.FileName} has at most {max} entries" : $"{Format.FileName} can't have more entries";
    }

    /// <summary>Whether one more entry fits (PyMS's <c>can_expand</c>).</summary>
    public bool CanGrow => ExpandedCount(Count + 1L) is not null;

    /// <summary>
    /// The file grown to <see cref="ExpandedCount"/>(<paramref name="count"/>) entries, in the expanded layout
    /// (PyMS's <c>expand_entries</c>): every property covers every entry, a value it didn't cover is 0,
    /// and the new entries are all 0. The same count is this file. Throws for fewer entries, or past the maximum.
    /// </summary>
    public DatFile WithCount(long count)
    {
        if (count == Count) return this;
        if (CountRefusal(count) is { } refusal) throw new ArgumentOutOfRangeException(nameof(count), count, refusal);
        int result = ExpandedCount(count)!.Value;
        var grown = new ulong[values.Length][];
        for (int i = 0; i < values.Length; i++)
        {
            grown[i] = new ulong[result];
            values[i].CopyTo(grown[i], offsets[i]);
        }
        return new DatFile(Format, result, true, grown, new int[values.Length]);
    }

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
    /// <param name="uncoveredIsZero">
    /// A property this file covers on the entry and <paramref name="source"/> doesn't is set to 0, what
    /// a growth gives it (counts spec, final review): Revert entry to a file opened in the vanilla layout.
    /// </param>
    public DatFile WithEntryFrom(int entry, DatFile source, bool uncoveredIsZero = false)
    {
        if (source.Format.Id != Format.Id)
            throw new ArgumentException($"{source.Format.FileName} is not {Format.FileName}", nameof(source));
        CheckEntry(entry);
        source.CheckEntry(entry);
        var result = this;
        foreach (var p in Format.Properties)
        {
            if (!Covers(entry, p.Name) || (!source.Covers(entry, p.Name) && !uncoveredIsZero)) continue;
            ulong value = source.Covers(entry, p.Name) ? source.Get(entry, p.Name)!.Value : 0;
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

    /// <summary>
    /// Whether the entry has the same values in both files, over the properties both cover on
    /// it, as <see cref="WithEntryFrom"/> copies them: a value only one file has is no difference
    /// (an expanded file's sprites 0-129 have a health bar, vanilla's do not). An entry the
    /// other file lacks differs.
    /// </summary>
    /// <param name="uncoveredIsZero">
    /// A value only this file covers counts as different unless it is 0, what a growth puts there
    /// (the unsaved mark against a file saved in the vanilla layout; counts spec, final review).
    /// </param>
    public bool EntrySameAs(int entry, DatFile other, bool uncoveredIsZero = false)
    {
        if (other.Format.Id != Format.Id) return false;
        CheckEntry(entry);
        if (entry >= other.Count) return false;
        for (int i = 0; i < values.Length; i++)
        {
            int a = entry - offsets[i], b = entry - other.offsets[i];
            bool hasA = a >= 0 && a < values[i].Length, hasB = b >= 0 && b < other.values[i].Length;
            if (hasA && hasB && values[i][a] != other.values[i][b]) return false;
            if (uncoveredIsZero && hasA && !hasB && values[i][a] != 0) return false;
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
