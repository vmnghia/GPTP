# Extended selection, stage 4 (control groups): implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ctrl+0–9 groups and the 8 recent selections (Alt-click) hold up to `SEL_MAX` units; recall, add, Alt-click and double-tap centring work at that size; groups survive save/load.

**Architecture:** a synced `groupsExt[8][18][SEL_MAX]` (u16 unit tags) becomes the real storage; vanilla's `u32[8][18][12]` at 0x57FE60 (inside CGame, saved whole) is the mirror of each group's first 12. The synced group rules written in stage 1 switch to it; the local recall (GPTP `selectUnitGroup`), Alt-click (0x496D30) and the double-tap centring (0x4967E0) are replaced to read it; the save chunk becomes version 2 with the groups.

**Tech stack:** C++ (MSVC, Win32), GPTP hook tools, MSVC inline asm.

**Spec:** `docs/superpowers/specs/2026-09-30-extended-selection-design.md` ("Stage 4").

## Global constraints
- The limit lives only in `selext::SEL_MAX`.
- `groupsExt` is synced: written only from command receive code (`sel_synced.cpp`) and the save/load chunk.
- Hooks one per line in `initialize.cpp`; CRLF files edited with the Edit tool; `tests/verify.ps1` (host test, plugin build, naked-wrapper check) after every task.

## Verified facts (2026-10-02)
- Group readers/writers left in vanilla: 0x4967E0 (centre on group; local; `cl` = group; averages the local human's group units' sprite positions and calls 0x49C440 with `eax` = x − 320, `ecx` = y − 200, each `& ~7`), 0x496D30 (Alt-click; `stdcall(tag)`, `ret 4`, returns BOOL; scans the local player's ring groups 17..10, 12 slots each, for the tag, keeps the one with the newest stamp (strictly greater, so ties keep the higher group), then recalls it through 0x496B40 = GPTP `selectUnitGroup`), 0x496B40 (GPTP `selectUnitGroup`, enabled; reads 12 slots; sends through 0x4C07B0), 0x4EEC30 (new game: zeroes groups and stamps), 0x4965A0 (zeroes them too; no callers).
- 0x496D30's callers: larva select, the drag box, the click, 0x46FA00, game-start reselect, the old wireframe click — all reach it by address, so hooking its entry covers them. 0x4C07B0's only exe caller is inside 0x496B40.
- On a load, CGame (with the vanilla groups) is read by 0x4CF5F0 before 0x4EED10 runs; 0x4EED10's entry clear must not wipe the vanilla groups; `groupsExt` is then filled by the save chunk (version 2) or from the vanilla 12 (older saves).

## Review focus
1. **Shift+number past the limit**: stops at `SEL_MAX`, never spills (vanilla's 13th unit went into the next group). Covered by `groupAssign`'s cap and step 4.3.
2. **Recall with dead or loaded units**: they are dropped from the group and the rest recalled. Covered by `groupRecall`'s compaction and step 4.4.
3. **Loading a version 1 save (stage 1–3 builds) or an older one**: groups come back with their first 12. Covered by the loader's fallback and step 4.8.
4. **Alt-click a unit in no recent group**: plain select, as vanilla. Covered by `newestRingGroupWith` returning -1 and step 4.6.
5. **Double-tap centring at 1280+ wide**: centres on the group, not offset. Covered by the view-size half and step 4.5.

---

### Task 1: storage and the ring search (pure part host-tested)
**Files:** `GPTP/SCBW/selection_ext.h`, `selection_ext.cpp`, `selection_ext_core.cpp`, `hooks/selection_ext/sel_selftest.cpp`.

**Produces:** `GROUP_COUNT` (18), `FIRST_RING_GROUP` (10), `extern u16 groupsExt[PLAYERS][GROUP_COUNT][SEL_MAX]`, `void mirrorGroup(u32 player, u32 group)`, `int newestRingGroupWith(const u16 (*ring)[SEL_MAX], const u16* stamps, u16 tag)`.

- [ ] RED: declare (header):
```cpp
//-------- Control groups (stage 4) --------//

const u32 GROUP_COUNT = 18;			//Ctrl+0-9, then the 8 recent selections
const u32 FIRST_RING_GROUP = 10;
//Synced: each player's control groups, as unit tags, compact.
extern u16 groupsExt[PLAYERS][GROUP_COUNT][SEL_MAX];
//Copies a group's first 12 into vanilla's u32[8][18][12] at 0x57FE60.
void mirrorGroup(u32 player, u32 group);
//Of the 8 recent-selection groups (ring[0..7]), the one holding tag with the
//newest stamp, ties going to the higher group, or -1 (vanilla 0x496D30).
int newestRingGroupWith(const u16 (*ring)[SEL_MAX], const u16* stamps, u16 tag);
```
a stub returning -2 in the core, and a test:
```cpp
void ringSearch() {
	static u16 ring[8][SEL_MAX];
	memset(ring, 0, sizeof(ring));
	const u16 stamps[8] = { 5, 9, 9, 1, 0, 0, 0, 0 };
	ring[0][0] = 7; ring[1][3] = 7; ring[2][0] = 7; ring[3][SEL_MAX - 1] = 8;
	ring[3][0] = 1;
	for (u32 i = 1; i < SEL_MAX; i++)
		ring[3][i] = (u16)(i + 1);	//full group: tag 8 at slot 7, SEL_MAX at the end
	CHECK(newestRingGroupWith(ring, stamps, 7) == 2);	//9 = 9: the higher group
	CHECK(newestRingGroupWith(ring, stamps, (u16)SEL_MAX) == 3);	//found past slot 12
	CHECK(newestRingGroupWith(ring, stamps, 999) == -1 || SEL_MAX >= 998);
	CHECK(newestRingGroupWith(ring, stamps, 0x7FFF) == -1);
}
```
- [ ] GREEN (core):
```cpp
int newestRingGroupWith(const u16 (*ring)[SEL_MAX], const u16* stamps, u16 tag) {
	int best = -1;
	for (int k = 7; k >= 0; k--) {
		bool found = false;
		for (u32 i = 0; i < SEL_MAX && ring[k][i] != 0 && !found; i++)
			found = ring[k][i] == tag;
		if (found && (best < 0 || stamps[k] > stamps[best]))
			best = k;
	}
	return best;
}
```
and in `selection_ext.cpp`: `u16 groupsExt[PLAYERS][GROUP_COUNT][SEL_MAX];`, `mirrorGroup` (writes `(u32*)0x0057FE60 + (player*18 + group)*12`, the first 12 tags, zero-padded), and `memset(groupsExt, 0, sizeof(groupsExt))` in `clearAll()` (the vanilla mirror is left alone: on a load it holds the saved groups). Host test PASS, verify PASS, commit.

### Task 2: synced group rules on `groupsExt`
**Files:** `hooks/selection_ext/sel_synced.cpp`.
- [ ] `GROUP_CAP` becomes `SEL_MAX`; `groupSlots` returns `u16*` = `groupsExt[player][group]`; drop `CONTROL_GROUPS`; tags compare as `u16`; `groupAssign` and `groupRecall` call `mirrorGroup(player, group)` after writing (assign's early returns go through one exit that mirrors). Verify, commit.

### Task 3: local side (recall, Alt-click, centring)
**Files:** `hooks/interface/selection.cpp` (`selectUnitGroup`), new `hooks/selection_ext/sel_groups.h/.cpp`, `sel_exe.h/.cpp` (`moveScreen` 0x49C440: `eax` = x, `ecx` = y), `sel_inject.cpp`, `selection_ext_hooks.h`, `initialize.cpp`, `GPTP.vcxproj`.
- [ ] `selectUnitGroup`: read `selext::groupsExt[*LOCAL_HUMAN_ID][group]` (u16 tags, `StoredUnit` is u16), arrays `static CUnit* temp_selection_array[selext::SEL_MAX]`, every 12-bound → `selext::SEL_MAX`, and call `selsend::cmdactHotkey(selectionGroupNumber, 1, temp_selection_array, index)` instead of the exe-ABI `CMDACT_HotkeyUnit` (its count is a byte).
- [ ] `sel_groups.cpp` (namespace `selgroups`):
```cpp
//Alt-click (0x496D30): selects the newest recent group holding the unit.
bool selectRecentGroupOf(u32 tag);
//Double-tap (0x4967E0): centres the view on Ctrl+group's units.
void centerViewOnGroup(u32 group);
```
`selectRecentGroupOf`: `p = *LOCAL_HUMAN_ID` (return false if ≥ 8); `k = newestRingGroupWith(groupsExt[p] + FIRST_RING_GROUP, &RING_STAMPS[p*8], tag)`; if `k < 0` return false; `hooks::selectUnitGroup(FIRST_RING_GROUP + k)`; return true.
`centerViewOnGroup`: return if `group >= 10` or `p >= 8`; `n` = leading tags; for each tag: `u = unitOfTag(tag)`, skip if null, hidden (sprite flag 0x20), or `!canMultiSelect(u) && n > 1`; sum sprite x/y (`u->sprite->position`); if any: `moveScreen(((sx/m) - viewWidth()/2) & ~7, ((sy/m) - viewHeight()/2) & ~7)`.
- [ ] Wrappers: 0x496D30 (`stdcall(tag)`, BOOL in `eax`, `ret 4`) and 0x4967E0 (`cl` = group, `ret`), installed by `hooks::injectControlGroupHooks()` (entry `nops` from the boundary scan); `initialize.cpp` line after `injectSelectionPanelHooks`. `selexe::selectRecentGroupOf` and `selexe::centerViewOnGroup` keep calling the exe addresses (now hooked). Verify, commit.

### Task 4: save chunk version 2
**Files:** `hooks/selection_ext/sel_save.cpp`.
- [ ] Write version 2: payload = selections `u16[PLAYERS][limit]` then groups `u16[PLAYERS][GROUP_COUNT][limit]`, `payloadBytes = PLAYERS * (1 + GROUP_COUNT) * limit * 2`.
- [ ] Read: version 1 (stage 1–3 builds) → selections from the chunk, groups from the vanilla mirror; version 2 → both (`min(limit, SEL_MAX)` per list, dead tags dropped, compacted, `mirrorGroup` each); no chunk → both from vanilla. Verify, commit.

### Task 5: in-game test round
4.1 Start: "self-test passed".
4.2 Select 200 units, Ctrl+1, click away, press 1. Expected: all 200 selected.
4.3 With Ctrl+1 = 390 units, select 30 others, Shift+1. Expected: group 1 = 400 (refused past 400, nothing spilled into group 2: press 2 shows its own units or nothing).
4.4 Kill or load 20 units of group 1, press 1. Expected: the rest selected.
4.5 Press 1 twice quickly. Expected: the view centres on group 1.
4.6 Select 150, then 5 others; Alt-click one of the 150. Expected: the 150 again. Alt-click a unit in no recent selection: just that unit.
4.7 Save with groups 1 (200) and 2 (50), load, press 1 and 2. Expected: 200 and 50.
4.8 Load a save from the stage 3 build. Expected: loads; groups keep their first 12.

### Task 6: docs
Spec status, memory, commit.
