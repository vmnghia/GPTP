# Smart rally (gather and enter on rally, SC2's worker rally)

Status: `[BUILT 2026-10-14]`, not yet run in game. Plan:
`docs/superpowers/plans/2026-10-14-smart-rally.md`. The worker rally line is yellow
(the user's pick from a preview, §2.5).
Asked for by the user on 2026-10-14. Idea from KYSXD's tutorial
(https://github.com/KYSXD/GPTP-For-VS2008/wiki/%5BPlugin%5D-Smart-rally), reviewed in §9
and not followed as is. SC2 is the reference; the user checks SC2's behaviour.

## 1. Goal

- A worker rallied onto a resource gathers it; a unit rallied onto its own Bunker or
  transport goes in.
- Hatchery, Lair and Hive get a second rally, for Drones only, as in SC2: set by
  right-clicking a resource or by a new button.
- At the start of a melee game every starting town hall is rallied (its workers' rally) to
  its nearest mineral field.

Synced game state: all of it changes orders, so it runs on every client from synced data
(§7).

## 2. What the player sees

### 2.1 Which rally a new unit uses

- **Hatchery, Lair, Hive** (the "two-rally" buildings): a unit hatched from their larvae
  uses the **worker rally** if it is a worker (a Drone), else the **normal rally** (the
  game's own). A Drone of a Hatchery with no worker rally **stays at the Hatchery**, even
  if the normal rally is set (the user, 2026-10-14). This changes vanilla, where Drones
  follow the Hatchery's one rally.
- **Every other building** (CC, Nexus, Barracks, Gateway…): its one rally, for every unit,
  as in vanilla.

### 2.2 What a rallied unit does

Decided when the unit is made (0x466F50), from the rally it uses (§2.1), in this order:

1. **Gather.** The unit gathers when its right-click gathers (units.dat right-click action
   Harvest or Harvest and Repair) and the rally target is a mineral field, or a finished
   Refinery, Assimilator or Extractor of the unit's owner (the game's own test, 0x468930).
   It gets the order a right-click gives: MoveToMinerals (0x55) or HarvestGas1 (0x51), so
   busy patches and the queue at a gas building behave as after a right-click.
2. **Enter.** The unit goes in when the rally target is the owner's own Bunker or
   transport and a right-click from the unit would load it (EnterTransport, 0x5C): the
   game's own checks, so a hallucination, an Overlord without Ventral Sacs, a full or a
   disabled transport refuse it. `[VERIFY]` the functions: 0x4E6BA0 and 0x4E6E00, as
   called by the right-click rule at 0x45596F–0x45598B.
3. **Otherwise vanilla** (0x466F50): Follow the rally unit if it is the owner's, else
   Move to the point. Vanilla's Follow already enters the owner's Nydus Canal (0x4E8C20,
   order 0x2F) and recharges shields at a Shield Battery (0x493520, order 0x42) when it
   arrives, so those need nothing new.

So a Drone rallied onto a bare Vespene Geyser walks there; onto the owner's unfinished
Extractor it follows it; onto an enemy's gas building it walks to the point.

### 2.3 Setting the rallies of a Hatchery, Lair or Hive

**Right-click** with the building selected:

| Clicked                                                          | Sets                          |
|------------------------------------------------------------------|-------------------------------|
| The ground                                                       | normal rally (vanilla)        |
| A unit that is not a resource                                    | normal rally (vanilla)        |
| A resource the worker rally is not on                            | worker rally                  |
| The resource the worker rally is already on                      | normal rally too (both on it) |

A **resource** here (the user, 2026-10-14): a mineral field; a Vespene Geyser (bare);
a Refinery, Assimilator or Extractor of the building's owner, finished or not. An enemy's
or an ally's gas building is "a unit that is not a resource".

**Set Rally Point** (the game's button) sets the normal rally, on anything, as in vanilla.

**Set Worker Rally Point**, a new button (hotkey G), sets the worker rally on the ground
or on any unit. On the building itself it clears it (Drones stay), as the game's rally
on the building itself is none (found in the final review, 2026-10-14). Same icon as Set Rally Point for now (§10). It is a button-set edit the
user makes (§8).

Every other building: right-click and its button as in vanilla.

### 2.4 Game start

Melee games only, where `initializeGame()` already sends the starting workers to mine
(`hooks/main/game_hooks.cpp`): each starting resource depot's nearest mineral field (the
one it already finds, within 512 px) becomes
- the CC's or Nexus's rally (unit target, at the mineral's position), so new SCVs and
  Probes mine;
- the Hatchery's worker rally, so new Drones mine. Its normal rally stays unset.

For every player, computer players included. Game start only: a town hall finished later
starts with no rally (the user, 2026-10-14).

### 2.5 Lines

With a Hatchery, Lair or Hive selected, both rally lines show (`drawRallyPoint`). The
worker rally's colour is picked from a rendered preview with the user in the first round
`[PROPOSED]`. The rules for when a line shows are the normal rally's
(`orderlines::rallyIsSet`), plus "worker rally set".

## 3. Pure rules

New module `SCBW/smart_rally.{h,cpp}`, host-tested by `tests/smart_rally_test.{cpp,bat}`
(added to `tests/verify.ps1`). No game state: the hooks read the units and pass values.

```cpp
namespace smartrally {
bool hasTwoRallies(u16 buildingId);                 //Hatchery, Lair, Hive
//A resource for the right-click rule (§2.3).
bool isRallyResource(u16 targetId, bool sameOwner);
enum RightClickSets { SETS_NORMAL, SETS_WORKER, SETS_BOTH };
RightClickSets rightClickSets(bool hasTarget, bool targetIsResource, bool workerRallyOnTarget);
enum RallySource { USE_NORMAL, USE_WORKER, USE_NONE };
RallySource rallySource(bool twoRallies, bool unitIsWorker, bool workerRallySet);
enum RallyAction { ACT_VANILLA, ACT_MINERALS, ACT_GAS, ACT_ENTER };
RallyAction rallyAction(bool gathersOnRightClick, u16 targetId, bool targetCompleted,
                        bool sameOwner, bool rightClickLoads);
}
```

Host test cases include every row of §2.3, the three answers of `rallySource`, and for
`rallyAction`: each mineral id, each gas building finished/unfinished/own/not, the bare
geyser, a non-worker on minerals, a worker on its own Bunker (enter only if
`rightClickLoads`), and gather ahead of enter.

## 4. The worker rally table

- One entry per unit slot (1700): `{ bool set; CUnit* target; u16 x, y; }`, like the
  game's own rally (`rally.unit`, `rally.pt`). Only Hatchery, Lair and Hive entries are
  used.
- **Death.** When a unit dies, its own entry is cleared, and every entry targeting it keeps
  its point and loses the target, as the game does to a factory's rally (0x49E643, in the
  pass that clears references to a dying unit). `[VERIFY]` the hook point: that pass, so
  both happen exactly where the game's own clearing happens.
- **Slot reuse.** An entry counts only while its slot holds a Hatchery, Lair or Hive. A
  Drone morphing into a Hatchery in a slot where a Hatchery died is covered by the death
  clearing; the type check is the backstop.
- Lair and Hive keep the Hatchery's entry: the morph keeps the slot.

## 5. Commands

- **Right-click** (0x14, vanilla): the rule of §2.3 runs where the exe's right-click
  receive sets a factory's rally on a unit (0x45620D, 40 bytes, on to 0x456384; documented
  by upstream's `setRallyUnitWrapper_Jmp1`). Buildings with one rally keep the vanilla
  code. The ground case (0x456256) stays vanilla.
- **Set Worker Rally Point** sends a new plugin command,
  `[0x3E][u16 x][u16 y][u16 target tag]` (7 bytes; tag 0 = the ground), added beside
  0x3C and 0x3D (`selext::commandLength`, the executor's dispatch slot in
  `sel_inject.cpp`). On receipt, every selected Hatchery, Lair and Hive of the command's
  player gets its worker rally, as vanilla's targeted rally does for the normal one
  (`receive_command.cpp`, RallyPoint1/RallyPoint2). `[VERIFY]` 0x3E is free.
- **The button's click.** The button reuses the game's rally action (0x4244A0) with
  `actVar` 1; the plugin hooks 0x4244A0 to note, locally, that the targeting is for the
  worker rally, then runs it unchanged. When that targeting's command (0x15, with
  RallyPoint1 or RallyPoint2) is sent, it goes as 0x3E instead, the way smart-build sends
  0x3D (`selbuild::sendAsQueued`, at 0x485BD0). The note is cleared after the send and
  whenever 0x4244A0 runs with another `actVar`. `[VERIFY]` the vanilla rally button's
  `actVar` is 0, the action's calling convention, and the 0x15 send site.

## 6. Saves

The worker rally table goes into the plugin's save chunk (`hooks/selection_ext/sel_save.cpp`),
version 3 → 4: the set entries as `{ u16 building tag, u16 target tag (0 none), u16 x,
u16 y }`. A version 3 or older save loads with no worker rallies.

## 7. Sync, replays, multiplayer

- Synced, run on every client from synced state: the new-unit rule (0x466F50), the
  right-click rule, the 0x3E receipt, the death clearing, the game-start rallies.
- Local only: the button's "worker rally targeting" note, which only picks the command the
  local player sends.
- Every player in a game needs this build, as for the other synced changes.
- **Replays recorded before this change will play differently**: Drones no longer follow
  a Hatchery's rally, and melee games start with rallies set, so nearly every earlier
  melee replay. Same note as for Cancel Last and the gather queue (`docs/resolution.md`
  §6).

## 8. Data edits, the user's to make

In the Manifold Editor, on the Hatchery, Lair and Hive button sets: add **Set Worker
Rally Point** beside Set Rally Point, with the rally icon, Set Rally Point's condition
(`Rally_Point_While_Upgrading_Only`, 0x429740), action Rally Point (0x4244A0) with
`actVar` 1, and a new string ("Set Worker Rally Point", hotkey G). Save; repack.

## 9. The tutorial, reviewed

| Tutorial                                                                  | Here |
|---------------------------------------------------------------------------|------|
| Turns on upstream's whole rally hook set: it also replaces the rally setting inside the right-click and command receipt, whose own comments say other hooks must then be off (`receive_command` is on here) | Hooks 0x466F50 alone, plus the one right-click site (§5) |
| Worker = the units.dat Worker flag                                        | The unit's right-click gathers (its right-click action), as a right-click decides |
| Gas owner compared with the factory's                                     | With the new unit's (the same in practice) |
| Harvest1 for minerals and gas                                             | The right-click's own orders, MoveToMinerals and HarvestGas1 |
| Enter: `canBeEnteredBy`, which by its own note doesn't refuse a hallucinated transport or an Overlord without its upgrade | The right-click's load test (§2.2) |
| Enter includes the Nydus Canal                                            | Vanilla's Follow already enters it |
| A dead rally target                                                       | Safe in both: the game clears `rally.unit` when its unit dies (0x49E643); the worker rally mirrors it (§4) |

## 10. Later

- **A separate icon for Set Worker Rally Point** (the user, 2026-10-14): SC2's is the
  rally icon with a small Drone in it. Same icon until then. Also in `docs/resolution.md` §6.
- Town halls finished later get no automatic rally (the user chose game start only).

## 11. Test round (first build)

1. Melee start, each race: the starting CC/Nexus shows a rally line to a mineral field;
   the Hatchery shows a worker rally line; a new SCV, Probe and Drone go mining.
2. Hatchery: right-click a mineral field: worker rally there; Drones mine; Zerglings stay
   (no normal rally).
3. Right-click the same mineral field again: the normal rally goes there too; Zerglings
   walk there and stand.
4. Right-click the ground: normal rally there, worker rally unchanged.
5. Right-click your own Extractor (finished): Drones mine gas. A bare geyser: Drones walk
   there. An enemy's Extractor: sets the normal rally.
6. Set Worker Rally Point button (G) on the ground and on a unit: worker rally there.
7. Barracks rallied onto your own Bunker (with room) and onto a Dropship: Marines go in.
   A full Bunker: they walk to it. A hallucinated Shuttle: Zealots don't board.
8. Lair and Hive keep the worker rally; a Hatchery that dies and is rebuilt has none.
9. Save and load: worker rallies kept. A replay of this build plays back the same.
