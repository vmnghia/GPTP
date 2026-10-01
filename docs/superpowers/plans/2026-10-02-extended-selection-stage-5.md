# Extended selection, stage 5 (subgroups and the command card): implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** the selection splits into subgroups by unit type; the command card and portrait follow the active subgroup, whose buttons show if any member can use them; Tab/Shift+Tab cycle subgroups; Ctrl+click and double-click take every state of a type; Build works with several workers selected.

**Architecture:** the console copy `clientSel` (local) is sorted by subgroup key when it is built (0x4C38B0, `sellocal::clientCopy`), and the active subgroup's key is kept across selection changes by a pure rule. The card stays vanilla's own: `updateButtonSet` no longer picks the merged group sets, so the card is the portrait unit's set, and the portrait is the active subgroup's leader. Button conditions are run once per member with the 12-slot client mirror holding only that member; send-side checks (button actions, the target-order check 0x46F5B0) run with the mirror holding the best 12 candidates. Build receive (0x4C23C0) picks the nearest able worker from the synced selection.

**Tech stack:** C++ (MSVC, Win32), GPTP hook tools, MSVC inline asm.

**Spec:** `docs/superpowers/specs/2026-09-30-extended-selection-design.md` ("Stage 5: subgroups and the command card").

## Global constraints
- The limit lives only in `selext::SEL_MAX`; no name contains a number for it.
- Synced state (selections, groups, the build pick) depends only on command data and game state. The subgroup key, priority table, active subgroup, sorted console list and mirror views are local and never feed synced state.
- Hooks one per line in `initialize.cpp`; CRLF files edited with the Edit tool (never `sed -i`); `tests/verify.ps1` (host test, plugin build, naked-wrapper check) after every task.
- New naked stubs go in `hooks/selection_ext/sel_inject.cpp` (the naked-wrapper check scans its object) and use only static variables, PUSHAD/POPAD and registers.

## Verified facts (2026-10-02)
- **Card flow.** 0x4C38B0 (`sellocal::clientCopy`) tail-jumps to 0x458DE0 (GPTP `updateButtonSet`, enabled), which sets the current set 0x68C1C4 to Blank and, when more than one unit is selected outside replays, calls `updateButtonSetEx` (0x458BC0), which picks GroupMixed/Peons/Cloaker/Burrower. With 0x68C1C4 Blank, 0x4599A0 uses `(*activePortraitUnit)->currentButtonSet`. GPTP `updateButtonSet_Sub4591D0` (0x4591D0, enabled) evaluates every button through one call, `req_check(reqFunc, reqVar, player, *activePortraitUnit)`.
- **Portrait.** `clientCopy` sets `*activePortraitUnit` (0x597248) to the client unit that `selexe::outranks` (0x49A350: prefers units not disabled, locked down, in stasis or maelstromed) picks.
- **Conditions** read the 12-slot client mirror 0x597208 and the u8 count 0x59723D. Every count reader in the exe compares with 0 or 1 (0x4283F3, 0x428965–0x428E65, 0x42965E, 0x4296CE, 0x45658C, 0x45816A, 0x458CA9, 0x458DE7, 0x464638, 0x492D5E, 0x49FF96 `cmp [count], bl; jbe`). The Build-menu conditions (SCV 0x428990/0x428A10, Probe 0x428AD0/0x428B80, Drone 0x428C30/0x428CB0, and 0x428E60 for 87 build/train buttons) need count == 1, which is why vanilla hides Build with several workers. Results: Enabled 1, Disabled −1, Invisible 0.
- **Button click** 0x4598D0: at 0x45990F `mov esi, [esi+0x26]` (the BUTTON), `mov dl, [0x596A28]` (Shift), `mov cx, [esi+0xE]` (actVar), `call [esi+8]` (actFunc), then `pop edi; mov eax, 1; pop esi; ret` (0x45990F–0x459926, 24 bytes). Keyboard hotkeys reach the same handler.
- **Target-order send check** 0x46F5B0: stdcall, 4 stack arguments, `ret 0x10`, no register inputs; loops the 12-slot client mirror for a unit able to take the order (energy, not disabled). Callers: 0x4A5631, 0x4BD54B, 0x4BD564 (5-byte `call`s).
- **Build receive** 0x4C23C0 (ESI = packet `[0x0C][order][u16 x tile][u16 y tile][u16 type]`, `ret`; only caller 0x486801): refuses a second selected unit, checks the tiles against 0x57F1D4/0x57F1D6, calls 0x48DBD0 (ECX = unit, DL = order, AX = type; EAX = allowed), then 0x48E190 (CL = order, AX = type, push the dword at packet+2; `ret 4`). 0x48E190 calls 0x48E010 (normal) or 0x48E0A0 (addon), and each takes its builder from the iterator again: `call 0x49A850` at 0x48E01E and 0x48E0B1 (5 bytes each), right after the cursor reset. 0x48E010/0x48E0A0 have no other callers.
- **Ctrl+click / double-click** both go through GPTP `SortAllUnits_Helper` (0x46F0F0, enabled, `hooks/interface/selection.cpp` ~line 145): same owner, same id, same burrow state, same detection state unless burrowed, same hallucination state. The panel's Ctrl-click (`selpanel::click`) compares `id`.
- **Keys.** `selpanel::keyDown` (0x484350) receives the in-game KEYDOWN event, virtual key at event+8; Shift held 0x596A28, Ctrl 0x596A29; `isChatOpen()` in `sel_panel.cpp`. Tab is not seen handled in the exe's key code, but this is checked in game (5.1).
- **Enabled GPTP 12-slot loops**: `game_hooks.cpp:376` (rally points of selected factories: buildings, single selection), `select_larva.cpp` (at most 12 larvae of one hatchery). Neither needs converting. The rest (`btns_cond.cpp`, `right_click_CMDACT.cpp`, `stim_packs.cpp`, `psi_field_util.cpp`, cloak tech, status display) are `//OFF`.
- units.dat: `units_dat::BaseProperty[id] & UnitProperty::Hero` (0x40), `units_dat::BuildScore[id]`; 228 unit types. `UnitStatus::IsHallucination`.

## Scope ruling
The panel highlight of the active subgroup (spec: "the look is chosen from rendered mock-ups before it is built") is a follow-up plan, made after the user picks a look. This plan makes the panel sorted and contiguous, so the active subgroup is visible as a block, and the portrait shows its leader.

## Review focus
1. **Shift-add, shift-remove and deaths keep the active subgroup; a fresh selection resets it.** Pinned by `keepsActive` and `activeKeyAfter` tests (Task 1) and 5.6.
2. **A condition that reads the count or the mirror must see one member, and the mirror must be restored after**, or later exe readers see a one-unit selection. Covered by restoring with `mirrorClient()` after each loop (Task 3) and 5.3/5.4.
3. **Build with several workers in a lockstep game**: the pick reads only synced state (selection order, positions, 0x48DBD0). Covered by `nearestIndex` tests (Task 1) and 5.7.
4. **Tab while chat is open types nothing and changes nothing; Tab with one subgroup does nothing.** Covered by `keyAfterTab` tests and 5.2.
5. **Replays keep the vanilla card.** Covered by the replay guard (Task 3) and 5.10.

---

### Task 1: subgroup logic (pure, host-tested)
**Files:** `GPTP/SCBW/selection_ext.h`, `GPTP/SCBW/selection_ext_core.cpp`, `GPTP/hooks/selection_ext/sel_selftest.cpp`.

**Produces** (namespace `selext`):
```cpp
//-------- Subgroups (stage 5) --------//

const u32 UNIT_TYPES = 228;
//The type a unit counts as in a subgroup: siege mode counts as tank mode
//(Siege Tank 30 -> 5, Edmund Duke 25 -> 23).
u16 subgroupType(u16 unitId);
//Default priority: heroes above everything, then higher build score.
u16 defaultPriority(bool hero, u16 buildScore);
//Sort key: ascending is panel order (higher priority, then lower type, real
//units before hallucinations). type must be below 512.
u32 subgroupKey(u16 priority, u16 type, bool hallucination);
//Sorts units by keys (parallel arrays), keeping the order of equal keys.
void sortBySubgroup(CUnit** units, u32* keys, u32 n);
//Whether a selection change keeps the active subgroup: the new selection
//holds every old unit (an add) or the old one holds every new unit (a
//removal, a death). Anything else is a fresh selection.
bool keepsActive(CUnit* const* before, u32 nb, CUnit* const* after, u32 na);
//The active key after a change. keys: the new selection's keys, sorted, n > 0.
//keep and oldKey present: oldKey. keep and absent: the first key past oldKey,
//else the last. Not keep: the first.
u32 activeKeyAfter(const u32* keys, u32 n, u32 oldKey, bool keep);
//The next (back: previous) distinct key after active, wrapping. n > 0.
u32 keyAfterTab(const u32* keys, u32 n, u32 active, bool back);
//Of two button condition results, the better: Enabled 1 > Disabled -1 > Invisible 0.
s32 betterButtonState(s32 a, s32 b);
//Index of the able entry nearest (x, y) by squared distance, ties to the
//lowest index, or -1.
int nearestIndex(const s32* xs, const s32* ys, const bool* able, u32 n, s32 x, s32 y);
```

- [ ] **RED.** Add the declarations above (after "Control groups"), stubs in the core that return wrong values (`subgroupType` returns `unitId + 1`, `defaultPriority` 0, `subgroupKey` 0, `sortBySubgroup` does nothing, `keepsActive` false, `activeKeyAfter` 0xFFFFFFFF, `keyAfterTab` 0xFFFFFFFF, `betterButtonState` returns `a`, `nearestIndex` -2), and these tests in `sel_selftest.cpp`, called from `selfTest()`:
```cpp
void subgroupKeys() {
	CHECK(subgroupType(30) == 5 && subgroupType(5) == 5);
	CHECK(subgroupType(25) == 23 && subgroupType(23) == 23);
	CHECK(subgroupType(37) == 37);
	CHECK(defaultPriority(true, 0) > defaultPriority(false, 0xFFFF));	//heroes first
	CHECK(defaultPriority(false, 1200) > defaultPriority(false, 50));
	const u16 bc = defaultPriority(false, 1200), marine = defaultPriority(false, 50);
	CHECK(subgroupKey(bc, 12, false) < subgroupKey(marine, 0, false));	//priority first
	CHECK(subgroupKey(marine, 0, false) < subgroupKey(marine, 32, false));	//then lower type
	CHECK(subgroupKey(marine, 0, false) < subgroupKey(marine, 0, true));	//real before fake
	CHECK(subgroupKey(marine, 0, true) < subgroupKey(marine, 1, false));	//fakes right after
}

void subgroupSort() {
	CUnit* units[5] = { fake(1), fake(2), fake(3), fake(4), fake(5) };
	u32 keys[5] = { 7, 3, 7, 3, 1 };
	sortBySubgroup(units, keys, 5);
	CHECK(keys[0] == 1 && keys[1] == 3 && keys[2] == 3 && keys[3] == 7 && keys[4] == 7);
	CHECK(units[0] == fake(5) && units[1] == fake(2) && units[2] == fake(4));	//stable
	CHECK(units[3] == fake(1) && units[4] == fake(3));
}

void subgroupActive() {
	CUnit* before[3] = { fake(1), fake(2), fake(3) };
	CUnit* added[4] = { fake(3), fake(1), fake(9), fake(2) };
	CUnit* removed[2] = { fake(3), fake(1) };
	CUnit* fresh[2] = { fake(1), fake(9) };
	CHECK(keepsActive(before, 3, added, 4));
	CHECK(keepsActive(before, 3, removed, 2));
	CHECK(keepsActive(before, 3, before, 3));
	CHECK(!keepsActive(before, 3, fresh, 2));
	const u32 keys[5] = { 2, 2, 5, 9, 9 };
	CHECK(activeKeyAfter(keys, 5, 5, true) == 5);
	CHECK(activeKeyAfter(keys, 5, 5, false) == 2);
	CHECK(activeKeyAfter(keys, 5, 4, true) == 5);	//gone: the next one down
	CHECK(activeKeyAfter(keys, 5, 10, true) == 9);	//gone and it was the lowest
	CHECK(keyAfterTab(keys, 5, 2, false) == 5);
	CHECK(keyAfterTab(keys, 5, 9, false) == 2);	//wraps
	CHECK(keyAfterTab(keys, 5, 2, true) == 9);
	CHECK(keyAfterTab(keys, 5, 5, true) == 2);
	const u32 one[2] = { 4, 4 };
	CHECK(keyAfterTab(one, 2, 4, false) == 4 && keyAfterTab(one, 2, 4, true) == 4);
}

void buttonStates() {
	CHECK(betterButtonState(0, -1) == -1 && betterButtonState(-1, 0) == -1);
	CHECK(betterButtonState(-1, 1) == 1 && betterButtonState(1, 0) == 1);
	CHECK(betterButtonState(0, 0) == 0);
}

void nearestWorker() {
	const s32 xs[4] = { 100, 10, 50, 50 };
	const s32 ys[4] = { 100, 10, 50, 50 };
	const bool able[4] = { true, false, true, true };
	CHECK(nearestIndex(xs, ys, able, 4, 0, 0) == 2);	//1 is nearer but unable; 2 and 3 tie
	CHECK(nearestIndex(xs, ys, able, 4, 200, 200) == 0);
	const bool none[4] = { false, false, false, false };
	CHECK(nearestIndex(xs, ys, none, 4, 0, 0) == -1);
	CHECK(nearestIndex(xs, ys, able, 0, 0, 0) == -1);
	const s32 far[2] = { 8191, 0 };
	const bool both[2] = { true, true };
	CHECK(nearestIndex(far, far, both, 2, 0, 8191) == 0);	//no overflow at map size
}
```
Run `tests\selection_ext_test.bat`. Expected: FAIL (first at the `subgroupType` line).
- [ ] **GREEN** in `selection_ext_core.cpp`:
```cpp
u16 subgroupType(u16 unitId) {
	if (unitId == 30)	//Siege Tank, siege mode
		return 5;
	if (unitId == 25)	//Edmund Duke, siege mode
		return 23;
	return unitId;
}

u16 defaultPriority(bool hero, u16 buildScore) {
	const u16 score = buildScore < 0x7FFF ? buildScore : 0x7FFF;
	return hero ? (u16)(0x8000 | score) : score;
}

u32 subgroupKey(u16 priority, u16 type, bool hallucination) {
	return ((u32)(0xFFFF - priority) << 10) | ((u32)(type & 0x1FF) << 1) | (hallucination ? 1 : 0);
}

void sortBySubgroup(CUnit** units, u32* keys, u32 n) {
	for (u32 i = 1; i < n; i++) {
		CUnit* const unit = units[i];
		const u32 key = keys[i];
		u32 j = i;
		for (; j > 0 && keys[j - 1] > key; j--) {
			units[j] = units[j - 1];
			keys[j] = keys[j - 1];
		}
		units[j] = unit;
		keys[j] = key;
	}
}

namespace {

bool holdsAll(CUnit* const* big, u32 nb, CUnit* const* small, u32 ns) {
	for (u32 i = 0; i < ns; i++) {
		bool found = false;
		for (u32 j = 0; j < nb && !found; j++)
			found = big[j] == small[i];
		if (!found)
			return false;
	}
	return true;
}

} //unnamed namespace

bool keepsActive(CUnit* const* before, u32 nb, CUnit* const* after, u32 na) {
	return holdsAll(after, na, before, nb) || holdsAll(before, nb, after, na);
}

u32 activeKeyAfter(const u32* keys, u32 n, u32 oldKey, bool keep) {
	if (!keep)
		return keys[0];
	for (u32 i = 0; i < n; i++)
		if (keys[i] >= oldKey)
			return keys[i];
	return keys[n - 1];
}

u32 keyAfterTab(const u32* keys, u32 n, u32 active, bool back) {
	if (!back) {
		for (u32 i = 0; i < n; i++)
			if (keys[i] > active)
				return keys[i];
		return keys[0];
	}
	for (u32 i = n; i > 0; i--)
		if (keys[i - 1] < active)
			return keys[i - 1];
	return keys[n - 1];
}

s32 betterButtonState(s32 a, s32 b) {
	//Enabled 1 > Disabled -1 > Invisible 0.
	const s32 rankA = a == 1 ? 2 : a == -1 ? 1 : 0;
	const s32 rankB = b == 1 ? 2 : b == -1 ? 1 : 0;
	return rankB > rankA ? b : a;
}

int nearestIndex(const s32* xs, const s32* ys, const bool* able, u32 n, s32 x, s32 y) {
	int best = -1;
	u32 bestDistance = 0;
	for (u32 i = 0; i < n; i++) {
		if (!able[i])
			continue;
		const s32 dx = xs[i] - x, dy = ys[i] - y;
		const u32 distance = (u32)(dx * dx) + (u32)(dy * dy);
		if (best < 0 || distance < bestDistance) {
			best = (int)i;
			bestDistance = distance;
		}
	}
	return best;
}
```
`activeKeyAfter` with keep and a key present returns it (the first `>=` hit equals it); absent, the first key past it. Run the host test. Expected: PASS. Run `tests\verify.ps1`. Expected: "plugin build succeeded". Commit `feat: subgroup keys, order and active-subgroup rules`.

### Task 2: the active subgroup on the console
**Files:** create `GPTP/hooks/selection_ext/sel_subgroups.h/.cpp`; modify `sel_local.cpp` (`clientCopy`), `sel_panel.cpp` (`click`, `keyDown`, page jump), `SCBW/selection_ext.h/.cpp` (`mirrorClientView`), `hooks/interface/selection.cpp` (`SortAllUnits_Helper`), `GPTP.vcxproj` (+ filters).

**Consumes:** Task 1's functions. **Produces** (namespace `selsub`, local UI only):
```cpp
//Subgroups of the console selection (stage 5). Local UI state: never synced.
#pragma once
#include <SCBW/api.h>

namespace selsub {

//A unit's subgroup key (selext::subgroupKey with its priority and type).
u32 keyOf(CUnit* unit);
//Sorts selext::clientSel (count n) by subgroup, updates the active subgroup
//from the change since the last call, and returns the leader (the active
//subgroup's unit selexe::outranks picks), or NULL for n == 0.
CUnit* sortAndPickLeader(u32 n);
//Tab / Shift+Tab. Returns false if nothing changed (one subgroup or none).
bool cycle(bool back);
//The active subgroup's units, in panel order; returns how many.
u32 activeMembers(CUnit** out);
//Index in clientSel of the active subgroup's first unit (0 if none).
u32 activeFirstIndex();
//Forgets the active subgroup (a new game).
void reset();

} //selsub
```
and in `selext` (`selection_ext.h`, under the mirrors):
```cpp
//Writes list (n entries, at most 12 used) and its count into the client
//mirror, for a check that reads it; mirrorClient() puts the real one back.
void mirrorClientView(CUnit* const* list, u32 n);
```

- [ ] `selection_ext.cpp`:
```cpp
void mirrorClientView(CUnit* const* list, u32 n) {
	for (u32 i = 0; i < VANILLA_MAX; i++)
		VANILLA_CLIENT_SEL[i] = i < n ? list[i] : NULL;
	*VANILLA_CLIENT_COUNT = (u8)(n < VANILLA_MAX ? n : VANILLA_MAX);
}
```
- [ ] `sel_subgroups.cpp`:
```cpp
#include "sel_subgroups.h"
#include "sel_exe.h"
#include <SCBW/selection_ext.h>
#include <cstring>

using namespace selext;

namespace {

u32 keys[SEL_MAX];			//of clientSel, sorted with it
CUnit* previous[SEL_MAX];	//the selection at the last sort
u32 previousCount;
u32 activeKey;

u16 priorityOf(u16 type) {
	if (type >= UNIT_TYPES)
		return 0;
	return defaultPriority((units_dat::BaseProperty[type] & UnitProperty::Hero) != 0,
	                       units_dat::BuildScore[type]);
}

} //unnamed namespace

namespace selsub {

u32 keyOf(CUnit* unit) {
	const u16 type = subgroupType(unit->id);
	return subgroupKey(priorityOf(type), type, (unit->status & UnitStatus::IsHallucination) != 0);
}

CUnit* sortAndPickLeader(u32 n) {
	if (n == 0) {
		previousCount = 0;
		return NULL;
	}
	for (u32 i = 0; i < n; i++)
		keys[i] = keyOf(clientSel[i]);
	sortBySubgroup(clientSel, keys, n);
	const bool keep = previousCount != 0 && keepsActive(previous, previousCount, clientSel, n);
	activeKey = activeKeyAfter(keys, n, activeKey, keep);
	memcpy(previous, clientSel, n * sizeof(CUnit*));
	previousCount = n;
	CUnit* leader = NULL;
	for (u32 i = 0; i < n; i++)
		if (keys[i] == activeKey && selexe::outranks(clientSel[i], leader))
			leader = clientSel[i];
	return leader;
}

bool cycle(bool back) {
	if (clientCount == 0)
		return false;
	const u32 next = keyAfterTab(keys, clientCount, activeKey, back);
	if (next == activeKey)
		return false;
	activeKey = next;
	return true;
}

u32 activeMembers(CUnit** out) {
	u32 m = 0;
	for (u32 i = 0; i < clientCount; i++)
		if (keys[i] == activeKey)
			out[m++] = clientSel[i];
	return m;
}

u32 activeFirstIndex() {
	for (u32 i = 0; i < clientCount; i++)
		if (keys[i] == activeKey)
			return i;
	return 0;
}

void reset() {
	previousCount = 0;
	activeKey = 0;
}

} //selsub
```
`cycle` changes only `activeKey`; the caller asks for a console rebuild, and `sortAndPickLeader` keeps it (same units: `keepsActive`).
- [ ] `sellocal::clientCopy`: replace the portrait loop with
```cpp
void clientCopy() {
	memcpy(clientSel, activeSel, sizeof(clientSel));
	clientCount = listCount(clientSel, SEL_MAX);
	*activePortraitUnit = selsub::sortAndPickLeader(clientCount);
	if (clientCount == 1) {
		CUnit* const only = clientSel[0];
		listClear(clientSel, SEL_MAX);
		clientSel[0] = only;
	}
	mirrorClient();
}
```
(vanilla kept the portrait unit as the one unit; with one unit it is `clientSel[0]`). Call `selsub::reset()` from `sellocal::gameStartClear()`.
- [ ] `sel_panel.cpp` `keyDown`: before the page code,
```cpp
	const u16 VK_TAB_KEY = 0x09;
	if (key == VK_TAB_KEY) {
		if (!chatOpen && selsub::cycle(*SHIFT_HELD != 0)) {
			if (pageSize != 0)
				selectionPage = selsub::activeFirstIndex() / pageSize;
			sellocal::requestRefresh();
		}
		return;
	}
```
(move `const bool chatOpen = isChatOpen();` above it). `click` with Ctrl: compare `selsub::keyOf(clientSel[i]) == selsub::keyOf(clickedUnit)` instead of `id`.
- [ ] `selection.cpp` `SortAllUnits_Helper`: replace `if(unit->id != current_unit->id)` with `if(selext::subgroupType(unit->id) != selext::subgroupType(current_unit->id))`, and replace the burrow/detection block so that only the hallucination test stays:
```cpp
									//Subgroup rule (stage 5): burrow, cloak and
									//siege state don't split a type; only
									//hallucinations stay apart.
									if(mixed_flags & UnitStatus::IsHallucination)
										bDontAddToList = true;
```
(remove the now-unused `isUnitBurrowed` call there; keep the function if other code uses it).
- [ ] Add the new files to `GPTP.vcxproj` and `.filters` (next to `sel_groups`). Run `tests\verify.ps1`. Expected: "plugin build succeeded". Commit `feat: subgroups sort the console and pick the portrait; Tab cycles them`.

### Task 3: the card follows the active subgroup
**Files:** `hooks/interface/buttonsets.cpp`, `hooks/selection_ext/sel_inject.cpp`, `selection_ext_hooks.h`, `initialize.cpp`, `sel_subgroups.h/.cpp`.

**Produces** (namespace `selsub`):
```cpp
//Puts into the client mirror the 12 best units for a send-side check: the
//active subgroup's first (most energy first), then the rest of the selection.
void viewBegin();
//Puts the real client mirror back.
void viewEnd();
```
- [ ] `buttonsets.cpp` `updateButtonSet`: replace the last `if` with a comment, so the card is the portrait unit's own set:
```cpp
		//Stage 5 (subgroups): the card is the active subgroup leader's own set
		//(the portrait unit's), not a merged group set; updateButtonSetEx is
		//no longer called.
```
- [ ] `updateButtonSet_Sub4591D0`: replace the `req_check(...)` call with `buttonState(current_button)`, defined above it in the unnamed namespace:
```cpp
	//Stage 5: with several units selected, a button takes the best result of
	//its condition over the active subgroup's members, each checked alone.
	s32 buttonState(BUTTON* button) {
		static CUnit* members[selext::SEL_MAX];
		const u32 player = (u8)*LOCAL_NATION_ID;
		if (*IS_IN_REPLAY || selext::clientCount <= 1)
			return req_check((u32)button->reqFunc, (u8)button->reqVar, player, *activePortraitUnit);
		const u32 m = selsub::activeMembers(members);
		s32 best = BUTTON_STATE::Invisible;
		for (u32 i = 0; i < m && best != BUTTON_STATE::Enabled; i++) {
			selext::mirrorClientView(&members[i], 1);
			best = selext::betterButtonState(best,
				req_check((u32)button->reqFunc, (u8)button->reqVar, player, members[i]));
		}
		selext::mirrorClient();
		return best;
	}
```
(includes: `<SCBW/selection_ext.h>`, `"../selection_ext/sel_subgroups.h"`; `req_check` must be declared before it, as now at the top of the file.)
- [ ] `sel_subgroups.cpp`:
```cpp
void viewBegin() {
	static CUnit* view[SEL_MAX];
	u32 m = activeMembers(view);
	//Most energy first: the check wants one unit able to cast.
	for (u32 i = 1; i < m; i++) {
		CUnit* const unit = view[i];
		u32 j = i;
		for (; j > 0 && view[j - 1]->energy < unit->energy; j--)
			view[j] = view[j - 1];
		view[j] = unit;
	}
	for (u32 i = 0; i < clientCount && m < VANILLA_MAX; i++)
		if (keys[i] != activeKey)
			view[m++] = clientSel[i];
	mirrorClientView(view, m);
}

void viewEnd() {
	mirrorClient();
}
```
- [ ] `sel_inject.cpp`, stage 5 section:
```cpp
//-------- Stage 5 --------//

//0x45990F (button click, 24 bytes to the handler's return): ESI = the dialog.
void __declspec(naked) buttonActionStub() {
	__asm {
		MOV ESI, [ESI+0x26]
		PUSHAD
	}
	selsub::viewBegin();
	__asm {
		POPAD
		PUSH EAX
		MOV EAX, 0x00596A28
		MOV DL, [EAX]
		POP EAX
		MOV CX, [ESI+0x0E]
		CALL DWORD PTR [ESI+8]
		PUSHAD
	}
	selsub::viewEnd();
	__asm {
		POPAD
		POP EDI
		MOV EAX, 1
		POP ESI
		RETN
	}
}

//Calls of 0x46F5B0 (target-order send check): stdcall, 4 arguments.
const u32 Func_TargetOrderCheck = 0x0046F5B0;
void __declspec(naked) targetOrderCheckStub() {
	static u32 result;
	__asm PUSHAD
	selsub::viewBegin();
	__asm {
		POPAD
		PUSH DWORD PTR [ESP+0x10]
		PUSH DWORD PTR [ESP+0x10]
		PUSH DWORD PTR [ESP+0x10]
		PUSH DWORD PTR [ESP+0x10]
		CALL Func_TargetOrderCheck
		MOV result, EAX
		PUSHAD
	}
	selsub::viewEnd();
	__asm {
		POPAD
		MOV EAX, result
		RETN 0x10
	}
}
```
(each `PUSH [ESP+0x10]` copies the next argument down: after the call into the stub the arguments are at ESP+4..+0x10, and every push moves ESP by 4). In `hooks`:
```cpp
void injectCommandCardHooks() {
	jmpPatch(buttonActionStub,		0x0045990F, 19);
	callPatch(targetOrderCheckStub,	0x004A5631, 0);
	callPatch(targetOrderCheckStub,	0x004BD54B, 0);
	callPatch(targetOrderCheckStub,	0x004BD564, 0);
}
```
Declare it in `selection_ext_hooks.h` ("Stage 5: the card's conditions over the active subgroup; send-side checks see it first."), and add `hooks::injectCommandCardHooks();` after `injectControlGroupHooks()` in `initialize.cpp`'s enabled list.
- [ ] Run `tests\verify.ps1`. Expected: "plugin build succeeded" and the naked-wrapper check passes. Commit `feat: the command card follows the active subgroup`.

### Task 4: Build with several workers
**Files:** `hooks/selection_ext/sel_synced.h/.cpp`, `sel_inject.cpp`, `sel_exe.h/.cpp`.

**Produces:** `void selsync::recvBuild(const u8* packet)` (0x4C23C0), `CUnit* selsync::chosenBuilder` (set only while 0x48E190 runs).
- [ ] `sel_exe`: wrappers `bool placeBuildingAllowed(CUnit* unit, u8 order, u16 type)` (0x48DBD0: ECX = unit, DL = order, AX = type, EAX result) and `void placeBuilding(u8 order, u16 type, u32 tiles)` (0x48E190: CL = order, AX = type, push tiles; `ret 4`), in the file's existing style (static variables, PUSHAD/POPAD).
- [ ] `sel_synced.cpp`:
```cpp
CUnit* chosenBuilder;

//Build (0x4C23C0): vanilla refuses it with more than one unit selected. Here
//the selected unit able to build it that is nearest the site builds it.
//Synced: the selection, positions and 0x48DBD0 only.
void recvBuild(const u8* packet) {
	static CUnit* units[SEL_MAX];
	static s32 xs[SEL_MAX], ys[SEL_MAX];
	static bool able[SEL_MAX];
	const u8 order = packet[1];
	const u16 tileX = *(const u16*)(packet + 2);
	const u16 tileY = *(const u16*)(packet + 4);
	const u16 type = *(const u16*)(packet + 6);
	if (tileX >= mapTileSize->width || tileY >= mapTileSize->height)
		return;
	u32 n = 0;
	*selectionIndexStart = 0;
	for (CUnit* unit = getActivePlayerNextSelection(); unit != NULL && n < SEL_MAX;
		 unit = getActivePlayerNextSelection()) {
		units[n] = unit;
		xs[n] = unit->position.x;
		ys[n] = unit->position.y;
		able[n] = selexe::placeBuildingAllowed(unit, order, type);
		n++;
	}
	const s32 siteX = tileX * 32 + (s16)units_dat::BuildingDimensions[type].x / 2;
	const s32 siteY = tileY * 32 + (s16)units_dat::BuildingDimensions[type].y / 2;
	const int k = nearestIndex(xs, ys, able, n, siteX, siteY);
	if (k < 0)
		return;
	chosenBuilder = units[k];
	selexe::placeBuilding(order, type, *(const u32*)(packet + 2));
	chosenBuilder = NULL;
}
```
(`type` comes from the packet: bound it, `if (type >= UNIT_TYPES) return;`, before indexing units.dat.)
- [ ] `sel_inject.cpp`:
```cpp
//0x4C23C0 (Build receive): ESI = the packet.
void __declspec(naked) recvBuildWrapper() {
	static const u8* packet;
	__asm {
		MOV packet, ESI
		PUSHAD
	}
	selsync::recvBuild(packet);
	__asm {
		POPAD
		RETN
	}
}

//The builder 0x48E010 / 0x48E0A0 take from the selection (their call of
//0x49A850 right after the cursor reset): the chosen one during a Build.
const u32 Func_NextSelected = 0x0049A850;
void __declspec(naked) builderStub() {
	__asm {
		MOV EAX, selsync::chosenBuilder
		TEST EAX, EAX
		JZ vanilla
		RETN
	vanilla:
		JMP Func_NextSelected
	}
}
```
and add to `injectCommandCardHooks()`:
```cpp
	jmpPatch(recvBuildWrapper,	0x004C23C0, 1);
	callPatch(builderStub,		0x0048E01E, 0);
	callPatch(builderStub,		0x0048E0B1, 0);
```
(`jmpPatch` at 0x4C23C0: `push edi` + `mov byte [0x6284B6], 0` are 8 bytes, so 3 nops after the 5-byte jump; use 3, after checking the boundary with `disat.py 4C23C0`.) If `MOV EAX, selsync::chosenBuilder` doesn't assemble for a namespaced variable, read it through a file-static pointer `static CUnit** const CHOSEN_BUILDER = &selsync::chosenBuilder;` with `MOV EAX, CHOSEN_BUILDER` / `MOV EAX, [EAX]`.
- [ ] Run `tests\verify.ps1`. Expected: "plugin build succeeded". Commit `feat: Build with several workers selected uses the nearest one`.

### Task 5: in-game test round
Build, repack, check the stamp. Then:
- 5.1 Start: "self-test passed". With chat closed, select one Marine and press Tab, then Shift+Tab. Expected: nothing else happens (no vanilla Tab action). **If Tab does anything in vanilla, stop and report it.**
- 5.2 Select 20 Marines, 5 Ghosts, 5 Firebats, 2 Medics, 3 Siege Tanks (1 sieged). Expected: the panel is grouped by type, highest priority first; the card is the first subgroup's (tanks or Ghosts, by build score); Tab steps through each subgroup, wraps at the end; Shift+Tab goes back; the portrait follows. Open chat, press Tab: nothing changes and the message gets no tab.
- 5.3 With the Ghost subgroup active, press Lockdown's hotkey and target a vehicle. Expected: one Ghost casts it (smart-cast).
- 5.4 Select 10 Hydralisks, 5 of them burrowed. Expected: one subgroup; the card shows Move, Stop, Attack, Patrol, Hold, Burrow and Lurker Aspect (if researched). Press Move on a point: the unburrowed ones move. Press Burrow: they burrow.
- 5.5 Sieged and unsieged tanks: one subgroup; Siege shows (the "on" button wins position 7). Ctrl+click a burrowed Hydralisk among burrowed and unburrowed ones: both states are selected. Double-click a sieged tank: both modes selected.
- 5.6 With a mixed army and the Firebat subgroup active (Tab), Shift+click another Marine in: the Firebats stay active. Kill the Firebats: the next subgroup down becomes active. Box-select a new group: its top subgroup is active.
- 5.7 Select 10 SCVs. Expected: the SCV card with Build Structure. Place a Supply Depot: the SCV nearest the site builds it, the others keep their orders. Same with Drones (one Drone morphs) and Probes.
- 5.8 Hallucinate 2 Zealots (High Templar) and select them with 4 real Zealots: two subgroups, the real Zealots first. Ctrl+click a real one: only the real ones.
- 5.9 12 or fewer of one type: the card and every button as vanilla.
- 5.10 Watch a replay of 5.2–5.7: the card is the replay set; nothing desyncs; the Build in 5.7 is replayed by the same SCV.

### Task 6: docs
Spec status (stage 5 built except the highlight); the "Other readers" resolution from the verified facts above (count readers compare with 0/1: unchanged; 0x46F5B0 sees the view; the enabled GPTP loops are buildings/larvae only); memory `selection-400-work.md`; commit.
