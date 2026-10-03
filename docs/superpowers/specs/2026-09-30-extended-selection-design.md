# Extended selection (larger selections, pages, control groups)

Status: stages 1+2 built and tested in game (2026-10-01, build 03:26); stage 3 (pages)
built and tested (2026-10-01, build 23:00, plan
`docs/superpowers/plans/2026-10-01-extended-selection-stage-3.md`); stage 4 (control
groups) built and tested (2026-10-02, build 00:09, plan
`docs/superpowers/plans/2026-10-02-extended-selection-stage-4.md`); stage 5 (subgroups and the command card) designed, not built. Stage 1+2
plan: `docs/superpowers/plans/2026-09-30-extended-selection-stage-1-2.md`.
Found during testing and review (details in the stage sections): the health-bar lookup
0x4D603C, the 80-circle and 12-health-bar image pools, and chunks needing an index so a
lost middle chunk drops the packet.
Branch: stages 1–4 on `master` (local); stage 5 on `feature/command-card`
Detailed notes: `docs/selection.md` §1–§5 (the first survey; where it and this spec
disagree, this spec wins, because every address below was re-checked in the exe)

## Goal
- Raise the selection limit above 12. The working target is 400, the most units at 200
  supply (all Zerglings), but **the number is not settled**: it lives in one constant,
  `SEL_MAX`, and no file, function or variable name contains it.
- Control groups (Ctrl+0–9 and the 8 recent-selection groups) hold as many units as a
  selection.
- The selection panel shows the selection in pages.
- Single-player first. Everything must stay lockstep-deterministic, so multiplayer can
  follow later without a redesign.

## What the player sees
- **Selecting.** A drag box, shift-drag, double-click or Ctrl-click can select up to the
  limit. Buildings and units still don't mix. A shift-add that would pass the limit is
  refused whole, as vanilla refuses one that would pass 12.
- **Orders.** Every selected unit takes the order. Group moves use the whole selection.
  Smart-cast spells and archon merges choose from the whole selection.
- **Circles.** Every selected unit shows its selection circle.
- **Panel.** The wireframes continue into more columns of the wide StatData box. Ctrl+PgUp
  and Ctrl+PgDn change the page, stopping at the first and last page. A new selection starts
  on page 0. When units die, the page stays, moved back to the last page if needed.
  - Click a wireframe to select that unit. Shift-click removes it. Ctrl-click selects
    every unit of that type in the **whole** selection, not just the page.
- **Subgroups and the card** (stage 5). The selection splits into subgroups by unit
  type. The card shows the active subgroup's buttons; Tab cycles subgroups.
- **Control groups.** Ctrl+number, Shift+number, number and Alt-click (the recent
  selections) work at the full limit.
- **Saves.** Selections and groups survive save and load. A save from before this
  feature loads with the first 12 units of each selection and group.
- **Not in this feature:** page buttons, a page indicator.

## Decisions
- **The mirror** (chosen over repointing every reference or rewriting ~90 functions).
  - New arrays sized `SEL_MAX` are the real selections and groups.
  - The vanilla 12-slot arrays stay where they are. After every change, `syncMirror()`
    copies the first 12 into them.
  - The rule: **every function that writes a selection moves to the new arrays in the
    stage that owns it; readers may move later.** A reader that hasn't moved sees the
    first 12, which is exactly vanilla behaviour, so a half-finished migration never
    breaks anything.
  - Converted code never writes the vanilla arrays except through `syncMirror()`.
- **Synced-only rule.** Nothing written to a synced array (`playersSelExt`,
  `groupsExt`, the pending chunk buffers) may depend on local state: the screen, the
  local player, or the UI arrays. Only command data and game state.
- **Naming.** `selection_ext`, `...Ext`, `SEL_MAX`. The spec says "the limit".
- **A new "select chunk" command** for selections above 12 (chosen over 0x09 plus 0x0A
  continuations). A 0x0A continuation can't be told apart from a real shift-add, so it
  would push junk entries into the recent-selection ring, break the overflow rule, and
  leave a partial selection in place across turns.
- **Selections of 12 or fewer keep vanilla 0x09/0x0A/0x0B**, so ordinary play looks
  vanilla in replays.
- **Circles on every selected unit from stage 1.** Sprite flag 0x08 means "selected by
  the local player" and 19 sites test it, so it must be set on every selected unit. In
  exchange, every reader of the u8 `CSprite::selectionIndex` is replaced by a lookup in
  `activeSelExt`.
- **Vanilla rules kept on receive:** skip null, Nuclear Missile, duplicate and hidden
  units; buildings and units don't mix; a shift-add past the limit is refused whole;
  control-group assign stops at the first unit the player doesn't own; recall keeps
  vanilla's checks (alive, not hidden, owned, multi-selectable unless the group has one
  unit).
- **Vanilla bugs not kept:**
  - Control-group add spills its 13th unit into the next group's slot 0; ours stops at
    the limit.
  - The hotkey receive guard lets group 18 through (`cmp al, 0x12; ja`); ours is
    `group < 18`.
  - Clearing the last-sent selection zeroes 3 of 12 slots (0x49A320, 0x4BF8A0); ours
    clears the whole array.
- **Breaking old saves is accepted,** but loading one must not crash.

## Architecture

### Storage (`GPTP/SCBW/selection_ext.h/.cpp`, one owner)
| Array | Size | Kind | Mirrors |
|---|---|---|---|
| `playersSelExt` | `CUnit*[8][SEL_MAX]` | synced, saved | 0x6284E8 `CUnit*[8][12]` |
| `groupsExt` | `u32[8][18][SEL_MAX]` tags | synced, saved | 0x57FE60 `u32[8][18][12]` in CGame |
| pending chunk buffer | `u16[8][SEL_MAX]` + count, mode | synced, not saved | — |
| `activeSelExt` | `CUnit*[SEL_MAX]` | local | 0x6284B8 |
| `clientSelExt` + `u16` count | `CUnit*[SEL_MAX]` | local | 0x597208 + u8 0x59723D |
| `lastSentExt` | `CUnit*[SEL_MAX]` | local | 0x59724C |
| `reselectExt` | `CUnit*[SEL_MAX]` | local | 0x596B7C (re-selected at game start) |
| page state | `u16` page, page size `P` | local | — |

Group tags use the vanilla encoding: 11-bit unit index plus uniqueness in the upper bits
(verified in 0x4967E0: `and edx, 0x7ff` ... `sar eax, 0xb; cmp` against unit+0xA5).

### Iterator 0x49A850
- Replaced with a version that keeps a u16 cursor in plugin memory.
- **Every other write to the vanilla cursor byte 0x6284B6 stores 0** (32 as immediate 0,
  7 as `bl` right after `xor ebx, ebx`, all checked). So the new iterator treats "byte is
  0" as a reset and writes a nonzero byte while it is partway through. None of the ~40
  command handlers that reset it need to change.
- Like vanilla, it calls the remove-from-all routine on a dying unit (order Die, state 1)
  and re-reads the same slot, and it stops at the first null. Unlike vanilla, a unit
  whose sprite is gone is removed without touching the sprite (vanilla 0x49A7F0 would
  dereference it).
- The iterator is the only synced reader of the selection, so converting it gives every
  order handler, and OpenBW-style group-move logic, the whole selection.

## Stage 1: storage, iterator, writers, circles

The four decompile reports of 2026-09-30 settled every function below (calling
convention, semantics, callers). Functions with **no references at all** in the exe are
dead code and are not touched: 0x49A320, 0x4BF8A0, 0x4BF870, 0x4CE700, 0x4D02D0,
0x49A7C0, 0x49A8B0, 0x4967A0, 0x4BF9A0, 0x4CDE10.

**Hooked (whole function replaced):**
| Address | Role | Convention |
|---|---|---|
| 0x49A850 | iterator | none → EAX; keep EBX/ESI/EDI/EBP |
| 0x49A7F0 | remove from every player's selection, the dashed ally circle, and last-sent (its tail jump to 0x4BF8C0) | EDI = unit |
| 0x49A740 | clear one player's selection (also called when a player leaves, 0x4C4F69) | EAX = player |
| 0x4C2750 / 0x4C2560 / 0x4BFB40 | receive 0x09 / 0x0A / 0x0B | stdcall(packet), `ret 4` |
| 0x4C2870 | receive hotkey 0x13 | ECX = packet |
| 0x49AE40 | build the local selection from a list (no bound in vanilla: past 12 it writes into the synced array) | EAX = list (written back), stdcall(count) |
| 0x49F7A0 | local removal (the only `selectionIndex` array-offset reader) | EAX = unit |
| 0x499A60 | redraw every circle after a save (which strips them all) | none |
| 0x4C38B0 | client copy + portrait (does not sort) | none; tail `jmp 0x458DE0` |
| 0x4C3B40 | local deselect-one and send | EDX = unit |
| 0x4D0820 | re-select the saved local selection at game start | none |

**Not hooked: 0x49B690** (redraw circles after an alliance or colour change). Its per-slot
loop only touches units the local player doesn't own, which can only be in slot 0, and its
dashed-circle pass reads the allies' mirror; so past 12, only allies' dashed circles keep
their old colour until the next selection.

**Not hooked, because every live caller is replaced:** 0x49A170 (remove from one player),
0x49AF80 (add), 0x4965D0 (group assign/add), 0x496940 (group recall), 0x496560 (oldest
ring slot). Their logic is reimplemented inside the replacements.

**Patched in place:**
- 0x4EED10 (game start/load) at its entry, to clear the new arrays alongside vanilla's
  clears, and at 0x4EEDC6, where vanilla copies the local player's row to 0x596B7C and
  wipes every synced selection: the new version copies `playersSelExt[local]` to
  `reselectExt` and wipes the new arrays too.

**Receive rules, reproduced faithfully (from the decompile):**
- 0x09: refuse count > 12; clear the player's selection even for count 0; for each tag:
  skip invalid, dying, duplicate, and Nuclear Missile; slot 0 takes any visible unit,
  later slots need multi-selectable (0x47B770) and owned by the active nation. Ring push
  if more than 1 unit was added.
- 0x0A: refuse count > 12 or `count + current > limit` (the raw count, refused whole); the
  same checks without the Nuclear Missile one; ring push if the total is more than 1.
- 0x0B: refuse count 0 or > 12; remove each valid tag; ring push if the count left after
  the last valid tag is more than 1.
- Ring push: oldest slot by timestamp (ties to the higher index), assign the selection to
  group 10 + slot, stamp it with the low 16 bits of frame counter 0x57EEBC. Vanilla's
  all-0xFFFF case (slot 0xFF → group 9, out-of-bounds stamp) keeps the group-9 write and
  skips the out-of-bounds stamp.
- Hotkey: group must be < 18 (vanilla lets 18 through). Assign/add/recall follow
  vanilla; add stops at the group's capacity instead of spilling into the next group.
  In stages 1–3 groups stay the vanilla 12-slot arrays; recall writes the new selection.
- Dashed ally circles (sprite flags 0x06, images 0x23B–0x244, gated on the local-state
  test 0x49A110) are kept by calling the vanilla helpers; they are local visuals only.

**Fixed-size consumers of the iterator:**
- **`CMDRECV_MergeArchon.cpp` (enabled) must be fixed:** both functions fill
  `templars_stored[12]` from the iterator with no bound.
- `receive_command.cpp` caps its `selection[]` / `candidates[]` at 12 and
  `smart_cast.cpp` keeps a 12-entry `roundSelection`: all move to `SEL_MAX`.
- In the exe, of the 38 functions that call the iterator, only the vanilla archon
  receivers store into a stack array, and GPTP replaces them.

**Circles and `selectionIndex`:**
- The new 0x49AE40 calls 0x4E6180 for every selected unit, so every one gets flag 0x08
  and a circle. `CSprite+0x0B` gets `min(slot, 255)`.
- The readers that use `CSprite+0x0B` as an array offset are 0x49F7B3 (in 0x49F7A0,
  replaced: it searches `activeSelExt` instead), **0x4D603C** in the health-bar setup
  0x4D6010 (hooked: `activeSelExt[slot]` below 255, else a search by sprite; found in the
  stage 1 test round, where it crashed on the stale mirror), and GPTP's shift-click in
  `selection.cpp` (stage 2, same fix). A search of every register-indexed read of
  0x6284B8 and 0x597208 confirms there are no others. 0x49F00B and 0x49F8B6 only save the byte and hand it back to
  0x4E6180, which is harmless. The other candidates were `CImage` fields. So no per-sprite
  slot table is needed.
- **Image pools.** Circles and health bars come from two small dedicated pools that
  0x4D6930 builds at game start: 80 circle images at 0x57D768 (free list 0x52F564/0x52E4C0,
  shared with allies' dashed circles) and 12 health-bar images at 0x57EB78 (free list
  0x5254B8/0x57EB6C). When a pool is empty the image is silently not made. The calls to
  0x4D6930 at 0x4EEE5F and 0x4EEFED are wrapped to append `SEL_MAX * 8` circle images and
  `SEL_MAX` health-bar images owned by the plugin. (Found in the stage 2 test round: only
  80 circles and 12 health bars showed.) Health-bar images need the pool init's per-image
  setup (0x4D68C0), including their own 14-byte GRP frame at `image+0x2C`, which 0x4D6010
  fills per unit; circle images are set up in full when allocated (0x4D6810).
- 17 of the 19 flag-0x08 tests are sprite "selected" tests; their actions (0x4C3B40,
  0x49F7A0, 0x45D040, HP-bar redraws) are either replaced or size-independent.

**Save chunk, version 1:**
- Save: the last vanilla write in 0x4C2910 is `fwrite(&screenY)` at 0x4C2E0A. Its call is
  retargeted to a wrapper that does that write and then appends a raw header
  `{u32 magic 'SELX', u16 version, u16 saved limit, u32 payload bytes}` and a compressed
  payload (0x4C3450) of `playersSelExt` as tags. A failed write returns 0, which takes
  vanilla's failure path (delete the file, error dialog).
- Load: the last vanilla read in 0x4CFEF0 is `fread(&screenY)` at 0x4D0225, after the
  units exist. Its call is retargeted to a wrapper that then reads the raw header with the
  exe's `fread`. A short read or a wrong magic means an old save: `playersSelExt` is
  filled from the vanilla 12. A bad payload (0x4C3280 returns 0) returns 0, which takes
  vanilla's clean failure path (error dialog, back to the menu). Load keeps
  `min(saved limit, SEL_MAX)` per selection.
- The exe's own CRT must be used (fwrite 0x411931, fread 0x4117DE, both cdecl), since
  the `FILE*` belongs to it.
- After a load, 0x4EED10 keeps only the local player's row (for re-selection) and wipes
  every synced selection; 0x4D0820 then re-sends it as select commands. So the chunk
  matters for the local player's restored selection.

**What stage 1 alone can show:** nothing above 12 can be selected until stage 2, so the
test is that everything still works at 12 or fewer, including save/load, deaths, loads
into transports, morphs, mind control and archon merges.

## Stage 2: select commands and client-side list builders

**Verified in the exe (send-side decompile report):**
- Executor 0x4865D0: `eax` = buffer, `[ebp+8]` = bytes left, `[ebp+0xC]` = 1 for
  replay-file commands. Normal dispatch at 0x48663F: `id - 5` through index table 0x486EF0
  and jump table 0x486E08. Slot 50 (dword at **0x486ED0**, currently 0x486DA3, the
  discard exit) is shared by unused ids 0x3C–0x44, 0x48 and 0x5B. Replay-file commands use
  the normal dispatch too. During a replay, the viewer's own network commands use a filter
  table and a skip path whose length comes from site 0x48661C.
- Length sites (table 0x5005F8, special case 0x09–0x0B):
  - 0x48661C, the replay-viewer skip path: with length -1 it loops ~2^31 times.
  - 0x4CE085, the replay playback frame reader 0x4CDFF0: with -1 it memcpys 4 GB.
  - 0x4CDD04, the replay save walk 0x4CDCE0: with -1 it never ends.
  - 0x4BF9A6 and 0x4CDE3A are in dead functions.
- Recorder 0x4CDE70: records `[storm id][command]`; skips size ≥ 0x100 and the ids at
  0x502860 (0x3C is not among them). The block size byte wraps at a 255-byte command, so
  **a command may be at most 254 bytes**.
- `QueueGameCommand` 0x485BD0 (ECX = data, EDX = size): if the command doesn't fit the
  turn (capacity 0x57F0D8, ≤ 512) it flushes early via 0x485A40, unless in menus
  (`gwGameMode` 4) or 16 − [0x57F090] turns are in transit, in which case it drops it.
- Replay playback joins one frame's commands, from every player, into a 512-byte buffer
  (0x6552B0) with no bounds check; ids and lengths go to separate arrays. One player's
  turn is at most 512 bytes, so single-player replays are safe. Several players each
  sending a full turn in one frame could overflow it (a latent vanilla bug that large
  selections make likely): see "Known issues".
- `CMDACT_Select` 0x4C0860, `stdcall(count, list)`: diffs the list against last-sent;
  new units need to be visible (0x57F0B0, or 0x6D0F18 in replays). If adds + removes ≥
  count it sends one 0x09 with the list **reversed**; otherwise a 0x0B of the removes then
  a 0x0A of the adds. It copies the list into last-sent unclamped. It sends in replays too.

**The command:** `[0x3C][flags][u8 count][u16 unit tag × count]`, length `3 + 2*count`.
- `flags`: bits 0–1 mode (0 replace, 1 add, 2 remove), bit 2 first, bit 3 last, bits
  4–7 the chunk's place in its packet (mod 16).
- **At most 125 units per chunk (253 bytes).**
- 0x3C is free: GPTP only sends 0x14 and 0x36, and SCManifold contains only the MPQDraft
  stub, WMode and GPTP.
- **Receive:** hooked by writing the stub's address into dispatch slot 50 (0x486ED0). The
  stub handles id 0x3C (length check, `[ebp-4]` = length, then `jmp 0x486D7A`, which
  records it and advances) and sends every other id of that slot to 0x486DA3 as before.
  "First" resets the player's pending buffer and sets its mode; each chunk appends; a
  chunk with no pending "first" or another mode is dropped; "last" commits under the same
  rules as 0x09 (replace), 0x0A (add) or 0x0B (remove), with the limit in place of 12,
  and pushes the ring once.
- **Length sites patched:** 0x48661C, 0x4CE085, 0x4CDD04 (the dead two are left alone).
- **Sender:** a packet of 12 or fewer tags goes out as vanilla 0x09/0x0A/0x0B, which the
  new receive handlers accept at the full limit. A packet of more goes out as ⌈n / 125⌉
  chunks. The diff against `lastSentExt`, the visibility rule and the reversed order
  follow vanilla. In replays, large packets are not sent (the viewer's selection is local
  only, and the executor would skip them anyway).
- **A dropped chunk** never reaches any machine. The receiver checks each chunk's place,
  so a gap anywhere (first, middle or last) drops the whole packet: the commit either
  doesn't happen or is replaced by the next "first". That is deterministic. (The final
  review found that a lost middle chunk used to commit a partial packet.) The local UI can briefly
  disagree, as in vanilla when a command drops.
- **The pending buffer is not saved.** A selection half-received at save time simply
  doesn't happen after load.

**Client-side list builders moved to `SEL_MAX` lists:**
- GPTP `selection.cpp` (enabled): drag box 0x46FA40, click / double-click / Ctrl-click
  0x46FB40, shift-combine 0x46F290, sorting 0x46F0F0. The shift-click removal searches
  the list instead of using `selectionIndex`. The list-full helper 0x46F040 takes its
  length as an argument, so it works at any size.
- `CMDACT_Select` 0x4C0860 replaced (above). `CMDACT_HotkeyUnit` 0x4C07B0 copies the
  recalled list into last-sent unclamped; it is replaced to copy into `lastSentExt`.
- 0x45D040 (the egg's twin Zergling or Scourge joins the selection): replaced to append
  to `activeSelExt`.
- 0x49AEF0 and 0x46FA00 need no change: they only pass the list and count to 0x49AE40 and
  0x4C0860.
- Larva select (0x423930, `select_larva.cpp`) needs no change: buildings can't
  multi-select, so it yields at most 3 larvae.

## Stage 3: selection pages

**Verified:** the KEYDOWN proc 0x484350 is a bare `ret`. Tooltips check control ids
0x21–0x2C (0x457D75/0x457D7B). The click handler 0x458220 loops over exactly 12
controls from 0x21. 0x4584C0 registers 0xB0 bytes (44 handlers) from 0x504AF0 through
0x418100, which does no bounds check, then sets [0x68C1F8] = 1. GPTP's panel fill
`unit_stat_selection.cpp` is OFF and untested.

- **Page size** `P` = columns that fit the StatData box at the current resolution × 2
  rows. Wireframe `k` on page `p` shows `clientSelExt[p*P + k]`. Local UI only.
- **Hotkeys: Ctrl+PgUp/PgDn** (the user's choice, 2026-10-01). Plain PgUp/PgDn are not
  free: vanilla's keyboard scroll (0x47EF80) scrolls the map diagonally with them (held
  state at 0x596A39/0x596A3A). The in-game KEYDOWN proc 0x484350 turns the page with Ctrl
  held, and the scroll's two reads (0x47EFEA, 0x47F03A) see the keys as up while Ctrl is
  held. The chat box (TextBox, `[0x68C140]`) lets PgUp/PgDn through, so paging is ignored
  while it is visible.
- **`statdata.bin`:** wireframe controls 45 onward, up to the largest `P` of any
  supported resolution, from a generator script next to the command-card one in
  `D:\SC Modding\SCManifold\to-repack\`. Controls beyond `P` stay hidden.
- **Interact table:** hook 0x4584C0 to register a plugin table with room for every
  wireframe.
- **Fill 0x425960:** a new version that fills the page slice. GPTP's OFF version is a
  reference only, after checking it.
- **Refresh:** the snapshot 0x424540 and condition 0x424660 compare `P` slots in plugin
  arrays instead of the 12-slot 0x6CA94C / 0x6CAD7C (end bound 0x6CAD96 at 0x424651). A
  page change sets [0x68C1F8] = 1.
- **Click 0x458220:** replaced whole (plain / Shift / Ctrl as above). Ctrl-click sends a
  chunk command when the result has more than 12 units.
- **Tooltips 0x457CE0:** hook the id range check.

## Stage 4: control groups

**Verified:** the hotkey command is `[0x13][sub][group]` with no unit list. Receive
0x4C2870: sub 0 → 0x4965D0(1) assign, sub 2 → 0x4965D0(0) add, sub 1 → 0x496940
recall. Groups live in CGame, which is saved whole (0x17700 bytes from 0x57F0F0, written
at 0x4C2B09 / 0x4D0474, read at 0x4CF6C4). The recent-ring timestamps 0x63FE40
(`u16[8][8]`) are outside CGame, not per unit, and need no change. 0x496560 picks the
oldest ring slot.

- **No packet change:** recall and assign work on synced state.
- **Receive** (0x4C2870, 0x4965D0, 0x496940 replaced): the guard, assign, add and recall
  rules in "Decisions". Add (Shift+number) fills the group up to the limit and stops,
  as vanilla fills up to 12; it is not refused whole (that rule is the selection's 0x0A).
- **Recent ring (groups 10–17):** a completed select of more than 1 unit (vanilla 0x09 /
  0x0A or one chunk commit) is copied into the oldest ring slot once.
- **Local side:** recall 0x496B40 (GPTP `selectUnitGroup`, enabled) and Alt-click
  0x496D30 read `groupsExt`. Double-tap centring 0x4967E0 (`cl` = group) is replaced to
  read `groupsExt` and centre on the view's size.
- **Save chunk, version 2:** adds `groupsExt` after the selections.

## Stage 5: subgroups and the command card

Replaces the first stage 5 design (button conditions over the whole selection). The
goal is SC2's command card: the selection splits into **subgroups** by unit type, one
subgroup is **active**, and the card shows that subgroup's buttons.

**What the player sees**
- The panel is sorted by subgroup, highest priority first, and each subgroup's units
  sit together. The active subgroup's wireframes are highlighted and the rest dimmed.
  The look is chosen from rendered mock-ups before it is built.
- The card and the portrait are the active subgroup leader's: its first unit in panel
  order. The card's hotkeys work at once, without selecting the subgroup first.
- **Tab** activates the next subgroup, **Shift+Tab** the previous one, both wrapping.
  Not while chat is open. With one subgroup, Tab does nothing. If the new subgroup's
  first unit is on another page, the panel goes to that page.
- A new selection (click, box, Ctrl+click, double-click, group recall, Alt-click)
  activates the highest-priority subgroup. A shift-add, shift-remove or a death keeps
  the active subgroup while any of its units remain; when its last unit goes, the
  highest-priority subgroup is active, as in SC2 (user, 2026-10-03).
- **Ctrl+click and double-click** select every unit with the same subgroup key on
  screen, so a burrowed Hydralisk takes the unburrowed ones too, and a sieged tank the
  unsieged ones.
- A button shows if **any** unit of the active subgroup can use it. Burrowed and
  unburrowed Hydralisks show Move, Stop, Attack, Patrol, Hold and Burrow. Pressing a
  button orders every selected unit able to obey: Burrow on the Hydralisk card also
  burrows selected Zerglings, as in SC2.
- **Build** with several workers selected stays hidden, as vanilla, until smart-build
  (a separate feature, below).
- Replays: the card is unchanged (the replay set). Tab still moves the highlight.
- Tab is not yet confirmed unused by vanilla in game; the plan checks it first and stops
  for a decision if it is taken.

**Verified (2026-10-02)**
- `BUTTON_SET` (12 bytes at 0x5187E8 + 12 × set): button count, first `BUTTON` (20
  bytes: position, icon, `reqFunc`, `actFunc`, reqVar, actVar, two string ids),
  `connectedUnit`. Only set 16 (Kerrigan) is connected (to 1, Ghost).
- Button condition results: Enabled 1, Disabled −1, Invisible 0
  (`BUTTON_STATE`). Vanilla conditions loop the 12-slot client selection 0x597208 and mix
  "any" and "all": Hydralisk set 38 has Move/Stop/Patrol/Hold on 0x4283C0 (hidden if
  any unit is burrowed), Burrow on 0x4290F0 (shown if any unit can burrow), Unburrow on
  0x429070 (shown only if all are burrowed). Burrow and Unburrow share position 9, so a
  mixed selection shows Burrow and hides Move (checked in game).
- **Toggle families.** Every on/off pair shares one position, chosen by condition:
  Siege/Unsiege at 7 (sets 5 and 30 hold both buttons, likewise 23 and 25);
  Cloak/Decloak at 7 (Ghost, Wraith, Kerrigan, Kazansky, Infested Kerrigan, Duran,
  Stukov, Infested Duran); Burrow/Unburrow at 9 (Zergling, Hydralisk, Drone, Defiler,
  Infested Terran, Lurker, Unclean One, Hunter Killer, Devouring One); Lift/Land at 9
  (Terran buildings, which can't be multi-selected). **Only siege changes the unit
  type** (Siege Tank 5 ↔ 30, Edmund Duke 23 ↔ 25).
- **Ctrl+click filter** 0x46F0F0 (GPTP `SortAllUnits_Helper`): same owner, same type,
  same burrow state, same detection state (unless burrowed), same hallucination state.
- The vanilla card choice (GPTP `updateButtonSetEx`, 0x458BC0) picks a merged set when
  types differ: GroupMixed, GroupPeons, GroupCloaker or GroupBurrower.
- Build receive refuses a Build with more than one unit selected, and the Build-menu
  conditions (SCV, Probe, Drone and the build/train buttons) need exactly one unit
  selected (u8 count 0x59723D == 1).
- Smart-casting's buffers and round snapshot already use `SEL_MAX` (stage 1–2).

**Design**
- **Subgroup key** = owner, unit type with the siege aliases (30 → 5, 25 → 23), and the
  hallucination flag. Burrow, cloak and detection state are ignored. Hallucinations
  are their own subgroup, ranked right after the real units of the type. The same key
  drives Ctrl+click, double-click and Ctrl-click on a wireframe (stage 3).
- **Priority table:** one `u16` per unit type in the plugin, filled at game start:
  heroes first (units.dat hero flag), then higher build score, then lower unit id. It
  is the one place a later editor writes (an override file loaded over the defaults;
  no format now). It only orders the UI, so a different table on another machine can't
  desync.
- **Active subgroup:** local UI state like the page, not synced and not saved. A load
  activates the highest-priority subgroup.
- **The panel order is local too.** The synced selection keeps its order; the panel
  draws from a sorted view of it.
- **Card:** the leader's own button set; the merged group sets are no longer chosen
  (except in replays, which keep the vanilla card).
- **Conditions, "any member can":** each button's existing condition is called once per
  member of the active subgroup, with the 12-slot client selection mirror temporarily
  holding only that unit, then restored. The mirror's count stays the real selection's,
  so the Build menus stay hidden with several units selected. The button takes the best result:
  Enabled > Disabled > Invisible, stopping at the first Enabled. No condition is
  rewritten, so conditions not looked at come along. It runs when the card refreshes,
  not every frame.
- **On/off pairs** keep sharing a position until the button editor moves them; the
  vanilla set order decides which one wins (the "on" one, as in vanilla).
- **Actions** send the same commands as now, for the whole synced selection; receive
  already drops units that can't obey. No new synced command.

**Other readers** (from the first stage 5 design, still to classify in the plan)
- The u8 count 0x59723D (23 readers): the mirror holds min(n, 12), so `== 0`, `== 1`
  and `> 1` behave the same. Any comparison against 12 or more is converted.
- 0x46F5B0 (send side: does any selected caster have the energy), 0x455A00, 0x4563A0,
  0x4564E0, 0x458120, 0x458DE0, 0x464360, 0x492CC0, 0x49FED0, 0x4E5640: converted where
  they differ above 12, or where the card change needs them.
- GPTP code still looping `SELECTION_ARRAY_LENGTH` over the client selection
  (`btns_cond.cpp`, `buttonsets.cpp`, the status display, `right_click_CMDACT.cpp` and
  others): each is classified as enabled or `//OFF` in `initialize.cpp`, and enabled
  ones are converted.

**TODO (with the button editor, not this stage):** on/off pairs side by side, the "on"
button (Siege, Cloak, Burrow) at position 11 and the "off" one (Unsiege, Decloak,
Unburrow) at 12. They are entries in each type's own set; the card needs no code change
to show both.

**Smart-build (separate feature, not this stage):** several workers selected can build,
like smart-casting: each order goes to one worker (synced pick), Shift queues several
buildings, of several kinds, across the workers, and the build menu stays open after each
placement instead of going back to the basic card (as in SC2). Vanilla hides Build with
several workers so they don't all walk to one site and contend for it.

**Out of scope:** Lift/Land (buildings aren't multi-selectable), multiplayer testing,
any editor UI.

## Testing
Each stage ends with a build and a numbered in-game test round, using hero units.
- **Stage 1:** everything at 12 or fewer is unchanged: select, shift-add, shift-remove,
  orders, deaths, loading into a transport or bunker, gas harvesting and nydus (units
  stay selected while hidden), morphs (egg, lurker), mind control, archon merges,
  smart-cast, save and load, circles.
- **Stage 1+2 together:** select 400 Zerglings with a drag box; move, attack and
  patrol; shift-add until refused; shift-remove 200; twin Zerglings from selected eggs
  join; rapid reselects while the game lags; watch a replay of it; save and load with
  400 selected.
- **Stage 3:** page with Ctrl+PgUp/PgDn and stop at the ends; plain PgUp/PgDn still scroll; click, shift-click and
  Ctrl-click on page 2; tooltip on a high slot; units dying on the last page; chat
  typing is unaffected.
- **Stage 4:** Ctrl+1 on 400 units and recall; Shift+1 until the limit; a group with dead
  or loaded units; Alt-click through the recent ring; save, load, recall; load a save
  from before this feature.
- **Stage 5:** host tests for the subgroup key (siege aliases, hallucinations), the
  priority sort, which subgroup stays active after add, remove or death, Tab wrapping,
  and combining button results. In game: a
  mixed army's card and Tab; burrowed + unburrowed Hydralisks show Move and Burrow;
  sieged + unsieged tanks are one subgroup; Ctrl+click on a burrowed Hydralisk takes
  both states; 10 SCVs show no Build (as vanilla); hallucinations as their own subgroup; shift-add
  keeps the active subgroup; a replay.

## Known issues and follow-ups
- Multiplayer: host-synced limit and a check that every player runs the same `SEL_MAX`.
- Multiplayer: circles and health bars on every selected unit draw from the shared
  5000-image pool; if the pool is nearly full, a synced sprite could fail to create on
  one machine and not another. Single-player is unaffected; check before multiplayer.
- Multiplayer replays: enlarge the replay frame buffers (data 0x6552B0, ids 0x6554D8,
  lengths 0x654AA8; referenced at 0x487150/0x487155/0x48715A and 0x4871B3/0x4871E8/
  0x4871F1/0x487203) before several players can send full turns of chunks in one frame.
- Page buttons, page indicator.
- Formation offsets in group moves apply only to groups that fit in 192–256 px (OpenBW
  `calc_group_move`), so large groups move as a blob. That is vanilla behaviour at a new
  scale, not a bug.

## Evidence and tools
- Scan scripts and outputs: `D:\SC Modding\resexp-analysis\selection\`
  - `selxref.py` → `selxref.txt`: every instruction touching the selection arrays.
  - `rawscan.py` + `cmpraw.py`: raw byte search for every address in every section;
    0 misses against the disassembly in `.text`.
  - `inv.py` → `inventory.txt`: per-function table with callees of address-taking sites.
  - `attr2.py`: second function-boundary method (padding runs), used to catch
    misattributions.
  - `itercallers.py`: iterator callers and their array stores.
  - `body.py calls|dis <addr...>`: calls made by, or disassembly of, a function.
- Online sources, saved in `...\selection\sources\`: OpenBW (`obw/`), BWAPI
  (`bwapi/`), teippi (`teippi/`), screp (`screp/`), and inwenis/decompile-sc research
  notes (`dsc/`, AI-assisted secondary work, useful for cross-checking addresses).
- Lessons from the scan:
  - An address just below an array used with a scaled index is an array access at index
    −1 (0x4BF8C0's `[eax*4 + 0x597248]`), not the neighbouring variable.
  - Functions reached only through pointers or tail jumps have no call-target start;
    check attributions with a second method.
