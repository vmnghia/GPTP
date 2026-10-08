using System.Globalization;
using Manifold.Core.Data;

namespace Manifold.Core.Editor;

/// <summary>The set list's groups, in the order shown (spec §5).</summary>
public enum SetGroup { Terran, Zerg, Protoss, NeutralAndHeroes, Units, Menus }

/// <summary>A set in the list: its name, group, and what the filters look at.</summary>
public sealed record SetEntry(int Id, string Name, SetGroup Group, Race Race = Race.None, UnitKind Kind = UnitKind.Card);

/// <summary>The set list's filters: race, type, and only sets that have buttons. Null: any.</summary>
public sealed record SetFilter(Race? Race = null, UnitKind? Kind = null, bool WithButtons = false);

public sealed record SetGroupView(SetGroup Group, IReadOnlyList<SetEntry> Entries);

/// <summary>All 250 sets with a name and a group, and the search box's filter.</summary>
public sealed class SetCatalog
{
    public IReadOnlyList<SetEntry> Entries { get; }

    SetCatalog(IReadOnlyList<SetEntry> entries) => Entries = entries;

    /// <summary>
    /// Unit sets (0-227) are named by their unit (<see cref="StatTxt.UnitName"/>: name and
    /// subname) and grouped by race, heroes apart; without units.dat they are one "Units"
    /// group, and race and type are unknown (None, Unit). 228-249 are the menus and other cards.
    /// </summary>
    public static SetCatalog Build(Func<int, string?> unitName, UnitsDat? units)
    {
        var entries = new SetEntry[Card.SetCount];
        for (int id = 0; id < Card.SetCount; id++)
        {
            if (id >= UnitsDat.UnitCount)
            {
                entries[id] = new SetEntry(id, CardNames[id - UnitsDat.UnitCount], SetGroup.Menus);
                continue;
            }
            string name = unitName(id) ?? "";
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
            entries[id] = new SetEntry(id, name, group, units?.RaceOf(id) ?? Race.None, units?.KindOf(id) ?? UnitKind.Unit);
        }
        return new SetCatalog(entries);
    }

    /// <summary>The cards 228-249, as GPTP's UnitId names them (Buttons_*).</summary>
    public static readonly IReadOnlyList<string> CardNames =
    [
        "Blank", "Cancel", "Cancel place building / add-on / land", "Cancel construction",
        "Cancel construction + rally", "Cancel mutation", "Cancel mutation + rally", "Cancel infestation",
        "Hatchery (morphing)", "Cancel nuke strike", "Basic Zerg buildings", "Basic Terran buildings",
        "Basic Protoss buildings", "Advanced Zerg buildings", "Advanced Terran buildings",
        "Advanced Protoss buildings", "Mixed group", "Peon group", "Cloaker group", "Burrower group",
        "Replay: paused", "Replay: playing",
    ];

    /// <summary>
    /// The groups that have matches, in order. A number matches that set id; anything else
    /// matches names, ignoring case. An empty query matches everything.
    /// </summary>
    public IReadOnlyList<SetGroupView> Filter(string query, SetFilter? filter = null, Func<int, bool>? hasButtons = null)
    {
        filter ??= new SetFilter();
        query = query.Trim();
        Func<SetEntry, bool> matches =
            query.Length == 0 ? _ => true
            : int.TryParse(query, NumberStyles.None, CultureInfo.InvariantCulture, out int id) ? e => e.Id == id
            : e => e.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
        return Entries.Where(matches)
            .Where(e => filter.Race is not Race race || e.Kind != UnitKind.Card && e.Race == race)   // cards have no race
            .Where(e => filter.Kind is not UnitKind kind || e.Kind == kind)
            .Where(e => !filter.WithButtons || hasButtons is null || hasButtons(e.Id))
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
