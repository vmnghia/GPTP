# Extended selection (larger selections, pages, control groups)

Status: designed, approved section by section (2026-09-30); not built
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
| `spriteSelSlot` | `u16[sprite count]` | local | `CSprite+0x0B` |
| page state | `u16` page, page size `P` | local | — |

Group tags use the vanilla encoding: 11-bit unit index plus uniqueness in the upper bits
(verified in 0x4967E0: `and edx, 0x7ff` ... `sar eax, 0xb; cmp` against unit+0xA5).

### Iterator 0x49A850
- Replaced with a version that keeps a u16 cursor in plugin memory.
- **Every other write to the vanilla cursor byte 0x6284B6 stores 0** (32 as immediate 0,
  7 as `bl` right after `xor ebx, ebx`, all checked). So the new iterator treats "byte is
  0" as a reset and writes a nonzero byte while it is partway through. None of the ~40
  command handlers that reset it need to change.
- Like vanilla, it calls 0x49A7F0 on a dying unit (order Die, state 1) and re-reads the
  same slot, and it stops at the first null.
- The iterator is the only synced reader of the selection, so converting it gives every
  order handler, and OpenBW-style group-move logic, the whole selection.

## Stage 1: storage, iterator, writers, circles

**Writers moved to the new arrays** (verified inventory, see "Evidence"):
| Group | Address | Role |
|---|---|---|
| Core remover | 0x49A170 | remove unit from one player's selection |
| Remove from all players | 0x49A7F0 | calls 0x49A170 for players 0–7, clears sprite flags 0x06, deletes circle images 0x23B–0x244 |
| Add | 0x49AF80 | |
| Clear one / clear all | 0x49A740 / 0x49A320 | 0x49A320 also clears `activeSel` and 3 slots of `lastSent` |
| Hotkey recall and add | 0x4965D0, 0x496940 | receive side (stage 4 widens the groups) |
| Build `activeSel` | 0x49AE40, 0x499A60, 0x49B690 | |
| Circle and slot | 0x4E6180 | writes `CSprite+0x0B`, sets flag 0x08 |
| Local removal | 0x49F7A0 | memmoves the unit out of `activeSel` by `selectionIndex`; always called right after 0x49A7F0 |
| Client copy | 0x4C38B0, 0x4C3BB0 | 0x4C38B0 sorts via `CompareUnitRank` 0x49A350 |
| Last-sent | 0x4BF8C0, 0x4BF8A0 | remove (indexes `[eax*4 + 0x597248]` = `lastSent[i-1]`); clear (no direct callers) |
| Receive select / shift-add / shift-remove | 0x4C2750, 0x4C2560, 0x4BFB40 | |
| Game start | 0x4D0820, 0x4CE700, 0x4EED10 | 0x596B7C array reapplied at game start |
| Save | 0x4C2910 (`CMDRECV_SaveGame`), 0x4D02D0 | chunks via 0x4C3450 |
| Load | 0x4CFEF0 | chunks via 0x4C3280 |
| Pointer ↔ tag | 0x4CEDA0, 0x4CEE00 | |

**Fixed-size consumers of the iterator:**
- **`CMDRECV_MergeArchon.cpp` (enabled) must be fixed:** both functions fill
  `templars_stored[12]` from the iterator with no bound, which would corrupt the stack
  with more than 12 templars.
- `receive_command.cpp` caps its `selection[]` / `candidates[]` at 12 (safe, but
  smart-cast then only sees 12 casters) and `smart_cast.cpp`'s `roundSelection`: both
  move to `SEL_MAX`.
- In the exe, of the 38 functions that call the iterator, only the vanilla archon
  receivers 0x4C0CD0/0x4C0E90 store into a stack array, and GPTP replaces them.

**Circles and `selectionIndex`:**
- 0x4E6180 runs for every selected unit, so every one gets flag 0x08 and a circle.
- `spriteSelSlot[sprite]` holds the real slot. `CSprite+0x0B` gets `min(slot, 255)`.
- Every reader of `CSprite+0x0B` as a selection slot is replaced by a lookup in
  `activeSelExt`. Candidates from the scan: 0x46FD77 (click, GPTP), 0x49F7B3 (local
  removal), 0x4D6016 (health bars), 0x4E614B, 0x49F00B, 0x49F8B6, 0x499299, 0x49938A,
  0x4D5744, 0x4D5940. Offset +0x0B is shared by other structs, so **the plan's first
  task confirms which are sprite reads**, and also classifies the 19 flag-0x08 tests.

**Save chunk, version 1:** appended after the vanilla chunks in both save paths and read
in 0x4CFEF0. Header `{magic, version, saved limit}`, then `playersSelExt` as tags. The
vanilla 0x180-byte selection chunk stays, holding the mirror.
- Load uses `min(saved limit, SEL_MAX)` per selection.
- A file without the chunk (an old save) keeps the vanilla first 12. `[VERIFY]` how
  0x4C3280 reports a missing chunk before relying on this.

**What stage 1 alone can show:** nothing above 12 can be selected until stage 2, so the
test is that everything still works at 12 or fewer, including save/load, deaths, loads
into transports, morphs, mind control and archon merges.

## Stage 2: select commands and client-side list builders

**Verified in the exe:**
- Executor 0x4865D0. Its loop head 0x4865F0 dispatches on `id - 5` through a replay-mode
  table (0x486DAC, when `IS_IN_REPLAY` 0x6D0F14) and a normal one (0x486E08). Unknown
  ids go to 0x486DA3, the same exit as a malformed command.
- Five places compute a command's length from table 0x5005F8 and special-case only
  0x09–0x0B as `count*2 + 2`: 0x48661C, 0x4BF9A6, 0x4CDD04, 0x4CDE3A, 0x4CE085.
- Unused ids: 0x16, 0x17, 0x3C–0x54, 0x59, 0x5B (length -1 in the table).
- `QueueGameCommand` 0x485BD0: if a command doesn't fit the turn (capacity 0x57F0D8,
  ≤ 512), it flushes the turn early via 0x485A40, unless 16 − [0x57F090] turns are
  already in transit, in which case it drops the command silently. It also drops
  everything while `gwGameMode` 0x596904 is 4 (menus).
- The replay recorder 0x4CDE70 records each executed command under 256 bytes
  (`cmp edi, 0x100` at 0x4CDE7B), starting a new block in the same frame when one fills.

**The command:** `[id][flags][u8 count][u16 unit tag × count]`.
- `flags`: mode (replace / add / remove), first, last.
- At most 126 units per chunk (255 bytes), so a chunk always fits one turn and one
  replay record.
- Id: an unused one, e.g. 0x3C. `[VERIFY]` that no other plugin in SCManifold uses it.
- **Receive:** "first" clears the player's pending buffer; each chunk appends; "last"
  commits in one go under the vanilla rules above, writes `playersSelExt`, refreshes the
  mirror, and updates the recent-selection ring **once** (stage 4 widens the ring).
- **Patches:** one detour at the executor loop head 0x4865F0, which covers normal play
  and replays, and the five length sites.
- **Sender:** selections of 12 or fewer send vanilla 0x09/0x0A/0x0B. Larger ones send
  ⌈n / 126⌉ chunks.
- **A dropped chunk** never reaches any machine, so the commit either doesn't happen or
  is replaced by the next "first". That is deterministic. The local UI can briefly
  disagree, as in vanilla when a command drops.
- **The pending buffer is not saved.** A selection half-received at save time simply
  doesn't happen after load.

**Client-side list builders moved to `SEL_MAX` lists:**
- GPTP `selection.cpp` (enabled): drag box 0x46FA40, click / double-click / Ctrl-click
  0x46FB40, shift-combine 0x46F290, sorting 0x46F0F0. It includes the shift-click
  memmove on `selectionIndex`.
- `CMDACT_Select` 0x4C0860 and `CMDACT_HotkeyUnit` 0x4C07B0 (diff against `lastSentExt`,
  choose vanilla packets or chunks).
- 0x45D040: adds the egg's twin Zergling or Scourge to the selection, called from
  0x45D910 when the egg has flag 0x08.
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
