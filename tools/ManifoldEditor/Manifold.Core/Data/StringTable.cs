namespace Manifold.Core.Data;

/// <summary>
/// A .tbl string table (rez\stat_txt.tbl) as the editor keeps and writes it: u16 count,
/// count u16 offsets, then the strings. Ids count from 1. Each id's bytes are its
/// <b>segment</b>: from its offset to the next distinct offset (the last one to the end of
/// the file), NULs included, as PyMS's PyTBL loads it. Unedited segments are written back
/// byte for byte, and ids that shared an offset still share one (spec §3). Immutable: an
/// edit returns a new table, so the undo history can keep the old one.
/// </summary>
public sealed class StringTable
{
    /// <summary>Offsets are 16-bit: every string must start below this.</summary>
    public const int Limit = 65536;

    readonly byte[][] segments;
    /// <summary>The id's offset in the file it was read from; -1 once edited or added.</summary>
    readonly int[] origins;
    /// <summary>The file read, for the full text of a string that runs on into the next.</summary>
    readonly byte[] source;

    StringTable(byte[][] segments, int[] origins, byte[] source)
    {
        this.segments = segments;
        this.origins = origins;
        this.source = source;
    }

    public int Count => segments.Length;

    public static StringTable Parse(byte[] bytes)
    {
        if (bytes.Length < 2) throw new InvalidDataException("shorter than its header");
        int count = BitConverter.ToUInt16(bytes, 0);
        int dataStart = 2 + 2 * count;
        if (dataStart > bytes.Length) throw new InvalidDataException($"{count} strings don't fit in {bytes.Length} bytes");
        var origins = new int[count];
        for (int i = 0; i < count; i++)
        {
            int offset = BitConverter.ToUInt16(bytes, 2 + 2 * i);
            if (offset < dataStart || offset > bytes.Length)
                throw new InvalidDataException($"string {i + 1} starts at {offset}, outside the strings ({dataStart}-{bytes.Length})");
            origins[i] = offset;
        }
        var starts = origins.Distinct().Order().ToArray();
        var segments = new byte[count][];
        for (int i = 0; i < count; i++)
        {
            int next = Array.BinarySearch(starts, origins[i]) + 1;
            int end = next < starts.Length ? starts[next] : bytes.Length;
            segments[i] = bytes[origins[i]..end];
        }
        return new StringTable(segments, origins, bytes);
    }

    void CheckId(int id)
    {
        if (id < 1 || id > Count) throw new ArgumentOutOfRangeException(nameof(id), $"no string {id} (the table has {Count})");
    }

    /// <summary>The id's bytes, NULs included.</summary>
    public byte[] Segment(int id)
    {
        CheckId(id);
        return segments[id - 1];
    }

    /// <summary>Edited or added since the table was read.</summary>
    public bool IsEdited(int id)
    {
        CheckId(id);
        return origins[id - 1] < 0;
    }

    static byte[] WithFinalNul(byte[] bytes) =>
        bytes.Length > 0 && bytes[^1] == 0 ? bytes : [.. bytes, 0];

    /// <summary>
    /// The id with new bytes (a final NUL is added when missing, as PyTBL does). A string
    /// whose text ran on into this one's old bytes first gets its full text as its own
    /// segment, so it never changes without being edited.
    /// </summary>
    public StringTable With(int id, byte[] bytes)
    {
        CheckId(id);
        var newSegments = (byte[][])segments.Clone();
        var newOrigins = (int[])origins.Clone();
        Detach(newSegments, newOrigins, origins[id - 1]);
        newSegments[id - 1] = WithFinalNul(bytes);
        newOrigins[id - 1] = -1;
        return new StringTable(newSegments, newOrigins, source);
    }

    /// <summary>A new string at the end; returns the table and its id.</summary>
    public (StringTable Table, int Id) Add(byte[] bytes)
    {
        if (Count >= ushort.MaxValue) throw new InvalidOperationException("the table holds 65,535 strings, its most");
        var newSegments = segments.Append(WithFinalNul(bytes)).ToArray();
        var newOrigins = origins.Append(-1).ToArray();
        return (new StringTable(newSegments, newOrigins, source), Count + 1);
    }

    /// <summary>The same bytes for every id (edited or not).</summary>
    public bool SameAs(StringTable other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other.Count != Count) return false;
        for (int i = 0; i < Count; i++)
            if (!ReferenceEquals(segments[i], other.segments[i]) && !segments[i].AsSpan().SequenceEqual(other.segments[i]))
                return false;
        return true;
    }

    /// <summary>
    /// The id as it is in <paramref name="opened"/>, the table this one was edited from: its
    /// own bytes at their old place again. A string that ran on into the next gets its full
    /// text instead, as when edited, since what followed it may have moved.
    /// </summary>
    public StringTable Revert(int id, StringTable opened)
    {
        CheckId(id);
        if (!ReferenceEquals(opened.source, source)) throw new ArgumentException("not the table this one was edited from");
        int origin = opened.origins[id - 1];
        var segment = opened.segments[id - 1];
        if (origin < 0) return With(id, segment);
        if (segment.Length == 0 || segment[^1] != 0)
        {
            int nul = Array.IndexOf(source, (byte)0, origin);
            return With(id, nul < 0 ? source[origin..] : source[origin..(nul + 1)]);
        }
        if (origins[id - 1] == origin) return this;
        var newSegments = (byte[][])segments.Clone();
        var newOrigins = (int[])origins.Clone();
        newSegments[id - 1] = segment;
        newOrigins[id - 1] = origin;
        return new StringTable(newSegments, newOrigins, source);
    }

    /// <summary>
    /// Before the bytes at <paramref name="origin"/> move, every unedited string that runs
    /// on into them (its segment ends where they start, without a NUL) takes its full text:
    /// its bytes through the first NUL after its segment. Repeats for strings running into
    /// those.
    /// </summary>
    void Detach(byte[][] newSegments, int[] newOrigins, int origin)
    {
        var pending = new Stack<int>();
        if (origin >= 0) pending.Push(origin);
        while (pending.Count > 0)
        {
            int target = pending.Pop();
            for (int i = 0; i < Count; i++)
            {
                int start = newOrigins[i];
                if (start < 0 || start + newSegments[i].Length != target) continue;
                var segment = newSegments[i];
                if (segment.Length > 0 && segment[^1] == 0) continue;
                int nul = Array.IndexOf(source, (byte)0, target);
                newSegments[i] = nul < 0 ? [.. source[start..], 0] : source[start..(nul + 1)];
                newOrigins[i] = -1;
                pending.Push(start);
            }
        }
    }

    /// <summary>
    /// The file: unedited segments in their original order (shared ones once), then the
    /// edited and added ones, shortest first, so the longest is the one that may run past
    /// the 16-bit limit.
    /// </summary>
    (List<byte[]> Order, int[] Offsets, int Size) Layout()
    {
        var order = new List<byte[]>();
        var offsets = new int[Count];
        int at = 2 + 2 * Count;
        var placed = new Dictionary<int, int>();
        foreach (int i in Enumerable.Range(0, Count).Where(i => origins[i] >= 0).OrderBy(i => origins[i]))
        {
            if (!placed.TryGetValue(origins[i], out int offset))
            {
                offset = at;
                placed[origins[i]] = offset;
                order.Add(segments[i]);
                at += segments[i].Length;
            }
            offsets[i] = offset;
        }
        foreach (int i in Enumerable.Range(0, Count).Where(i => origins[i] < 0).OrderBy(i => segments[i].Length))
        {
            offsets[i] = at;
            order.Add(segments[i]);
            at += segments[i].Length;
        }
        return (order, offsets, at);
    }

    /// <summary>
    /// The bytes that can still go in before the last string would start past the 16-bit
    /// limit (negative when it already does).
    /// </summary>
    public int BytesFree
    {
        get
        {
            var (order, _, size) = Layout();
            int lastStart = order.Count > 0 ? size - order[^1].Length : size;
            return Limit - 1 - lastStart;
        }
    }

    /// <summary>The first id that would start past the 16-bit limit, or null when the table fits.</summary>
    public int? FirstOverLimit()
    {
        var offsets = Layout().Offsets;
        for (int i = 0; i < Count; i++)
            if (offsets[i] >= Limit) return i + 1;
        return null;
    }

    public byte[] Write()
    {
        if (FirstOverLimit() is int id)
            throw new InvalidOperationException($"string {id} would start past byte 65,535, the most a .tbl can address");
        var (order, offsets, size) = Layout();
        var bytes = new byte[size];
        BitConverter.TryWriteBytes(bytes.AsSpan(0, 2), (ushort)Count);
        for (int i = 0; i < Count; i++)
            BitConverter.TryWriteBytes(bytes.AsSpan(2 + 2 * i, 2), (ushort)offsets[i]);
        int at = 2 + 2 * Count;
        foreach (var segment in order)
        {
            segment.CopyTo(bytes, at);
            at += segment.Length;
        }
        return bytes;
    }

    /// <summary>
    /// The save's self-check: the written file reads back every id's segment. Returns null,
    /// or what differs.
    /// </summary>
    public string? CheckWritten(byte[] written)
    {
        StringTable read;
        try { read = Parse(written); }
        catch (InvalidDataException e) { return $"the written table doesn't read back: {e.Message}"; }
        if (read.Count != Count) return $"the written table reads back {read.Count} strings, not {Count}";
        for (int id = 1; id <= Count; id++)
            if (!read.Segment(id).AsSpan().SequenceEqual(Segment(id)))
                return $"string {id} reads back different";
        return null;
    }
}
