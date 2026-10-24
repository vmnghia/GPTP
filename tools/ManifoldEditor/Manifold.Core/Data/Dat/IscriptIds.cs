namespace Manifold.Core.Data;

/// <summary>The ids an iscript.bin defines, and the names PyDAT's Images tab gives them.</summary>
public static class IscriptIds
{
    /// <summary>
    /// The ids in the bin's table, sorted. The word at 0 is the table's offset; the table is
    /// (u16 id, u16 offset) pairs up to the pair (0xFFFF, 0) or the end of the file.
    /// </summary>
    public static IReadOnlyList<int> Read(byte[] bin)
    {
        var ids = new SortedSet<int>();
        if (bin.Length < 2) return [];
        for (int at = BitConverter.ToUInt16(bin, 0); at + 4 <= bin.Length; at += 4)
        {
            int id = BitConverter.ToUInt16(bin, at), offset = BitConverter.ToUInt16(bin, at + 2);
            if (id == 0xFFFF && offset == 0) break;
            ids.Add(id);
        }
        return ids.ToArray();
    }

    /// <summary>
    /// One name per id from 0 to the last (the entries of the Images tab's script box): a gap
    /// is "*Unused*", an id within the list its name there, any other "Unnamed Custom Entry".
    /// </summary>
    public static IReadOnlyList<string> Names(IReadOnlyList<int> ids, IReadOnlyList<string> idList)
    {
        var names = new List<string>();
        foreach (int id in ids.Order())
        {
            while (names.Count < id) names.Add("*Unused*");
            if (id < names.Count) continue;
            names.Add(id < idList.Count ? idList[id] : "Unnamed Custom Entry");
        }
        return names;
    }
}
