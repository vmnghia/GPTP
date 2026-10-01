# Extended selection, stage 3 (selection pages): implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** the console's selection panel shows the whole selection in pages of wireframes that fill the widened StatData box; PgUp/PgDn change page; clicking, Shift- and Ctrl-clicking a wireframe act on the whole selection; tooltips work on every wireframe.

**Architecture:** `statdata.bin` gets wireframe controls 45 onward (ids 0x2D..0x7A, 90 in all), continuing vanilla's grid. The plugin gives them vanilla's wireframe handler (a larger interact table), replaces the panel fill (0x425960), its refresh test (0x424660) and the click (0x458220), widens the tooltip id range, and turns PgUp/PgDn into page changes in the in-game KEYDOWN proc (0x484350). Page size is computed in game from the StatData dialog's width.

**Tech stack:** C++ (MSVC, Win32), GPTP hook tools, MSVC inline asm; Python 2.7 for the `.bin` generator.

**Spec:** `docs/superpowers/specs/2026-09-30-extended-selection-design.md` ("What the player sees", "Stage 3").

## Global constraints
- The selection limit lives only in `selext::SEL_MAX`; the wireframe count only in `selext::WIREFRAME_MAX` (90) and the generator's `TOTAL` (must match).
- Page state is local UI; nothing here may write synced arrays except through `selsend::cmdactSelect` (as the click already did).
- Hooks one per line in `initialize.cpp`; CRLF-stored files are edited with the Edit tool.
- Build/verify: `powershell -NoProfile -ExecutionPolicy Bypass -File tests/verify.ps1` (host test + plugin build). Touch `GPTP/hooks/main/game_hooks.cpp` before an in-game build.
- **The new `statdata.bin` and this plugin ship together**: vanilla's interact table entry for id 0x2D is string data, so new controls without the plugin's table crash.

## Verified facts (2026-10-01, decompile + own checks)
- `statdata.bin`: root 270×92; wireframes ids 33..44 are the last 12 entries (0x0F74..0x1326, 0x56 bytes each), column-major: x = 30 + 36·col, y = 8 / 45, 33×34, flags 0x400. Entry 44 (0x1326) has next = 0; the 9-byte name string follows at 0x137C.
- 0x418100 (`eax`=dialog, `edi`=table, `ecx`=bytes) gives each child `table[index-1]` with no bounds check. 0x4584C0 passes `ecx` = 0xB0 (imm32 at **0x4584C3**) and `edi` = 0x504AF0 (imm32 at **0x4584C8**). Entries 0x20..0x2B are 0x4583E0; entry 0x2C is string data.
- 0x4583E0 (wireframe handler) allocates an 8-byte user block `{CUnit* unit; u16 unitId; u16 pad}` at USER_CREATE and sets the draw proc 0x456F50, which draws from that block every frame.
- 0x425960 (`eax`=dialog; keep `esi`/`edi`/`ebx`): on first entry to the multi layout (`[0x68C1E5]` ≠ 1) hides all children and sets it to 1; snapshots; binds packed non-null client units to the wireframes in list order (show 0x4186A0 `esi`=ctrl, then `flags |= 1` + invalidate 0x41C400 `eax`=ctrl); hides the rest (0x418700 `esi`=ctrl).
- 0x424660 (no args, BOOL in `eax`): true if a non-null shown unit's hit points or id changed.
- 0x458220 (`edx`=clicked control): Shift = deselect, Ctrl = select type, plain = select one; Alt recalls the recent group (0x496D30); then 0x49AE40, 0x4C0860 and the six refresh flags. Modifiers: Shift 0x596A28, Ctrl 0x596A29, Alt 0x596A2A.
- Tooltip ids 0x21..0x2C: the upper bound is a sign-extended imm8 at **0x457D7E** (`66 83 F9 2C`); text is static.
- Keys: a fresh keydown is offered to dialogs (0x419FD0, e.g. the chat box) first, then to `[0x5968A0]` = 0x484350 (a bare `ret` with 15 bytes of `int3` after it), `ecx` = event; the virtual key is the u16 at event+8.
- The plugin's widest view is 2048 (RESOLUTION_MAX_WIDTH); StatData is at most 270 + 1408 wide: (1678 − 90) / 36 + 1 = 45 columns = 90 wireframes, highest id 0x7A ≤ 0x7F.

## Review focus
1. **A 640×480 or unwidened console** (12 wireframe controls fit): pages of 12, no stray controls drawn. Covered by `pageSizeFor` tests and test step 3.7.
2. **Units dying on the last page** until it is empty: the panel moves to the new last page instead of showing nothing. Covered by `selectionPanelChanged`'s shown-start check and step 3.5.
3. **PgUp/PgDn with one unit or no selection**: nothing happens, nothing crashes. Covered by `keyDown`'s page count and step 3.3.
4. **Typing in chat**: PgUp/PgDn go to the chat box, not the pages. Covered by the dispatch order (dialogs first) and step 3.8.
5. **Old `statdata.bin` (not repacked) with the new plugin**: pages of 12. Covered by capping the page size at the controls present; noted in step 3.1.

---

### Task 1: page arithmetic (pure, host-tested)

**Files:** Modify `GPTP/SCBW/selection_ext.h`, `GPTP/SCBW/selection_ext_core.cpp`, `GPTP/SCBW/selection_ext.cpp` (page variable, reset in `clearAll`), `GPTP/hooks/selection_ext/sel_selftest.cpp`.

**Interfaces — produces:** `WIREFRAME_FIRST_ID` (0x21), `WIREFRAME_MAX` (90), `u32 pageSizeFor(u32 dialogWidth, u32 wireframeControls)`, `u32 pageCountFor(u32 count, u32 pageSize)`, `u32 clampPage(u32 page, u32 count, u32 pageSize)`, `extern u32 selectionPage`.

- [ ] **Step 1 (RED):** add to `selection_ext.h`, and stubs returning 0 in `selection_ext_core.cpp`:
```cpp
//-------- Selection panel pages (stage 3) --------//

//Wireframe controls of statdata.bin: ids 0x21 onward, 2 rows, columns 36 px
//apart from x 30 (make_statdata_wide.py writes WIREFRAME_MAX of them).
const u32 WIREFRAME_FIRST_ID = 0x21;
const u32 WIREFRAME_MAX = 90;
//Wireframes per page: the columns that fit a StatData dialog this wide (the
//vanilla 270 fits 6), two rows, at most the controls present.
u32 pageSizeFor(u32 dialogWidth, u32 wireframeControls);
//Pages for count units (at least 1).
u32 pageCountFor(u32 count, u32 pageSize);
//page, moved back to the last page if it is past it.
u32 clampPage(u32 page, u32 count, u32 pageSize);
//Local: the page of the selection the panel shows.
extern u32 selectionPage;
```
and a test function `pages()` in `sel_selftest.cpp` (called from `selfTest`):
```cpp
void pages() {
	CHECK(pageSizeFor(270, 12) == 12);		//vanilla
	CHECK(pageSizeFor(270, WIREFRAME_MAX) == 12);
	CHECK(pageSizeFor(910, WIREFRAME_MAX) == 46);	//23 columns
	CHECK(pageSizeFor(1678, WIREFRAME_MAX) == 90);	//2048 wide
	CHECK(pageSizeFor(4000, WIREFRAME_MAX) == WIREFRAME_MAX);
	CHECK(pageSizeFor(910, 12) == 12);		//statdata.bin not repacked
	CHECK(pageSizeFor(60, 12) == 2);		//never below one column
	CHECK(pageCountFor(0, 12) == 1 && pageCountFor(12, 12) == 1);
	CHECK(pageCountFor(13, 12) == 2 && pageCountFor(400, 46) == 9);
	CHECK(clampPage(5, 13, 12) == 1 && clampPage(1, 13, 12) == 1);
	CHECK(clampPage(3, 0, 12) == 0);
}
```
Run `tests/selection_ext_test.bat`. Expected: FAIL.
- [ ] **Step 2 (GREEN):** implement in `selection_ext_core.cpp`:
```cpp
u32 pageSizeFor(u32 dialogWidth, u32 wireframeControls) {
	const u32 columns = dialogWidth > 90 ? (dialogWidth - 90) / 36 + 1 : 1;
	const u32 size = 2 * columns;
	return size < wireframeControls ? size : wireframeControls;
}

u32 pageCountFor(u32 count, u32 pageSize) {
	if (count == 0 || pageSize == 0)
		return 1;
	return (count + pageSize - 1) / pageSize;
}

u32 clampPage(u32 page, u32 count, u32 pageSize) {
	const u32 last = pageCountFor(count, pageSize) - 1;
	return page < last ? page : last;
}
```
and in `selection_ext.cpp` define `u32 selectionPage;` and add `selectionPage = 0;` to `clearAll()`. Run the host test: PASS. Run `tests/verify.ps1`: PASS + build succeeded.
- [ ] **Step 3:** commit `feat: selection panel page arithmetic`.

### Task 2: `statdata.bin` generator

**Files:** Create `D:\SC Modding\SCManifold\to-repack\make_statdata_wide.py` (next to `make_statbtn_5x3.py`; outside the repo, like it).

- [ ] **Step 1:** write the script:
```python
# Builds rez\statdata.bin with WIREFRAME_MAX wireframes for the GPTP plugin's
# selection pages (docs/superpowers/specs/2026-09-30-extended-selection-design.md,
# stage 3). Vanilla has 12 (ids 33-44, the last 12 entries); the new ones (45 on)
# continue its grid, column-major: x = 30 + 36 * column, y = 8 or 45, 33 x 34.
# They are appended after the file's last byte and linked from entry 44, so no
# existing offset moves. TOTAL must equal selext::WIREFRAME_MAX in the plugin.
# Ship it only with a plugin that has the extended selection pages: the vanilla
# exe has no handler for ids above 44 and crashes.
#
# Run with Python 2.7 from this folder: python make_statdata_wide.py
import struct

SOURCE = '../unpacked/rez/statdata.bin'
OUT = 'rez/statdata.bin'
ENTRY = 0x56
LAST_VANILLA = 0x1326      # entry of id 44, the list's tail
FIRST_ID = 33
TOTAL = 90

d = bytearray(open(SOURCE, 'rb').read())
assert struct.unpack_from('<I', d, LAST_VANILLA)[0] == 0
assert struct.unpack_from('<h', d, LAST_VANILLA + 0x20)[0] == 44
assert struct.unpack_from('<4h', d, LAST_VANILLA + 4) == (210, 45, 242, 78)
template = d[LAST_VANILLA:LAST_VANILLA + ENTRY]
previous = LAST_VANILLA
for k in range(12, TOTAL):
    column, row = divmod(k, 2)
    left, top = 30 + 36 * column, 8 + 37 * row
    entry = bytearray(template)
    struct.pack_into('<I', entry, 0, 0)
    struct.pack_into('<4h', entry, 4, left, top, left + 32, top + 33)
    struct.pack_into('<h', entry, 0x20, FIRST_ID + k)
    offset = len(d)
    d += entry
    struct.pack_into('<I', d, previous, offset)
    previous = offset
open(OUT, 'wb').write(d)
print 'ok: %d wireframes, %d bytes' % (TOTAL, len(d))
```
- [ ] **Step 2:** run it; parse the output with the entry-walk used in the decompile (every entry's id/rect) and check: 90 wireframes, ids 33..122, the last at (30 + 36·44, 45). Expected: `ok: 90 wireframes, 11705 bytes` (4997 + 78 × 0x56).

### Task 3: panel logic

**Files:** Create `GPTP/hooks/selection_ext/sel_panel.h`, `sel_panel.cpp`; modify `sel_exe.h/.cpp` (control show/hide/invalidate), `GPTP.vcxproj`.

**Interfaces — produces (namespace `selpanel`):** `void fill(BinDlg* dialog)` (0x425960), `bool changed()` (0x424660), `void click(BinDlg* control)` (0x458220), `void keyDown(const u8* event)` (0x484350), `const u32* interactTable()`, `u32 interactTableBytes()`. Consumes `selexe::showControl/hideControl/invalidateControl(BinDlg*)`, `sellocal::buildActive`, `sellocal::requestRefresh`, `selsend::cmdactSelect`.

- [ ] **Step 1:** add to `sel_exe.h` / `sel_exe.cpp`:
```cpp
void showControl(BinDlg* control);			//0x4186A0
void hideControl(BinDlg* control);			//0x418700
void invalidateControl(BinDlg* control);	//0x41C400
```
```cpp
const u32 Func_ShowControl			= 0x004186A0;
const u32 Func_HideControl			= 0x00418700;
const u32 Func_InvalidateControl	= 0x0041C400;

void showControl(BinDlg* control) {
	__asm {
		PUSHAD
		MOV ESI, control
		CALL Func_ShowControl
		POPAD
	}
}

void hideControl(BinDlg* control) {
	__asm {
		PUSHAD
		MOV ESI, control
		CALL Func_HideControl
		POPAD
	}
}

void invalidateControl(BinDlg* control) {
	__asm {
		PUSHAD
		MOV EAX, control
		CALL Func_InvalidateControl
		POPAD
	}
}
```
- [ ] **Step 2:** write `sel_panel.h`:
```cpp
//The selection panel's pages (stage 3): the StatData wireframes show one page
//of the client selection; PgUp/PgDn change it. Local UI only.
#pragma once
#include <SCBW/api.h>

namespace selpanel {

//Fills the wireframes with the current page (0x425960).
void fill(BinDlg* dialog);
//Whether a shown unit's hit points or type changed, or the page is past the
//selection's end (0x424660).
bool changed();
//A click on a wireframe: plain selects its unit, Shift removes it, Ctrl
//selects every unit of its type in the whole selection (0x458220).
void click(BinDlg* control);
//The in-game KEYDOWN proc (0x484350): PgUp/PgDn change the page.
void keyDown(const u8* event);
//StatData's interact table, indexed by control id - 1 (for 0x4584C0).
const u32* interactTable();
u32 interactTableBytes();

} //selpanel
```
- [ ] **Step 3:** write `sel_panel.cpp`:
```cpp
#include "sel_panel.h"
#include "sel_exe.h"
#include "sel_local.h"
#include "sel_send.h"
#include <SCBW/selection_ext.h>

using namespace selext;

namespace {

u8* const	LAYOUT				= (u8*)	0x0068C1E5;	//1: the multi-selection layout is up
u8* const	REFRESH_STAT_DATA	= (u8*)	0x0068C1F8;
const u8* const	SHIFT_HELD		= (u8*)	0x00596A28;
const u8* const	CTRL_HELD		= (u8*)	0x00596A29;
const u8* const	ALT_HELD		= (u8*)	0x00596A2A;
const u32* const VANILLA_INTERACT = (u32*)0x00504AF0;	//44 entries
const u32 VANILLA_CONTROLS = 44;
const u32 WIREFRAME_INTERACT = 0x004583E0;
const u16 VK_PAGE_UP = 0x21;
const u16 VK_PAGE_DOWN = 0x22;

//Made by the wireframe handler at USER_CREATE; its draw proc reads it.
struct WireframeUser {
	CUnit* unit;
	u16 unitId;
	u16 pad;
};

u32 interact[WIREFRAME_FIRST_ID - 1 + WIREFRAME_MAX];
bool interactBuilt;

u32 pageSize = VANILLA_MAX;		//of the last fill
u32 shownStart;
u32 shownCount;
s32 shownHitPoints[WIREFRAME_MAX];
u16 shownId[WIREFRAME_MAX];

BinDlg* firstChild(BinDlg* dialog) {
	return *(BinDlg**)((u8*)dialog + 0x42);
}

WireframeUser* userOf(BinDlg* control) {
	return (WireframeUser*)control->user;
}

//The dialog's wireframe controls by id; returns how many there are.
u32 collectWireframes(BinDlg* dialog, BinDlg** wireframes) {
	for (u32 k = 0; k < WIREFRAME_MAX; k++)
		wireframes[k] = NULL;
	for (BinDlg* control = firstChild(dialog); control != NULL; control = control->next) {
		const s32 k = control->index - (s32)WIREFRAME_FIRST_ID;
		if (k >= 0 && k < (s32)WIREFRAME_MAX)
			wireframes[k] = control;
	}
	u32 n = 0;
	while (n < WIREFRAME_MAX && wireframes[n] != NULL)
		n++;
	return n;
}

} //unnamed namespace

namespace selpanel {

void fill(BinDlg* dialog) {
	if (*LAYOUT != 1) {
		for (BinDlg* control = dialog->controlType ? dialog : firstChild(dialog);
			 control != NULL; control = control->next)
			selexe::hideControl(control);
		*LAYOUT = 1;
	}
	if (dialog->controlType)
		dialog = dialog->parent;

	static BinDlg* wireframes[WIREFRAME_MAX];
	const u32 controls = collectWireframes(dialog, wireframes);
	pageSize = pageSizeFor(dialog->bounds.width, controls);
	selectionPage = clampPage(selectionPage, clientCount, pageSize);
	shownStart = selectionPage * pageSize;

	u32 k = 0;
	for (u32 i = shownStart; i < SEL_MAX && k < pageSize; i++) {
		CUnit* unit = clientSel[i];
		if (unit == NULL)
			break;
		BinDlg* control = wireframes[k];
		userOf(control)->unit = unit;
		userOf(control)->unitId = unit->id;
		selexe::showControl(control);
		if (!(control->flags & 1)) {
			control->flags |= 1;
			selexe::invalidateControl(control);
		}
		shownHitPoints[k] = unit->hitPoints;
		shownId[k] = unit->id;
		k++;
	}
	shownCount = k;
	for (; k < controls; k++)
		selexe::hideControl(wireframes[k]);
}

bool changed() {
	if (shownCount == 0 && clientCount > shownStart)
		return true;
	if (clientCount != 0 && shownStart >= clientCount)
		return true;
	for (u32 k = 0; k < pageSize && shownStart + k < SEL_MAX; k++) {
		CUnit* unit = clientSel[shownStart + k];
		if (unit == NULL)
			continue;
		if (k >= shownCount || unit->hitPoints != shownHitPoints[k] || unit->id != shownId[k])
			return true;
	}
	return false;
}

void click(BinDlg* control) {
	static CUnit* list[SEL_MAX];
	CUnit* const clickedUnit = userOf(control)->unit;
	const bool shift = *SHIFT_HELD != 0;
	const bool ctrl = !shift && *CTRL_HELD != 0;
	u32 n = 0;
	if (shift) {
		for (u32 i = 0; i < SEL_MAX && clientSel[i] != NULL; i++)
			if (clientSel[i] != clickedUnit)
				list[n++] = clientSel[i];
	}
	else
	if (ctrl) {
		for (u32 i = 0; i < SEL_MAX && clientSel[i] != NULL; i++)
			if (clientSel[i]->id == clickedUnit->id)
				list[n++] = clientSel[i];
	}
	else {
		if (*ALT_HELD && selexe::selectRecentGroupOf(tagOf(clickedUnit)))
			return;
		list[n++] = clickedUnit;
	}
	if ((shift || ctrl) && n == 1 && *ALT_HELD && selexe::selectRecentGroupOf(tagOf(list[0])))
		return;
	//Removing a unit keeps the page; a new selection starts at page 0.
	if (!shift)
		selectionPage = 0;
	sellocal::buildActive(list, n);
	selsend::cmdactSelect(n, list);
	sellocal::requestRefresh();
}

void keyDown(const u8* event) {
	const u16 key = *(const u16*)(event + 8);
	const u32 pages = pageCountFor(clientCount, pageSize);
	if (key == VK_PAGE_UP && selectionPage > 0)
		selectionPage--;
	else
	if (key == VK_PAGE_DOWN && selectionPage + 1 < pages)
		selectionPage++;
	else
		return;
	*REFRESH_STAT_DATA = 1;
}

const u32* interactTable() {
	if (!interactBuilt) {
		for (u32 i = 0; i < VANILLA_CONTROLS; i++)
			interact[i] = VANILLA_INTERACT[i];
		for (u32 id = VANILLA_CONTROLS + 1; id < WIREFRAME_FIRST_ID + WIREFRAME_MAX; id++)
			interact[id - 1] = WIREFRAME_INTERACT;
		interactBuilt = true;
	}
	return interact;
}

u32 interactTableBytes() {
	return sizeof(interact);
}

} //selpanel
```
- [ ] **Step 4:** page reset on a new selection made by the player: in `sel_inject.cpp`'s `buildActiveWrapper` (the exe's callers: drag, click, recall, Alt-click) call a new `sellocal::buildActiveNewSelection(list, count)` that sets `selext::selectionPage = 0` then calls `buildActive`. Plugin-internal callers (`localRemove`, `deselectAndSend`, `addTwin`) keep calling `buildActive` and keep the page (clamped at the next fill). Add the declaration to `sel_local.h`:
```cpp
//buildActive for a new selection made by the player: also goes back to page 0.
void buildActiveNewSelection(CUnit** list, u32 count);
```
and to `sel_local.cpp`:
```cpp
void buildActiveNewSelection(CUnit** list, u32 count) {
	selectionPage = 0;
	buildActive(list, count);
}
```
- [ ] **Step 5:** add `sel_panel.cpp/.h` to `GPTP.vcxproj`; `tests/verify.ps1`: PASS; commit `feat: selection panel pages logic`.

### Task 4: panel hooks

**Files:** Modify `GPTP/hooks/selection_ext/sel_inject.cpp`, `selection_ext_hooks.h`, `GPTP/initialize.cpp`.

- [ ] **Step 1:** check entry boundaries of 0x425960, 0x424660, 0x458220 with the capstone boundary scan (Task 8 of stage 1+2) and that nothing jumps into their first 5 bytes.
- [ ] **Step 2:** add wrappers to `sel_inject.cpp`:
```cpp
//-------- Stage 3 --------//

//0x425960: EAX = dialog.
void __declspec(naked) panelFillWrapper() {
	static BinDlg* dialog;
	__asm {
		MOV dialog, EAX
		PUSHAD
	}
	selpanel::fill(dialog);
	__asm {
		POPAD
		RETN
	}
}

//0x424660: no arguments, BOOL in EAX.
void __declspec(naked) panelChangedWrapper() {
	static u32 result;
	__asm PUSHAD
	result = selpanel::changed() ? 1 : 0;
	__asm {
		POPAD
		MOV EAX, result
		RETN
	}
}

//0x458220: EDX = the clicked wireframe.
void __declspec(naked) panelClickWrapper() {
	static BinDlg* control;
	__asm {
		MOV control, EDX
		PUSHAD
	}
	selpanel::click(control);
	__asm {
		POPAD
		RETN
	}
}

//0x484350, the in-game KEYDOWN proc: ECX = the event.
void __declspec(naked) keyDownWrapper() {
	static const u8* event;
	__asm {
		MOV event, ECX
		PUSHAD
	}
	selpanel::keyDown(event);
	__asm {
		POPAD
		RETN
	}
}
```
and `hooks::injectSelectionPanelHooks()`:
```cpp
void injectSelectionPanelHooks() {
	//StatData's interact table (0x4584C0 passes it to 0x418100 at USER_CREATE).
	memoryPatch(0x004584C3, selpanel::interactTableBytes());
	memoryPatch(0x004584C8, (u32)selpanel::interactTable());
	//Tooltips for every wireframe id (sign-extended imm8, so at most 0x7F).
	memoryPatch(0x00457D7E, (u8)(selext::WIREFRAME_FIRST_ID + selext::WIREFRAME_MAX - 1));
	jmpPatch(panelFillWrapper,		0x00425960, <nops>);
	jmpPatch(panelChangedWrapper,	0x00424660, <nops>);
	jmpPatch(panelClickWrapper,		0x00458220, <nops>);
	jmpPatch(keyDownWrapper,		0x00484350, 0);
}
```
(`<nops>` from Step 1's scan.) Declare it in `selection_ext_hooks.h`; in `buildActiveWrapper` call `sellocal::buildActiveNewSelection` instead of `sellocal::buildActive`; add `hooks::injectSelectionPanelHooks();` after `hooks::injectSelectChunkHooks();` in `initialize.cpp`.
- [ ] **Step 3:** `tests/verify.ps1`: PASS + build; commit `feat: selection panel pages hooks`.

### Task 5: in-game test round

Repack `rez/statdata.bin` (from Task 2) **and** the new `GPTP.qdp` together. Round:
3.1 Start a game with 100+ units; select them. Expected: the panel fills the widened box with wireframes (2 rows); "self-test passed".
3.2 PgDn repeatedly to the last page, then PgUp to the first. Expected: pages advance and stop at both ends.
3.3 With one unit selected, and with nothing selected, press PgUp/PgDn. Expected: nothing happens.
3.4 On page 2: click a wireframe; reselect; Shift-click a wireframe; Ctrl-click a wireframe. Expected: plain selects that unit (page 0); Shift removes it and stays on the page; Ctrl selects every unit of that type in the whole selection (not just the page).
3.5 On the last page, let units die until the page is empty. Expected: the panel moves to the new last page.
3.6 Hover a wireframe on the last page. Expected: its tooltip.
3.7 Set `Manifold.ini` to 640×480. Expected: pages of 12 at the vanilla box.
3.8 Open chat, press PgUp/PgDn, type. Expected: chat unaffected; the page doesn't change.

### Task 6: docs
Spec status for stage 3, the memory file, commit.
