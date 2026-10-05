# Smart-casting

Status: built, tested in game (2026-09-30)
Branch: `feature/resolution` (merged into `master`)
Detailed notes: `docs/selection.md` §6

## Goal
- SC2-style casting: for particular spells, only one unit of the selection casts per
  command, instead of every unit that can.
- Successive casts cycle through the casters, so each one casts before any casts twice.

## What the player sees
- **Spells.** Select 4 Ghosts and cast Lockdown 4 times: each lockdown comes from a
  different Ghost, starting with the one with the most energy. The 5th goes to whichever
  has the most energy then.
- **Walking casters.** A Ghost still walking to its target counts as having spent that
  energy, so the next cast goes to another Ghost.
- **Low energy.** Units without enough energy are skipped. If none has enough, the one
  with the most gets the order and shows the usual "Not enough energy".
- **Archon merges.** Each press of Archon Warp or Dark Archon Meld merges only the two
  Templar standing closest together. Templar already walking to a merge are left alone,
  so the next press takes the next closest pair.
- **Everything else is unchanged:** attack, move, patrol, stim, cloak, siege, burrow,
  rally points and Medic heal-move all still apply to the whole selection.

## Decisions
- **Cycling rule** (the user chose option (b) of two):
  - Each selection starts a round.
  - Every cast goes to the unit that hasn't cast yet this round and has the most energy
    left at cast time.
  - Ties go to the lower unit index.
  - When all units have cast, a new round starts.
- **What starts a round.** A selection that gains a unit starts a new round. Units dying
  or leaving the selection don't.
- **Merge pairs.** One pair per command, the closest pair among the free Templar. Ties go
  to the lower unit indices.
- **Where the choice is made.** It is made when the command is received, from synced
  state only, so every computer and every replay picks the same unit. The network
  commands are unchanged.
- **The spell list is compiled in, not read from `Manifold.ini`.** It decides which unit
  gets an order, so players with different lists would desync.
- **The list:**
  - Yamato
  - Lockdown
  - Parasite
  - Spawn Broodlings
  - Dark Swarm
  - EMP
  - Recall
  - Defensive Matrix
  - Psionic Storm
  - Irradiate
  - Plague
  - Ensnare
  - Stasis Field
  - Hallucination
  - Restoration
  - Disruption Web
  - Mind Control
  - Feedback
  - Optical Flare
  - Maelstrom
  - Archon Warp
  - Dark Archon Meld

## Implementation
- **Files:**
  - `GPTP/hooks/recv_commands/smart_cast.{h,cpp}`: the list (`smartCastOrders`), the
    round state, `pickCaster()` and `keepClosestPair()`.
  - `receive_command.cpp`: picks the caster before its selection loop, and the loop skips
    every other unit.
  - `CMDRECV_MergeArchon.cpp`: narrows the Templar list to one pair before the vanilla
    pairing loop.
  - `hooks/main/game_hooks.cpp`: `gameOn()` resets the rounds.
- **Hooks and addresses:**
  - `0x49AB00` (targeted orders, packet 0x15) and `0x4C0E90`/`0x4C0CD0` (the merges) are
    replaced by GPTP's reimplementations.
  - Both hooks were switched on for this feature in `initialize.cpp`; they had been
    written but disabled.
- **Round state:**
  - A per-unit "cast stamp": round, player and the unit's uniqueness byte at +0xA5, so a
    reused unit slot never inherits a stamp.
  - A round counter per player.
  - A snapshot of the selection the round belongs to.
  - It holds 12 units now and must grow with the 400-unit selection.

## Testing
- Passed in game (2026-09-30):
  - Lockdown cycling, walking caster, low energy
  - Psionic Storm, Defensive Matrix
  - unchanged orders (stim, cloak, siege, burrow, move, patrol, attack, attack-move,
    Medic heal-move, rally points)
  - Archon Warp and Dark Archon Meld one pair at a time
  - replay reproduces the same casters and pairs

## Known issues and follow-ups
- **Save games.** The round state isn't saved, so it restarts after a load. That is the
  same on every machine, so it can't desync.
- **400-unit selection.** The candidate and selection buffers in `receive_command` and
  the round snapshot hold `SELECTION_ARRAY_LENGTH` units; raise them with the 400-unit
  selection.
- **Queued casts.** Shift-casting behaves as vanilla, where spells can't be queued. If
  `orders.dat` ever makes spells queueable, queued casts already count toward a unit's
  spent energy.
- **Enabled hooks.** Turning on GPTP's `receive_command` means attack, attack-move,
  heal-move and rally points now run through GPTP's code. They were tested, but watch
  them after future GPTP changes.
