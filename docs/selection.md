# 400-unit selection, selection pages, and smart-casting

Goal: raise the selection limit from 12 to 400 (the most units a player can have at 200
supply, all Zerglings). Control groups (Ctrl+0–9) grow to 400 too. Old save games will no
longer load, which the user accepted. Single-player comes first, multiplayer later. The
selection panel gets pages. Particular spells use SC2-style smart-casting.

Tags as in `resolution.md`: `[BUILT]` works today, `[PROPOSED]` planned, `[VERIFY]` inferred
but not confirmed. Everything below comes from a survey of StarCraft.exe 1.16.1 on
2026-09-29. Nothing is built yet.

## 1. Why selection is synced game state

BW multiplayer is lockstep. Every computer runs the whole simulation, and players only
exchange commands. Selecting units is a command: packet 0x09 select, 0x0A shift-add,
0x0B shift-remove, `{u8 id, u8 count, u16 unit refs[count]}`. Every machine applies it to
`playersSelections` at the same frame.

Order commands carry no unit list. They mean "player X's current selection does Y". So
the selection must be identical everywhere. Single-player uses the same command queue,
and replays record the commands.

## 2. The hard limits for 400

| Limit | Where | Consequence |
|---|---|---|
| Select packet count is a u8 | packets 0x09/0x0A/0x0B | At most 255 units per packet |
| Turn buffer is 512 bytes | 0x654880; fill level 0x654AA0; limit 0x57F0D8 | QueueGameCommand (0x485BD0) never checks one command against the buffer, so a single 802-byte command overflows it |
| Replay recorder | 0x4CDE70 skips commands of 0x100 bytes or more (0x4CDE7B) | Big commands silently vanish from replays |
| u8 indexes | `clientSelectionCount` 0x59723D, `selectionIndexStart` 0x6284B6, `CSprite+0x0B selectionIndex` | Can't count to 400 |
| No room after any array | Every 12-slot array is followed by a live variable (e.g. 0x597238 holds the console image pointer) | Each array must move, and every reference must be repointed |

Plan for packets: a big selection is one 0x09 packet plus 0x0A continuation packets, each
of at most 125 units (≤ 252 bytes). A full 400-unit selection is 4 packets, about
806 bytes, and forces an early flush into a second turn. `[VERIFY]` that this is safe in
single-player.

## 3. Data sized for 12

| Address | Size | Owner |
|---|---|---|
| 0x597208 client selection | CUnit*[12] | Local UI |
| 0x59723D count | u8 | Local |
| 0x59724C last-sent selection | CUnit*[12] | Local; CMDACT_Select diffs against it |
| 0x596B7C | CUnit*[12] | Local; reapplied at game start (0x4D0820) |
| 0x6284B8 active player's selection | CUnit*[12] | Local, immediate; owns the selection circles |
| 0x6284E8 playersSelections | CUnit*[8][12] | **Synced; saved** (0x4CEE00/0x4CEDA0 convert it) |
| 0x57FE60 control groups | u32[8][18][12] | **Synced; inside CGame (96000 bytes, saved whole)**. Groups 0–9 are Ctrl+0–9; 10–17 are 8 automatic "recent selections" used by Alt-click |
| 0x63FE40 | u16[8][8] | Timestamps of the recent-selection groups |
| Stack [12] arrays | | 0x45D040, 0x49F7A0, 0x4C3B40, 0x4D0820, 0x46FA40/0x46FB40, 0x496B40, 0x423930, 0x4C0E90/0x4C0CD0 (archon merges), 0x458220 |

Size at 400: about 1.6 KB per local array, 12.8 KB for playersSelections, 230 KB for
control groups.

## 4. Code that assumes 12

About 316 references in about 90 functions.

- **Selection iterator.** `getActivePlayerNextSelection` 0x49A850 (`cmp bl, 0xC`) has
  72 call sites, including every order handler. Fixing it fixes all of them.
- **Sending selections.** CMDACT_Select 0x4C0860 builds 0x09/0x0A/0x0B in fixed stack
  buffers. Also CMDACT_HotkeyUnit 0x4C07B0.
- **Local selection.** Click 0x46FB40, drag box 0x46FA40, sorting 0x46F0F0, shift-combine
  0x46F290. GPTP `hooks/interface/selection.cpp` has these and is enabled. Also Alt-click
  0x496D30 and group recall 0x496B40.
- **Receiving.** Select 0x4C2750, shift-select 0x4C2560 (GPTP `CMDRECV_Selection.cpp`,
  disabled), shift-deselect 0x4BFB40 (no GPTP version), hotkey 0x4C2870 → 0x4965D0 /
  0x496940.
  - Helpers: 0x49A170 remove, 0x49A740 clear, 0x49AF80 add, 0x49AE40 create selections
    (`movzx eax, bl` index).
  - GPTP bugs if these are re-enabled: 0x4965D0's duplicate check steps `loopCounter*4`,
    and CMDRECV_Hotkey accepts slot 18.
- **Removing units.** 0x49F7A0, 0x4C3B40 and 0x4BF8C0 handle death, morph and load.
- **Selection circles.** 0x4E6180, 0x4E6290, 0x499A60, 0x49B690, and the health bar
  0x4D6010.
- **Buttons.** About 25 button conditions in 0x4284B0–0x429740 (`btns_cond.cpp`,
  disabled), the button set 0x458BC0/0x458D50/0x458DE0, and actions 0x4234D0, 0x423540,
  0x423660.
- **Save/load.** playersSelections is its own 0x180-byte chunk. Control groups live inside
  CGame, so larger ones need an extra save chunk.

## 5. The selection panel and pages

- `rez\statdata.bin`: the 12 wireframes are controls 33–44, 33×34, column-major (x pitch
  36, y pitch 37, 2 rows). Control 44 is the last entry in the file, so new wireframes 45+
  appended at the end continue in more columns.
- **Interact table.** Registered by 0x4584C0 from 0x504AF0 (44 entries), through
  0x418100, which does no bounds check. The wireframe handler is 0x4583E0. Hook 0x4584C0
  with a larger plugin table, as `statbtn_BIN_CustomCtrlID` does for the command card.
- **Fill.** 0x425960 `UnitStatAct_Selection` (GPTP `unit_stat_selection.cpp`) packs the
  client selection into controls 33..44. It needs a page start and the new last id. The
  refresh condition 0x424660 and snapshot 0x424540 compare 12 slots (arrays 0x6CA94C and
  0x6CAD7C).
- **Click.** 0x458220 (plain / Shift deselect / Ctrl select type) walks exactly 12
  controls into a 12-entry stack array. Replace it whole.
- **Tooltips.** 0x457CE0 covers ids 0x21–0x2C (`cmp cx, 0x2C`, imm8 at 0x457D7B).
- **Refresh.** Set [0x68C1F8] = 1.
- **Per page:** about 22 columns × 2 rows = 44 at 1280 wide, about 30 at 1024, about 82 at
  1920. Page size is local UI, so it may depend on resolution. The limit of 400 may not.
- **Hotkeys.** In game, the KEYDOWN input proc [0x5968A0] = 0x484350 is a bare `ret`.
  Patching it reaches keys that no dialog or accelerator used. Free candidates `[VERIFY]`:
  F5–F9, F11, F12, PgUp/PgDn, `[`, `]`, `` ` ``.
- **Page buttons, later.** Type 2 buttons with flags 0x18, with a plugin interact handler
  (`__fastcall(BinDlg*, dlgEvent*)`, activate = event 14 / dwUser 2, otherwise chain to
  the default at `((fn*)0x5014AC)[type]`). They must sit on opaque console art.

## 6. Smart-casting

**The requirement.** For particular spells, one selected caster casts per command, and
successive casts cycle through the casters. The choice must be made the same way on
every machine, so it may use only synced state, with no local randomness.

**How BW sends a spell.** Packet 0x15 (11 bytes: x, y, target, unitType, order, queued)
has no caster field.
- The receive handler 0x4C2320 calls 0x49AB00, which is GPTP `hooks::receive_command` in
  `receive_command.cpp`, enabled.
- It gives the order to every selected unit that passes `canUseTech` 0x46DD80. **There is
  no energy check there.**
- Energy is checked on the send side (0x46F5B0: send only if some selected caster has
  enough) and when the spell runs (`orders_Spell`).
- Every spell order has `CanBeQueued = 0`, so a shift-cast replaces the current order.

**Where to pick the caster.** Pick on the receive side, in `receive_command`, before its
selection loop. Every machine runs it with the same data, and replays reproduce it. No
new packet is needed, and it works with any selection size.

**KYSXD's version.** Wiki "[Source] Smart Casting Interface". It is switched off in his
own source and has no rotation, since it undoes the other casters' orders one frame
later. Don't reuse it.

**Built 2026-09-30** with option 2, and archon merges added (one pair per command, the closest
pair). Details are in `docs/superpowers/specs/2026-09-30-smart-casting-design.md`.

**Cycling options.**
1. **Sort when the selection is made** (the user's first idea). Order the units by energy
   on each select command, then walk that order with a cursor per player. The order stays
   fixed until the next select.
2. **Stamps** (recommended by the survey). Each select starts a new round. Each cast goes
   to the eligible unit that hasn't cast this round, with the most energy at cast time.
   Each unit casts once per round, in energy order, and a new select starts over. It
   needs no sorted list, handles deaths and selection changes, and doesn't depend on the
   selection size.

**Rules for both options.**
- Break ties by unit index.
- Count energy already committed to a pending spell as spent, so a Ghost still walking to
  its Lockdown isn't picked again.
- If nobody qualifies, give the order to the single best unit, so vanilla's
  "Not enough energy" appears.

**Spells.** A plugin table of orders.
- Candidates: Yamato 0x71, Lockdown 0x73, Parasite 0x78, Broodlings 0x79, Dark Swarm 0x77,
  EMP 0x7A, Defensive Matrix 0x8D, Psi Storm 0x8E, Irradiate 0x8F, Plague 0x90,
  Ensnare 0x92, Stasis 0x93, Hallucination 0x94, Restoration 0xB4,
  Disruption Web 0xB5, Mind Control 0xB6, Feedback 0xB8, Optical Flare 0xB9,
  Maelstrom 0xBA, Recall 0x89/0x8A `[VERIFY]`.
- Never: cloak, burrow, siege (their own packets, and toggles), archon merges.
