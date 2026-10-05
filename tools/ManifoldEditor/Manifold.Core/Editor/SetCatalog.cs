using System.Globalization;
using Manifold.Core.Data;

namespace Manifold.Core.Editor;

/// <summary>The set list's groups, in the order shown (spec §5).</summary>
public enum SetGroup { Terran, Zerg, Protoss, NeutralAndHeroes, Units, Menus }

public sealed record SetEntry(int Id, string Name, SetGroup Group);

public sealed record SetGroupView(SetGroup Group, IReadOnlyList<SetEntry> Entries);

/// <summary>All 250 sets with a name and a group, and the search box's filter.</summary>
public sealed class SetCatalog
{
    public IReadOnlyList<SetEntry> Entries { get; }

    SetCatalog(IReadOnlyList<SetEntry> entries) => Entries = entries;

    /// <summary>
    /// Unit sets (0-227) are named by their unit's stat_txt string (id + 1) and grouped by
    /// race, heroes apart; without units.dat they are one "Units" group. 228-249 are the
    /// menus and other cards.
    /// </summary>
    public static SetCatalog Build(Func<ushort, string?> strings, UnitsDat? units)
    {
        var entries = new SetEntry[Card.SetCount];
        for (int id = 0; id < Card.SetCount; id++)
        {
            if (id >= UnitsDat.UnitCount)
            {
                entries[id] = new SetEntry(id, id == UnitsDat.UnitCount ? "(empty)" : $"Card {id}", SetGroup.Menus);
                continue;
            }
            string name = ButtonText.Clean(strings((ushort)(id + 1)));
            if (name.Length == 0) name = $"Unit {id}";
            var group = units is null ? SetGroup.Units
                : units.IsHero(id) ? SetGroup.NeutralAndHeroes
                : units.RaceOf(id) switch
                {
                    Race.Terran => SetGroup.Terran,
                    Race.Zerg => SetGroup.Zerg,
                    Race.Protoss => SetGroup.Protoss,
                    _ => SetGroup.NeutralAndHeroes,
                };
            entries[id] = new SetEntry(id, name, group);
        }
        return new SetCatalog(entries);
    }

    /// <summary>
    /// The groups that have matches, in order. A number matches that set id; anything else
    /// matches names, ignoring case. An empty query matches everything.
    /// </summary>
    public IReadOnlyList<SetGroupView> Filter(string query)
    {
        query = query.Trim();
        Func<SetEntry, bool> matches =
            query.Length == 0 ? _ => true
            : int.TryParse(query, NumberStyles.None, CultureInfo.InvariantCulture, out int id) ? e => e.Id == id
            : e => e.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
        return Entries.Where(matches)
            .GroupBy(e => e.Group)
            .OrderBy(g => g.Key)
            .Select(g => new SetGroupView(g.Key, g.ToArray()))
            .ToArray();
    }

    public static string GroupTitle(SetGroup group) => group switch
    {
        SetGroup.NeutralAndHeroes => "Neutral and heroes",
        _ => group.ToString(),
    };
}
