# Extended selection (larger selections, pages, control groups)

Status: designed, approved section by section (2026-09-30); stage 1+2 plan in
`docs/superpowers/plans/2026-09-30-extended-selection-stage-1-2.md`
Branch: `feature/resolution`
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
- **Panel.** The wireframes continue into more columns of the wide StatData box. PgUp
  and PgDn change the page, stopping at the first and last page. A new selection starts
  on page 0. When units die, the page stays, moved back to the last page if needed.
  - Click a wireframe to select that unit. Shift-click removes it. Ctrl-click selects
    every unit of that type in the **whole** selection, not just the page.
- **Buttons.** The command card considers every selected unit, so a Ghost at slot 13 of
  a Marine selection still shows Lockdown.
- **Control groups.** Ctrl+number, Shift+number, number and Alt-click (the recent
  selections) work at the full limit.
- **Saves.** Selections and groups survive save and load. A save from before this
  feature loads with the first 12 units of each selection and group.
- **Not in this feature:** page buttons, a page indicator, grouping the panel by unit
  type.

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
- `flags`: bits 0–1 mode (0 replace, 1 add, 2 remove), bit 2 first, bit 3 last.
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
- **A dropped chunk** never reaches any machine, so the commit either doesn't happen or
  is replaced by the next "first". That is deterministic. The local UI can briefly
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
- **Hotkeys:** replace 0x484350 with a PgUp/PgDn handler. `[VERIFY]` first that chat
  input and dialogs never reach it.
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
  rules in "Decisions".
- **Recent ring (groups 10–17):** a completed select of more than 1 unit (vanilla 0x09 /
  0x0A or one chunk commit) is copied into the oldest ring slot once.
- **Local side:** recall 0x496B40 (GPTP `selectUnitGroup`, enabled) and Alt-click
  0x496D30 read `groupsExt`. Double-tap to centre stays. 0x4967E0 (group unit count, 12
  callers) is checked per caller; callers that use the count move over.
- **Save chunk, version 2:** adds `groupsExt` after the selections.

## Stage 5: remaining readers

- **Button conditions and actions:** about 40 functions (0x4234D0–0x429470, 0x458BC0,
  0x46F5B0) loop `for (p = clientSel; p < clientSel + 12; p++)` (the 44
  `cmp reg, 0x597238` hits). For each loop, patch the base and end immediates as a pair
  to `clientSelExt` and `clientSelExt + SEL_MAX`. A site where both halves can't be
  found gets a C++ rewrite. 0x46F5B0 is the send-side "does any selected caster have the
  energy" check.
- **The u8 count 0x59723D (23 readers):** the mirror holds min(n, 12), so `== 0`,
  `== 1` and `> 1` behave the same. Every comparison is listed; any that tests against 12
  or more is converted.
- **Classified in the plan and converted where they differ above 12:** 0x455A00,
  0x4563A0, 0x4564E0, 0x458120, 0x458DE0, 0x464360, 0x492CC0, 0x49FED0, 0x4E5640.

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
- **Stage 3:** page with PgUp/PgDn and stop at the ends; click, shift-click and
  Ctrl-click on page 2; tooltip on a high slot; units dying on the last page; chat
  typing is unaffected.
- **Stage 4:** Ctrl+1 on 400 units and recall; Shift+1 until the limit; a group with dead
  or loaded units; Alt-click through the recent ring; save, load, recall; load a save
  from before this feature.
- **Stage 5:** 12 Marines + a Ghost at slot 13 shows Lockdown and the Ghost casts it; a
  mixed selection where only unit 20 can burrow shows Burrow; buttons unchanged at 12 or
  fewer.

## Known issues and follow-ups
- Multiplayer: host-synced limit and a check that every player runs the same `SEL_MAX`.
- Multiplayer replays: enlarge the replay frame buffers (data 0x6552B0, ids 0x6554D8,
  lengths 0x654AA8; referenced at 0x487150/0x487155/0x48715A and 0x4871B3/0x4871E8/
  0x4871F1/0x487203) before several players can send full turns of chunks in one frame.
- 0x4967E0 (centre the view on a group, local) hardcodes 320/200, a 640x400 view: fix
  with the stage 4 group work.
- Page buttons, page indicator, group-by-type panel.
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
