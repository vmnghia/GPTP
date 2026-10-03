# Smart-build (several workers build, Shift queues buildings)

Status: designed 2026-10-03, not built. Branch `feature/smart-build`.
Builds on the extended selection (`2026-09-30-extended-selection-design.md`, stage 5:
subgroups, the per-member button conditions, the select-chunk command and its length
hooks, the SELX save chunk).

## Goal
SC2-style building with several workers selected:
- The Build menus show with several workers selected.
- A plain placement goes to one worker; the others keep what they are doing.
- Shift-placement queues buildings, of one or several kinds, shared out across the
  selected workers, each building its share in order.
- Holding Shift keeps placing; the build menu stays open after a Shift sequence.
- Lockstep-safe: every pick is made on the receive side from synced state.

## What the player sees
- **Conditions.** The build buttons' own condition functions (the FireGraft condition
  entries) allow several selected units, so any button using them, including new ones,
  behaves the same. A mixed subgroup shows a building if any member can build it (the
  card checks each member alone, stage 5).
- **Plain placement** (no Shift): the selected worker able to build it that is nearest
  the site builds it; its orders are replaced as in vanilla. Afterwards the card returns
  to the basic card, as now.
- **Shift-placement**: the building is queued on one worker and the placement cursor
  stays on the same building for the next site.
  - **SCVs and Probes (balanced):** the able worker with the fewest queued builds; a tie
    goes to the one nearest the site, measured from its last queued building's site (or
    its own position if it has none); then the lowest selection index. Four workers and
    four depots: one each.
  - **Drones:** a Drone with no build order takes it, nearest first. When every
    selected Drone has one, the Drone with the earliest assignment that hasn't morphed
    has its build cancelled and takes the new one; the next placement takes the
    second-earliest, and so on.
- **Releasing Shift** ends placing; the card stays on the build menu (Basic or Advanced)
  it was on. Pressing a building there starts placing it.
- **A queued building starting** goes through the same checks as a plain one (supply,
  minerals, gas, the site still valid). If one fails, the player gets vanilla's error
  (e.g. "Not enough minerals") and the worker goes on to its next queued order.
  Resources are spent when each building starts, not when it is queued.
- A plain order (Stop, Move, a new plain Build) clears the worker's queue, as BW does;
  Shift+Move after the builds queues the move after them.
- **Not in this feature:** SC2's translucent images of queued buildings at their sites;
  AI use of smart-build.

## Verified facts (2026-10-03)
- Build orders are not queueable in the mod's `orders.dat` (DroneStartBuild 0x19,
  BuildTerran 0x1E, BuildProtoss1 0x1F, PlaceAddon 0x24 have `queueable` 0; Move 0x06,
  Attack 0x0A have 1). The Build command `[0x0C][order][u16 x tile][u16 y tile][u16 type]`
  (8 bytes) carries no queue flag.
- **Send:** the placement click (0x48E5xx) builds the 0x0C packet (`[0x640890]` = the
  tile position), queues it at 0x48E62E (0x485BD0), then calls 0x4843F0(0), which ends
  placing. 0x4C0560 is another 0x0C sender (CMDACT_Build).
- **Receive:** 0x4C23C0 (ESI = packet, only caller 0x486801) refuses a second selected
  unit, checks the tiles (0x57F1D4/0x57F1D6) and 0x48DBD0 (ECX = unit, DL = order,
  AX = type), then 0x48E190 (CL = order, AX = type, push the tiles; `ret 4`). 0x48E190
  calls 0x48E010 (normal) or 0x48E0A0 (addon); each takes its builder from the iterator
  again (`call 0x49A850` at 0x48E01E / 0x48E0B1) and ends in 0x48DE70, which checks
  supply and resources (errors via the advisor) and calls 0x467250 (fills
  `buildQueue[buildQueueSlot]`).
- **Order queue:** `CUnit::orderQueueHead/Tail` (+0x74/+0x78) of `COrder` {prev, next,
  u16 orderId, u16 unitId, Target}. 0x474810 (`CUnit::order`) appends an order with a
  unit type when stopPreviousOrders is false. 0x475000 (`prepareForNextOrder`) starts the
  head: order id to +0x4D, target to +0x58, and the entry's unit type to
  `orderUnitType` (+0x50) unless it is 0xE4. The build orders read their building from
  `buildQueue[buildQueueSlot]` (+0x98/+0xA4), not from `orderUnitType`, so a queued
  build needs that slot filled when it starts.
- **Build conditions:** 0x428990 / 0x428A10 (SCV Basic/Advanced), 0x428AD0 / 0x428B80
  (Probe), 0x428C30 / 0x428CB0 (Drone) return Invisible unless the client count
  (0x59723D) is 1 (`cmp byte [0x59723D], 1` then `jne`). 0x428E60
  (Can_Create_UnitorBuilding, 87 buttons) returns Invisible when the count is above 1
  unless the unit is a Larva (0x23), Mutalisk (0x2B) or Hydralisk (0x26).
- Holding Ctrl or Alt keeps the card's hotkeys from firing (they work from typed
  characters); Shift alone does not (user-tested).

## Design

### Conditions
- In 0x428990, 0x428A10, 0x428AD0, 0x428B80, 0x428C30, 0x428CB0: the count test no
  longer hides the button (the `jne` after `cmp byte [0x59723D], 1` is removed).
- 0x428E60: the list of unit types allowed with several selected gains "any worker"
  (`units_dat::BaseProperty & UnitProperty::Worker`).
- Patched in place, at these addresses, so every button pointing at them changes.

### Commands
- Plain placement: vanilla 0x0C, unchanged on the wire.
- Shift-placement: new command **0x3D** `[0x3D][order][u16 x tile][u16 y tile][u16 type]`
  (8 bytes), sent instead of 0x0C when Shift is held at the placement click.
  - 0x3D is free: vanilla's unused ids 0x3C–0x44 share dispatch slot 50 (0x486ED0),
    whose stage 2 stub (`selectChunkDispatch`) already takes 0x3C; it also takes 0x3D.
    The replay recorder's skip list (0x502860: 0x05–0x08, 0x10, 0x11, 0x37–0x3B) leaves
    it in, so replays record it.
  - Its length (8) goes into `selext::commandLength`, which the stage 2 hooks at
    0x48661C / 0x4CE085 / 0x4CDD04 already use for every command.

### Receive (synced)
- **0x0C** (replaced 0x4C23C0): the builder is the selected worker that passes 0x48DBD0
  for this order and type, nearest the site (squared distance from the unit's position
  to the site's centre), ties to the lowest selection index. Then vanilla's path with
  that builder: tile check, 0x48E190 with the builder fed to 0x48E010/0x48E0A0 at their
  iterator calls. With one unit selected this is vanilla.
- **0x3D**: same able-worker set (0x48DBD0 passes; PlaceAddon is never queued: a
  building alone takes the 0x0C path). The pick:
  - Terran and Protoss workers: fewest build orders (current plus queued); then
    nearest to the site from the worker's last queued build site (else its position);
    then lowest index.
  - Drones: one without a build order, nearest first; else the one with the lowest
    assignment number that is not already landing or morphing (0x48DE70's own test:
    DroneBuild, or DroneLand with NoBrkCodeStart/CanNotReceiveOrders). Assignment
    numbers come from a per-player counter, stamped when a Drone is picked.
  - **A pick with no build order** (mining, idle, moving, or a recycled Drone) starts
    the building now, through the plain path (0x48E190 with that builder): vanilla's
    checks, errors and order, its other orders replaced, as a plain build would.
  - **A pick already building** gets it appended to its queue (0x4745F0 with
    `orderId`, the site's centre, and the type marked with 0x8000), after vanilla's
    queue checks (0x4754F0): at most 8 queued orders ("Unit's waypoint list is
    full"), and the global order count below 1800. The site is checked when queued
    (0x473FB0 and its message, as 0x48E010 does).
  - No able worker: the placement is dropped (the vanilla error of the check that
    failed is shown).
- **A queued build starting:** the main order dispatcher 0x4EC4D0 (EAX = unit, every
  unit, every frame) checks first: if the current order is a build order and its
  `orderUnitType` carries the 0x8000 mark, the mark is cleared and 0x48DE70's command
  time checks run (minerals, gas, supply, with their errors), then 0x466E80 and
  0x467250 fill the build slot. A failed check sends the worker on with `orderToIdle`
  (the next queued order, or idle). Arrival still runs vanilla's own checks (money at
  0x467030, the site at createUnit), so a site blocked since queueing fails as in
  vanilla.
- **Assignment numbers:** synced, per unit (indexed by unit index), with a per-player
  counter; saved in the SELX chunk (version 3); older saves start at 0, so ties go to
  the lowest selection index.

### Local UI
- At the placement click (0x48E5xx): Shift held → send 0x3D and skip the end-placing
  call (0x4843F0); no Shift → vanilla.
- Releasing Shift while placing: placing ends (as Esc/right-click does), but the card
  keeps its current build menu set instead of returning to the basic card.

## Testing
- Host tests (pure): the balanced pick (fewest, then nearest from the queue's end, then
  index), the Drone pick (free nearest; else earliest assignment), the plain nearest
  pick with its tie rule.
- In game:
  - 10 SCVs: place 1 depot (the nearest builds, the rest keep working); Shift-place 6
    depots, 2 barracks and a bunker (spread out, each worker builds its share in order);
    the menu stays open while Shift is held; releasing Shift keeps the build menu; a
    plain placement returns to the basic card.
  - Probes: the same.
  - 3 Drones, 5 Shift-placements: the first three take one each; the 4th and 5th
    replace the earliest two.
  - Not enough minerals when a queued build starts: the error shows, the worker goes on.
  - A plain Move clears the queue; Shift+Move after builds runs after them.
  - One worker selected: everything as vanilla.
  - Save and load mid-queue; watch a replay of all of it (no desync).

## Known issues and follow-ups
- Images of queued buildings at their sites (SC2).
- AI smart-build.
