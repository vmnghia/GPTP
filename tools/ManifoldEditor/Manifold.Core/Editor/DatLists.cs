namespace Manifold.Core.Editor;

/// <summary>
/// PyDAT's name lists (<c>Units.txt</c> and the rest, one name per line) and its field hints
/// (<c>Hints.txt</c>, <c>Key=Text</c>). A list that is not there is empty.
/// </summary>
public sealed class DatLists
{
    /// <summary>Every list file the editor reads, without the ".txt".</summary>
    public static readonly IReadOnlyList<string> Names =
    [
        "Units", "Weapons", "Flingy", "Sprites", "Images", "Upgrades", "Techdata", "Sfxdata", "Portdata", "Mapdata",
        "Orders", "IscriptIDList", "Icons", "Animations", "Behaviours", "DamTypes", "DrawList", "ElevationLevels",
        "Explosions", "FlingyControl", "Races", "Remapping", "Rightclick", "SelCircleSize", "ShieldSize", "UnitSize",
    ];

    readonly IReadOnlyDictionary<string, string[]> _lists;
    readonly IReadOnlyDictionary<string, string> _hints;

    DatLists(IReadOnlyDictionary<string, string[]> lists, IReadOnlyDictionary<string, string> hints)
    {
        _lists = lists;
        _hints = hints;
    }

    /// <summary>Reads the lists and <c>Hints.txt</c> from the folder; what is missing is empty.</summary>
    public static DatLists Load(string folder)
    {
        var lists = new Dictionary<string, string[]>();
        foreach (string name in Names)
        {
            string path = Path.Combine(folder, name + ".txt");
            if (File.Exists(path)) lists[name] = File.ReadAllLines(path).Select(l => l.TrimEnd()).ToArray();
        }
        var hints = new Dictionary<string, string>();
        string hintsPath = Path.Combine(folder, "Hints.txt");
        if (File.Exists(hintsPath))
            foreach (string line in File.ReadAllLines(hintsPath))
            {
                if (line.StartsWith('[') || line.StartsWith('#')) continue;
                int eq = line.IndexOf('=');
                if (eq > 0) hints[line[..eq]] = line[(eq + 1)..];
            }
        return new DatLists(lists, hints);
    }

    public static DatLists From(IReadOnlyDictionary<string, string[]> lists, IReadOnlyDictionary<string, string> hints) =>
        new(lists, hints);

    /// <summary>The named list's entries; empty when there is none.</summary>
    public IReadOnlyList<string> this[string name] => _lists.TryGetValue(name, out var list) ? list : [];

    /// <summary>The hint for a field key, or null.</summary>
    public string? Hint(string key) => _hints.TryGetValue(key, out var text) ? text : null;
}
