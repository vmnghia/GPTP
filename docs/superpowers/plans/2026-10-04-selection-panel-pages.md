# Selection Panel Pages Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The selection panel gets a 24 px taller box, three gapless rows of wireframes, page tabs (2 x 8) on its left and arrows past 16 pages.

**Architecture:** Pure logic (layout rects, paging, the console-art raise, the 9-slice frame) goes in host-tested files (`SCBW/selection_ext_core.cpp`, new `SCBW/console_raise.cpp`). The game side splits in two. The console code (`hooks/interface/resolution_hud.cpp`) raises the art, grows StatData and lays its controls out as the `.bin` loads. The panel code (`hooks/selection_ext/sel_panel.cpp`) shows, draws and clicks the new controls. The generator outside the repo adds the controls to `statdata.bin`.

**Tech Stack:** C++ (MSVC, Win32, GPTP plugin), inline asm stubs, Python 2.7 generator, host test `tests/selection_ext_test.bat`.

**Spec:** `docs/superpowers/specs/2026-10-04-selection-panel-pages-design.md`

## Global Constraints

- The box is raised **24 px**; three rows of **33 x 34** wireframes, edge to edge, from x 58, y 8 of the raised StatData root.
- Tabs: **2 columns x 8 rows**, 20 px wide, from x 14; row r spans E(r)..E(r+1) with E(r) = round(102·r/8) = 0, 13, 26, 38, 51, 64, 77, 89, 102. Tabs for 2-16 pages; arrows (15 px tall, 40 wide) and "page/pages" above 16; nothing for 1 page.
- `WIREFRAME_MAX` = **144**; tab ids 0xB1-0xC0, ▲ 0xC1, page number 0xC2, ▼ 0xC3.
- Vanilla StatData controls move down **12 px**.
- Raised spans (vanilla columns, by `consoleRace()`): Zerg 138-408, Terran 143-407, Protoss 143-408, replays 150-405; cut row **420**. Every span inside [138, 408).
- Frames: GRP at `[0x68C1C0]`, frame **0x0D**, 33 x 34. Current tab remap: 0xA5 → **0x7E**, 0x29 and 0x2A → **0xA0**; green number. Greyed arrow: 0xA5 → **0x91**, 0xA0 → **0x43**, triangle **0x4A**; normal triangle **0x54**.
- Paging is local UI: nothing synced changes, no command is sent.
- The selection limit lives only in `selext::SEL_MAX`; no "400" in names.
- Naked stubs use only statics and registers (`tests/check_naked_wrappers.py`); `type` and `size` are reserved words in inline asm.
- Files are committed as they are stored. Docs that are CRLF in git (`docs/resolution.md`) are staged with `git -c core.autocrlf=false add`; everything else with plain `git add`.
- Every console-art change sits in one clearly named, switchable step and is documented in `docs/resolution.md` (the user may replace the consoles with SC2 art).

## Review Focus

1. **An out-of-date `statdata.bin`** (the 90-wireframe one now repacked, or vanilla's): no crash, no stray controls, the warning names the problem. Task 5 step 1 pins the message rule (`panelFileOutdated`).
2. **A selection that shrinks past the shown page** (units die on the last page): the page clamps and the tabs drop to the new count. Task 1 pins `clampPage` with the new page size; Task 5 redraws on any change of pages or page.
3. **Clicks on the label or a greyed arrow**: nothing happens, no page change. Task 1 pins `pageAfterArrow` at the ends; Task 5 ignores the label.
4. **Very wide screens**: ids above 0x7F keep their tooltips and clicks. Task 5 adds the tooltip stub with a `static_assert` on the last id; in-game test 11.8.
5. **Pieces whose top is near 0 in the raised span**: `raisePiece` clamps to row 0. Task 2 pins it.

---

## File Structure

- `GPTP/SCBW/selection_ext.h`, `GPTP/SCBW/selection_ext_core.cpp`: panel constants, rects, paging, `.bin` layout, GRP decode, 9-slice (pure).
- `GPTP/SCBW/console_raise.h`, `GPTP/SCBW/console_raise.cpp` (new): the console art raise and the StatFluf split (pure).
- `GPTP/hooks/selection_ext/sel_selftest.cpp`: checks for both.
- `tests/selection_ext_test.bat`, `GPTP/GPTP.vcxproj`: build `console_raise.cpp`.
- `GPTP/hooks/interface/resolution_hud.cpp`: raise the art, split StatFluf, grow StatData, lay out its controls.
- `GPTP/hooks/selection_ext/sel_panel.cpp/.h`: show, draw and click the tabs and arrows.
- `GPTP/hooks/selection_ext/sel_inject.cpp`, `tests/check_naked_wrappers.py`: the tooltip stub.
- `D:\SC Modding\SCManifold\to-repack\make_statdata_wide.py` (outside the repo): 144 wireframes plus 19 page controls.
- `docs/resolution.md`: Console art reference, TODO.

Setup (once, before Task 1): `git checkout -b feature/selection-pages` from master.

Test command for every task: `powershell -ExecutionPolicy Bypass -File tests/verify.ps1`. It runs the host test, builds the plugin and checks the naked stubs. Expected last line: `plugin build succeeded`. For the RED steps, run only the host test: `cmd //c "tests\\selection_ext_test.bat"` (expected `FAIL: ...` or a compile error), and for GREEN expect `PASS`.

---

### Task 1: Panel layout and paging (pure)

**Files:**
- Modify: `GPTP/SCBW/selection_ext.h` (the "Selection panel pages" section)
- Modify: `GPTP/SCBW/selection_ext_core.cpp` (`pageSizeFor` and new functions)
- Test: `GPTP/hooks/selection_ext/sel_selftest.cpp` (`pages()`, new `panelLayout()`)

**Interfaces:**
- Produces (in `namespace selext`): the constants below; `struct PanelRect { s16 left, top, right, bottom; }` (inclusive, as `.bin` stores child rects); `PanelRect wireframeRect(u32 k)`, `PanelRect pageTabRect(u32 tab)`, `PanelRect pageUpRect()`, `PanelRect pageLabelRect()`, `PanelRect pageDownRect()`; `u32 pageSizeFor(u32 dialogWidth, u32 wireframeControls)` (new formula); `enum PageControls { PAGE_CONTROLS_NONE, PAGE_CONTROLS_TABS, PAGE_CONTROLS_ARROWS }`; `PageControls pageControlsFor(u32 pages)`; `u32 pageAfterTab(u32 tab, u32 page, u32 pages)`; `u32 pageAfterArrow(u32 page, u32 pages, bool up)`; `void layOutStatDataBin(u8* base)`.

- [ ] **Step 1: Write the failing tests**

In `sel_selftest.cpp`, replace the body of `pages()` with:

```cpp
void pages() {
	CHECK(pageSizeFor(270, WIREFRAME_MAX) == 18);	//640 wide: 6 columns
	CHECK(pageSizeFor(910, WIREFRAME_MAX) == 75);	//1280: 25 columns
	CHECK(pageSizeFor(1550, WIREFRAME_MAX) == 132);	//1920: 44 columns
	CHECK(pageSizeFor(1678, WIREFRAME_MAX) == 144);	//2048: 48 columns
	CHECK(pageSizeFor(4000, WIREFRAME_MAX) == WIREFRAME_MAX);
	CHECK(pageSizeFor(910, 12) == 12);		//vanilla statdata.bin
	CHECK(pageSizeFor(60, WIREFRAME_MAX) == 3);	//never below one column
	CHECK(pageCountFor(0, 75) == 1 && pageCountFor(75, 75) == 1);
	CHECK(pageCountFor(76, 75) == 2 && pageCountFor(400, 75) == 6 && pageCountFor(400, 18) == 23);
	CHECK(clampPage(5, 76, 75) == 1 && clampPage(1, 76, 75) == 1);
	CHECK(clampPage(5, 75, 75) == 0);		//units died: back to the last page
	CHECK(clampPage(3, 0, 75) == 0);
	//Fewer wireframe controls than the panel fits: statdata.bin not repacked.
	CHECK(wireframesMissing(910, 12));
	CHECK(wireframesMissing(270, 12));		//even at 640: 18 fit now
	CHECK(!wireframesMissing(270, 18));
	CHECK(!wireframesMissing(910, WIREFRAME_MAX));
	CHECK(!wireframesMissing(4000, WIREFRAME_MAX));
}
```

Add after `pages()`:

```cpp
bool rectIs(PanelRect r, s16 left, s16 top, s16 right, s16 bottom) {
	return r.left == left && r.top == top && r.right == right && r.bottom == bottom;
}

void panelLayout() {
	//Wireframes: column-major, 3 rows, 33 x 34, edge to edge.
	CHECK(rectIs(wireframeRect(0), 58, 8, 90, 41));
	CHECK(rectIs(wireframeRect(2), 58, 76, 90, 109));
	CHECK(rectIs(wireframeRect(4), 91, 42, 123, 75));
	//Tabs: 2 x 8, column-major; rows E(r) = round(102r/8) from y 8.
	CHECK(rectIs(pageTabRect(0), 14, 8, 33, 20));		//13 tall
	CHECK(rectIs(pageTabRect(3), 14, 46, 33, 58));		//E 38-51
	CHECK(rectIs(pageTabRect(7), 14, 97, 33, 109));		//E 89-102: level with the grid's bottom
	CHECK(rectIs(pageTabRect(8), 34, 8, 53, 20));
	CHECK(rectIs(pageTabRect(15), 34, 97, 53, 109));
	u32 total = 0;
	for (u32 r = 0; r < TAB_ROWS; r++)
		total += pageTabRect(r).bottom - pageTabRect(r).top + 1;
	CHECK(total == GRID_ROWS * CELL_HEIGHT);
	//Arrows level with the grid's top and bottom, the number between.
	CHECK(rectIs(pageUpRect(), 14, 8, 53, 22));
	CHECK(rectIs(pageLabelRect(), 14, 23, 53, 94));
	CHECK(rectIs(pageDownRect(), 14, 95, 53, 109));
	//Which controls show.
	CHECK(pageControlsFor(0) == PAGE_CONTROLS_NONE && pageControlsFor(1) == PAGE_CONTROLS_NONE);
	CHECK(pageControlsFor(2) == PAGE_CONTROLS_TABS && pageControlsFor(16) == PAGE_CONTROLS_TABS);
	CHECK(pageControlsFor(17) == PAGE_CONTROLS_ARROWS);
	//Clicks.
	CHECK(pageAfterTab(3, 0, 6) == 3 && pageAfterTab(9, 2, 6) == 2);	//past the last page: no change
	CHECK(pageAfterArrow(0, 23, true) == 0 && pageAfterArrow(0, 23, false) == 1);
	CHECK(pageAfterArrow(22, 23, false) == 22 && pageAfterArrow(22, 23, true) == 21);
	//Ids.
	CHECK(PAGE_TAB_FIRST_ID == 0xB1 && PAGE_UP_ID == 0xC1 && PAGE_LABEL_ID == 0xC2 && PAGE_DOWN_ID == 0xC3);
}

//A .bin as read from disk: root (0x56 bytes) then children linked by file
//offsets (root +0x42 to the first, each child +0 to the next).
void statDataBin() {
	static u8 bin[0x56 * 6];
	memset(bin, 0, sizeof(bin));
	const s16 ids[5] = { 1, (s16)(WIREFRAME_FIRST_ID + 4), (s16)(PAGE_TAB_FIRST_ID + 9),
	                     (s16)PAGE_DOWN_ID, -5 };
	*(u32*)(bin + 0x42) = 0x56;
	for (u32 i = 0; i < 5; i++) {
		u8* entry = bin + 0x56 * (i + 1);
		*(u32*)entry = i < 4 ? 0x56 * (i + 2) : 0;
		s16* rect = (s16*)(entry + 4);
		rect[0] = 30; rect[1] = 10; rect[2] = 60; rect[3] = 40;
		*(s16*)(entry + 0x20) = ids[i];
	}
	layOutStatDataBin(bin);
	const s16* vanilla = (const s16*)(bin + 0x56 + 4);
	CHECK(vanilla[0] == 30 && vanilla[1] == 22 && vanilla[2] == 60 && vanilla[3] == 52);	//down 12
	const s16* wire = (const s16*)(bin + 0x56 * 2 + 4);
	CHECK(wire[0] == 91 && wire[1] == 42 && wire[2] == 123 && wire[3] == 75);
	const s16* tab = (const s16*)(bin + 0x56 * 3 + 4);
	CHECK(tab[0] == 34 && tab[1] == 21 && tab[2] == 53 && tab[3] == 33);	//tab 9: column 1, row 1
	const s16* down = (const s16*)(bin + 0x56 * 4 + 4);
	CHECK(down[0] == 14 && down[1] == 95 && down[2] == 53 && down[3] == 109);
	const s16* negative = (const s16*)(bin + 0x56 * 5 + 4);
	CHECK(negative[1] == 22);		//vanilla controls with negative ids move too
}
```

Register both in `selfTest()` after `pages();`:

```cpp
	pages();
	panelLayout();
	statDataBin();
```

- [ ] **Step 2: Run the host test to verify it fails**

Run: `cmd //c "tests\\selection_ext_test.bat"`
Expected: compile errors (`wireframeRect`, `PanelRect`, `PAGE_TAB_FIRST_ID` … undefined).

- [ ] **Step 3: Write the declarations**

In `selection_ext.h`, replace the block from `//-------- Selection panel pages (stage 3) --------//` down to and including the `u32 pageSizeFor(...)` declaration and its comment with:

```cpp
//-------- Selection panel pages (stage 3; taller box 2026-10-04) --------//
//Design: docs/superpowers/specs/2026-10-04-selection-panel-pages-design.md

//statdata.bin's controls (make_statdata_wide.py writes them): wireframes
//from id 0x21, then the page tabs, then the arrows and the page number.
const u32 WIREFRAME_FIRST_ID = 0x21;
const u32 WIREFRAME_MAX = 144;
const u32 PAGE_TABS = 16;
const u32 PAGE_TAB_FIRST_ID = WIREFRAME_FIRST_ID + WIREFRAME_MAX;	//0xB1
const u32 PAGE_UP_ID = PAGE_TAB_FIRST_ID + PAGE_TABS;				//0xC1
const u32 PAGE_LABEL_ID = PAGE_UP_ID + 1;
const u32 PAGE_DOWN_ID = PAGE_UP_ID + 2;
const u32 PANEL_LAST_ID = PAGE_DOWN_ID;
//How much higher and taller the selection box is than vanilla's.
const s32 PANEL_RAISE = 24;
//Vanilla's own StatData controls move down this much: centred in the box.
const s32 VANILLA_CONTROLS_DROP = PANEL_RAISE / 2;
//The layout, relative to the raised StatData root.
const s32 GRID_LEFT = 58;
const s32 GRID_TOP = 8;
const s32 CELL_WIDTH = 33;
const s32 CELL_HEIGHT = 34;
const u32 GRID_ROWS = 3;
const s32 GRID_RIGHT_MARGIN = 10;
const s32 TABS_LEFT = 14;
const s32 TAB_WIDTH = 20;
const u32 TAB_ROWS = 8;
const s32 ARROW_HEIGHT = 15;

//A control's rect as a .bin stores it: inclusive, relative to the root.
struct PanelRect {
	s16 left, top, right, bottom;
};
//Wireframe k of a page: column-major, GRID_ROWS rows, edge to edge.
PanelRect wireframeRect(u32 k);
//Page tab 0-15: 2 columns of TAB_ROWS, column-major, together as tall as
//the grid.
PanelRect pageTabRect(u32 tab);
//The arrows (level with the grid's top and bottom) and the page number
//between them, shown instead of the tabs past PAGE_TABS pages.
PanelRect pageUpRect();
PanelRect pageLabelRect();
PanelRect pageDownRect();
//Wireframes per page: GRID_ROWS x the columns that fit a StatData dialog
//this wide, at most the controls present.
u32 pageSizeFor(u32 dialogWidth, u32 wireframeControls);
```

After the existing `pageAfterKey` declaration, add:

```cpp
//Which page controls show for this many pages: none for one, tabs up to
//PAGE_TABS, arrows past that.
enum PageControls { PAGE_CONTROLS_NONE, PAGE_CONTROLS_TABS, PAGE_CONTROLS_ARROWS };
PageControls pageControlsFor(u32 pages);
//The page after a click on tab (unchanged if the tab is past the last page).
u32 pageAfterTab(u32 tab, u32 page, u32 pages);
//The page after a click on an arrow (up: the previous page), stopping at
//the ends.
u32 pageAfterArrow(u32 page, u32 pages, bool up);
//Lays out StatData's controls in its .bin as read from disk (before the
//game turns offsets into pointers): the wireframes, tabs and arrows get
//their rects, vanilla's own controls move down VANILLA_CONTROLS_DROP. The
//root is the console code's (resolution_hud.cpp).
void layOutStatDataBin(u8* base);
```

- [ ] **Step 4: Write the implementation**

In `selection_ext_core.cpp`, replace `pageSizeFor` with:

```cpp
u32 pageSizeFor(u32 dialogWidth, u32 wireframeControls) {
	const s32 room = (s32)dialogWidth - GRID_LEFT - GRID_RIGHT_MARGIN - CELL_WIDTH;
	const u32 columns = room >= 0 ? room / CELL_WIDTH + 1 : 1;
	const u32 size = GRID_ROWS * columns;
	return size < wireframeControls ? size : wireframeControls;
}

namespace {

PanelRect rectAt(s32 left, s32 top, s32 width, s32 height) {
	PanelRect r;
	r.left = (s16)left;
	r.top = (s16)top;
	r.right = (s16)(left + width - 1);
	r.bottom = (s16)(top + height - 1);
	return r;
}

//Row r's top in the tab column, from the grid's top: round(102r / 8).
s32 tabEdge(u32 r) {
	const s32 height = GRID_ROWS * CELL_HEIGHT;
	return (2 * height * (s32)r + (s32)TAB_ROWS) / (2 * (s32)TAB_ROWS);
}

} //unnamed namespace

PanelRect wireframeRect(u32 k) {
	return rectAt(GRID_LEFT + CELL_WIDTH * (s32)(k / GRID_ROWS),
	              GRID_TOP + CELL_HEIGHT * (s32)(k % GRID_ROWS), CELL_WIDTH, CELL_HEIGHT);
}

PanelRect pageTabRect(u32 tab) {
	const u32 column = tab / TAB_ROWS, row = tab % TAB_ROWS;
	return rectAt(TABS_LEFT + TAB_WIDTH * (s32)column, GRID_TOP + tabEdge(row),
	              TAB_WIDTH, tabEdge(row + 1) - tabEdge(row));
}

PanelRect pageUpRect() {
	return rectAt(TABS_LEFT, GRID_TOP, 2 * TAB_WIDTH, ARROW_HEIGHT);
}

PanelRect pageLabelRect() {
	const s32 height = GRID_ROWS * CELL_HEIGHT;
	return rectAt(TABS_LEFT, GRID_TOP + ARROW_HEIGHT, 2 * TAB_WIDTH, height - 2 * ARROW_HEIGHT);
}

PanelRect pageDownRect() {
	const s32 height = GRID_ROWS * CELL_HEIGHT;
	return rectAt(TABS_LEFT, GRID_TOP + height - ARROW_HEIGHT, 2 * TAB_WIDTH, ARROW_HEIGHT);
}
```

`tabEdge` check: (2·102·r + 8) / 16 gives 0, 13, 26, 38, 51, 64, 77, 89, 102. After `pageAfterKey`'s definition add:

```cpp
PageControls pageControlsFor(u32 pages) {
	if (pages <= 1)
		return PAGE_CONTROLS_NONE;
	return pages <= PAGE_TABS ? PAGE_CONTROLS_TABS : PAGE_CONTROLS_ARROWS;
}

u32 pageAfterTab(u32 tab, u32 page, u32 pages) {
	return tab < pages ? tab : page;
}

u32 pageAfterArrow(u32 page, u32 pages, bool up) {
	if (up)
		return page > 0 ? page - 1 : 0;
	return page + 1 < pages ? page + 1 : page;
}

void layOutStatDataBin(u8* base) {
	u32 offset = *(const u32*)(base + 0x42);
	for (u32 guard = 0; offset != 0 && guard < 1000; guard++) {
		u8* const entry = base + offset;
		const s32 id = *(const s16*)(entry + 0x20);
		s16* const rect = (s16*)(entry + 4);
		PanelRect r;
		bool placed = true;
		if (id >= (s32)WIREFRAME_FIRST_ID && id < (s32)PAGE_TAB_FIRST_ID)
			r = wireframeRect(id - WIREFRAME_FIRST_ID);
		else if (id >= (s32)PAGE_TAB_FIRST_ID && id < (s32)PAGE_UP_ID)
			r = pageTabRect(id - PAGE_TAB_FIRST_ID);
		else if (id == (s32)PAGE_UP_ID)
			r = pageUpRect();
		else if (id == (s32)PAGE_LABEL_ID)
			r = pageLabelRect();
		else if (id == (s32)PAGE_DOWN_ID)
			r = pageDownRect();
		else
			placed = false;
		if (placed) {
			rect[0] = r.left;
			rect[1] = r.top;
			rect[2] = r.right;
			rect[3] = r.bottom;
		}
		else {
			rect[1] = (s16)(rect[1] + VANILLA_CONTROLS_DROP);
			rect[3] = (s16)(rect[3] + VANILLA_CONTROLS_DROP);
		}
		offset = *(const u32*)entry;
	}
}
```

- [ ] **Step 5: Run the host test**

Run: `cmd //c "tests\\selection_ext_test.bat"`
Expected: `PASS`. If a check fails, the message names its line; fix the code, not the check (the values come from the spec).

- [ ] **Step 6: Fix the plugin's other uses and build**

`sel_panel.cpp` still compiles (it uses `WIREFRAME_FIRST_ID`, `WIREFRAME_MAX`, `pageSizeFor`). `sel_inject.cpp` has `memoryPatch(0x00457D7E, (u8)(selext::WIREFRAME_FIRST_ID + selext::WIREFRAME_MAX - 1));`. With 144 the value is 0xB0, which the sign-extended imm8 reads as −80, so tooltips would stop. Leave it for Task 5, which replaces it, but keep the build honest now by changing the line to:

```cpp
	//Tooltips: replaced by wireframeTooltipStub in Task 5 (ids past 0x7F).
	memoryPatch(0x00457D7E, (u8)0x7F);
```

Run: `powershell -ExecutionPolicy Bypass -File tests/verify.ps1`
Expected: `plugin build succeeded`.

- [ ] **Step 7: Commit**

```bash
git add GPTP/SCBW/selection_ext.h GPTP/SCBW/selection_ext_core.cpp GPTP/hooks/selection_ext/sel_selftest.cpp GPTP/hooks/selection_ext/sel_inject.cpp
git commit -m "feat: selection panel layout: 3 gapless rows, page tabs and arrows (pure)"
```

---

### Task 2: Console art raise (pure)

**Files:**
- Create: `GPTP/SCBW/console_raise.h`, `GPTP/SCBW/console_raise.cpp`
- Modify: `tests/selection_ext_test.bat` (compile the new file), `GPTP/GPTP.vcxproj` (add it)
- Test: `GPTP/hooks/selection_ext/sel_selftest.cpp` (new `consoleRaise()`)

**Interfaces:**
- Consumes: nothing.
- Produces (namespace `consoleraise`): `struct Span { s32 left, right; }`; `const s32 CUT_ROW = 420`; `extern const Span SPANS[4]`; `void raiseArt(u8* art, s32 width, s32 height, Span span, s32 cutRow, s32 raise)`; `struct Piece { s32 left, top, right, bottom; }` (right and bottom exclusive); `u32 raisePiece(Piece piece, Span span, s32 cutRow, s32 raise, Piece out[3])`.

- [ ] **Step 1: Write the failing tests**

At the top of `sel_selftest.cpp`, after `#include <SCBW/selection_ext.h>`, add `#include <SCBW/console_raise.h>`. Add before `selfTest`:

```cpp
void consoleRaise() {
	using namespace consoleraise;
	//6 x 10 art; column x, row y holds 10 * y + x + 1 (never 0).
	u8 art[60];
	for (u32 i = 0; i < 60; i++)
		art[i] = (u8)(10 * (i / 6) + i % 6 + 1);
	const Span span = { 2, 4 };
	raiseArt(art, 6, 10, span, 6, 2);
	CHECK(art[0 * 6 + 2] == 10 * 2 + 3);		//row 0 now shows row 2
	CHECK(art[3 * 6 + 3] == 10 * 5 + 4);		//row 3 shows row 5
	CHECK(art[4 * 6 + 2] == 10 * 6 + 3 && art[5 * 6 + 2] == 10 * 6 + 3);	//rows 4-5 repeat the cut row
	CHECK(art[6 * 6 + 2] == 10 * 6 + 3 && art[9 * 6 + 3] == 10 * 9 + 4);	//from the cut down: unchanged
	CHECK(art[0 * 6 + 1] == 2 && art[3 * 6 + 4] == 10 * 3 + 5);			//outside the span: unchanged

	//Pieces: split at the span's edges, the part inside grows up.
	Piece out[3];
	const Piece across = { 130, 367, 450, 388 };
	const Span terran = SPANS[1];
	CHECK(raisePiece(across, terran, CUT_ROW, 24, out) == 3);
	CHECK(out[0].left == 130 && out[0].right == 143 && out[0].top == 367 && out[0].bottom == 388);
	CHECK(out[1].left == 143 && out[1].right == 407 && out[1].top == 343 && out[1].bottom == 388);
	CHECK(out[2].left == 407 && out[2].right == 450 && out[2].top == 367);
	const Piece inside = { 200, 350, 300, 380 };
	CHECK(raisePiece(inside, terran, CUT_ROW, 24, out) == 1 && out[0].top == 326 && out[0].bottom == 380);
	const Piece outside = { 0, 293, 23, 315 };
	CHECK(raisePiece(outside, terran, CUT_ROW, 24, out) == 1 && out[0].top == 293 && out[0].left == 0);
	const Piece below = { 200, 430, 300, 440 };
	CHECK(raisePiece(below, terran, CUT_ROW, 24, out) == 1 && out[0].top == 430);
	const Piece high = { 200, 10, 300, 40 };
	CHECK(raisePiece(high, terran, CUT_ROW, 24, out) == 1 && out[0].top == 0);	//clamped
	//Every span lies inside StatData's vanilla columns [138, 408).
	for (u32 race = 0; race < 4; race++)
		CHECK(SPANS[race].left >= 138 && SPANS[race].right <= 408 && SPANS[race].left < SPANS[race].right);
}
```

Register it in `selfTest()` after `statDataBin();`: `consoleRaise();`.

- [ ] **Step 2: Run to verify it fails**

Run: `cmd //c "tests\\selection_ext_test.bat"`
Expected: compile error, `SCBW/console_raise.h` not found.

- [ ] **Step 3: Write the header**

`GPTP/SCBW/console_raise.h`:

```cpp
//Raising the selection box in the console art (24 px taller selection
//panel). Pure, so the host test covers it; resolution_hud.cpp applies it to
//the console image and to the decorative pieces (StatFluf).
//Design: docs/superpowers/specs/2026-10-04-selection-panel-pages-design.md
//section 6; documented for art replacements in docs/resolution.md
//("Console art reference").
#pragma once
#include "../types.h"

namespace consoleraise {

//Columns [left, right) of the vanilla 640x480 art.
struct Span {
	s32 left, right;
};

//The row the raise cuts at: inside the box's black area in every console.
const s32 CUT_ROW = 420;

//The raised columns of each console, indexed like consoleRace(): 0 Zerg,
//1 Terran, 2 Protoss, 3 replays (nconsole.pcx). Each lies inside StatData's
//vanilla columns [138, 408), so StatData repaints the rows the raise
//rewrites below the old box top.
extern const Span SPANS[4];

//In art (width x height, row-major), within span: every row above cutRow
//moves up raise rows (those pushed above row 0 are dropped), and the raise
//rows just above cutRow repeat cutRow.
void raiseArt(u8* art, s32 width, s32 height, Span span, s32 cutRow, s32 raise);

//A rect of the art, right and bottom exclusive.
struct Piece {
	s32 left, top, right, bottom;
};

//A decorative piece for the raised art: split at the span's edges, and the
//part inside grown raise rows upward (top clamped at 0) if it starts above
//cutRow. Writes up to 3 pieces to out; returns how many.
u32 raisePiece(Piece piece, Span span, s32 cutRow, s32 raise, Piece out[3]);

} //consoleraise
```

- [ ] **Step 4: Write the implementation**

`GPTP/SCBW/console_raise.cpp`:

```cpp
#include "console_raise.h"

namespace consoleraise {

const Span SPANS[4] = {
	{ 138, 408 },	//Zerg
	{ 143, 407 },	//Terran
	{ 143, 408 },	//Protoss
	{ 150, 405 },	//replays
};

void raiseArt(u8* art, s32 width, s32 height, Span span, s32 cutRow, s32 raise) {
	if (cutRow >= height || raise <= 0)
		return;
	for (s32 x = span.left; x < span.right && x < width; x++) {
		for (s32 y = 0; y < cutRow - raise; y++)
			art[y * width + x] = art[(y + raise) * width + x];
		for (s32 y = cutRow - raise; y < cutRow; y++)
			if (y >= 0)
				art[y * width + x] = art[cutRow * width + x];
	}
}

u32 raisePiece(Piece piece, Span span, s32 cutRow, s32 raise, Piece out[3]) {
	if (piece.top >= cutRow || piece.right <= span.left || piece.left >= span.right) {
		out[0] = piece;
		return 1;
	}
	u32 n = 0;
	if (piece.left < span.left) {
		out[n] = piece;
		out[n].right = span.left;
		n++;
	}
	out[n] = piece;
	out[n].left = piece.left > span.left ? piece.left : span.left;
	out[n].right = piece.right < span.right ? piece.right : span.right;
	out[n].top = piece.top - raise > 0 ? piece.top - raise : 0;
	n++;
	if (piece.right > span.right) {
		out[n] = piece;
		out[n].left = span.right;
		n++;
	}
	return n;
}

} //consoleraise
```

In `tests/selection_ext_test.bat`, add `..\GPTP\SCBW\console_raise.cpp` after `..\GPTP\SCBW\selection_ext_core.cpp` on the `cl` line. In `GPTP/GPTP.vcxproj`, after line `<ClCompile Include="SCBW\selection_ext_core.cpp" />` add `<ClCompile Include="SCBW\console_raise.cpp" />`, and after `<ClInclude Include="SCBW\selection_ext.h" />` add `<ClInclude Include="SCBW\console_raise.h" />`.

- [ ] **Step 5: Run the host test and the build**

Run: `cmd //c "tests\\selection_ext_test.bat"` → Expected: `PASS`.
Run: `powershell -ExecutionPolicy Bypass -File tests/verify.ps1` → Expected: `plugin build succeeded`.

- [ ] **Step 6: Commit**

```bash
git add GPTP/SCBW/console_raise.h GPTP/SCBW/console_raise.cpp GPTP/hooks/selection_ext/sel_selftest.cpp tests/selection_ext_test.bat GPTP/GPTP.vcxproj
git commit -m "feat: console art raise for the taller selection box (pure)"
```

---

### Task 3: Frame drawing (pure)

**Files:**
- Modify: `GPTP/SCBW/selection_ext.h`, `GPTP/SCBW/selection_ext_core.cpp`
- Test: `GPTP/hooks/selection_ext/sel_selftest.cpp` (new `frames()`)

**Interfaces:**
- Produces (namespace `selext`): `const u32 FRAME_WIDTH = 33, FRAME_HEIGHT = 34`; `bool decodeGrpFrame(const u8* grp, u32 frame, u8* out)`; `u32 nineSliceSource(u32 x, u32 length, u32 size)`; `void drawNineSlice(const u8* frame, u8* dst, u32 pitch, u32 dstWidth, u32 dstHeight, s32 x, s32 y, u32 width, u32 height, const u8* remap)`.

- [ ] **Step 1: Write the failing tests**

Add before `selfTest`:

```cpp
void frames() {
	//The stretch: 4 px corners as they are, the middle repeated.
	CHECK(nineSliceSource(0, 20, 33) == 0 && nineSliceSource(3, 20, 33) == 3);
	CHECK(nineSliceSource(4, 20, 33) == 4 && nineSliceSource(15, 20, 33) == 15);
	CHECK(nineSliceSource(16, 20, 33) == 29 && nineSliceSource(19, 20, 33) == 32);
	for (u32 x = 0; x < 33; x++)
		CHECK(nineSliceSource(x, 33, 33) == x);		//full size: as it is
	CHECK(nineSliceSource(40, 45, 33) == 4 + 36 % 25);

	//A GRP with 2 frames of 33 x 34; frame 1 is all 7 but a transparent
	//first pixel and a 4-pixel repeat of 9 on row 1.
	static u8 grp[6 + 16 + 34 * 2 + 34 * 8];
	memset(grp, 0, sizeof(grp));
	*(u16*)grp = 2; *(u16*)(grp + 2) = 33; *(u16*)(grp + 4) = 34;
	const u32 data = 6 + 16;
	u8* f = grp + 6 + 8;
	f[0] = 0; f[1] = 0; f[2] = 33; f[3] = 34;
	*(u32*)(f + 4) = data;
	u8* line = grp + data + 34 * 2;
	for (u32 row = 0; row < 34; row++) {
		*(u16*)(grp + data + 2 * row) = (u16)(line - (grp + data));
		if (row == 0) {
			*line++ = 0x81;					//skip 1
			*line++ = 0x40 | 32; *line++ = 7;	//32 x 7
		}
		else if (row == 1) {
			*line++ = 0x40 | 4; *line++ = 9;	//4 x 9
			*line++ = 0x40 | 29; *line++ = 7;
		}
		else {
			*line++ = 0x40 | 33; *line++ = 7;
		}
	}
	static u8 decoded[33 * 34];
	CHECK(decodeGrpFrame(grp, 1, decoded));
	CHECK(decoded[0] == 0 && decoded[1] == 7 && decoded[32] == 7);
	CHECK(decoded[33] == 9 && decoded[36] == 9 && decoded[37] == 7);
	CHECK(decoded[33 * 33 + 32] == 7);
	CHECK(!decodeGrpFrame(grp, 2, decoded));		//no such frame

	//Drawing: a frame whose pixels name their source (row band x 3 + column band).
	static u8 frame[33 * 34];
	for (u32 r = 0; r < 34; r++)
		for (u32 c = 0; c < 33; c++)
			frame[r * 33 + c] = (u8)(10 * (r < 4 ? 1 : r >= 30 ? 3 : 2) + (c < 4 ? 1 : c >= 29 ? 3 : 2));
	frame[0] = 0;							//a transparent corner pixel
	static u8 dst[24 * 16];
	memset(dst, 0xEE, sizeof(dst));
	drawNineSlice(frame, dst, 24, 24, 16, 1, 1, 20, 13, NULL);
	CHECK(dst[1 * 24 + 1] == 0xEE);			//transparent: untouched
	CHECK(dst[1 * 24 + 2] == 11);			//top-left corner band
	CHECK(dst[7 * 24 + 10] == 22);			//middle
	CHECK(dst[13 * 24 + 20] == 33);			//bottom-right corner
	CHECK(dst[0] == 0xEE && dst[14 * 24 + 21] == 0xEE);	//outside: untouched
	//Remap and clipping at the edges.
	static u8 remap[256];
	for (u32 i = 0; i < 256; i++)
		remap[i] = (u8)i;
	remap[22] = 5;
	drawNineSlice(frame, dst, 24, 24, 16, -2, 10, 20, 13, remap);
	CHECK(dst[15 * 24 + 0] != 0xEE);		//drawn up to the bottom edge
	CHECK(dst[15 * 24 + 6] == 5);			//remapped middle (row 5, column 8)
}
```

Register it in `selfTest()` after `consoleRaise();`: `frames();`.

- [ ] **Step 2: Run to verify it fails**

Run: `cmd //c "tests\\selection_ext_test.bat"`
Expected: compile errors, `nineSliceSource` and friends undefined.

- [ ] **Step 3: Declarations**

In `selection_ext.h`, after `layOutStatDataBin`:

```cpp
//The wireframe's frame (frame 0x0D of the race's cmdbtns.grp, which the
//game keeps at 0x68C1C0): tabs and arrows draw it stretched.
const u32 FRAME_WIDTH = 33;
const u32 FRAME_HEIGHT = 34;
//Decodes frame (BW's GRP run-length format) into out, FRAME_WIDTH x
//FRAME_HEIGHT, 0 where transparent. False if the GRP has no such frame.
bool decodeGrpFrame(const u8* grp, u32 frame, u8* out);
//Which column (or row) of a size-wide frame shows at x of a stretch length
//long: the 4 px corners as they are, the middle repeated.
u32 nineSliceSource(u32 x, u32 length, u32 size);
//Draws frame stretched to width x height at (x, y) of dst (clipped to
//dstWidth x dstHeight), each pixel through remap unless it is NULL. 0 is
//transparent.
void drawNineSlice(const u8* frame, u8* dst, u32 pitch, u32 dstWidth, u32 dstHeight,
                   s32 x, s32 y, u32 width, u32 height, const u8* remap);
```

- [ ] **Step 4: Implementation**

In `selection_ext_core.cpp`, after `layOutStatDataBin`:

```cpp
bool decodeGrpFrame(const u8* grp, u32 frame, u8* out) {
	if (frame >= *(const u16*)grp)
		return false;
	const u8* const entry = grp + 6 + 8 * frame;
	const u32 fx = entry[0], fy = entry[1], fw = entry[2], fh = entry[3];
	const u8* const data = grp + *(const u32*)(entry + 4);
	for (u32 i = 0; i < FRAME_WIDTH * FRAME_HEIGHT; i++)
		out[i] = 0;
	for (u32 row = 0; row < fh; row++) {
		const u8* p = data + *(const u16*)(data + 2 * row);
		u32 col = 0;
		while (col < fw) {
			const u8 code = *p++;
			u32 count;
			if (code & 0x80) {
				col += code & 0x7F;
				continue;
			}
			if (code & 0x40) {
				count = code & 0x3F;
				const u8 value = *p++;
				for (u32 i = 0; i < count; i++, col++)
					if (fx + col < FRAME_WIDTH && fy + row < FRAME_HEIGHT)
						out[(fy + row) * FRAME_WIDTH + fx + col] = value;
				continue;
			}
			count = code;
			for (u32 i = 0; i < count; i++, col++, p++)
				if (fx + col < FRAME_WIDTH && fy + row < FRAME_HEIGHT)
					out[(fy + row) * FRAME_WIDTH + fx + col] = *p;
		}
	}
	return true;
}

u32 nineSliceSource(u32 x, u32 length, u32 size) {
	const u32 corner = 4;
	if (x < corner)
		return x;
	if (x >= length - corner)
		return size - (length - x);
	return corner + (x - corner) % (size - 2 * corner);
}

void drawNineSlice(const u8* frame, u8* dst, u32 pitch, u32 dstWidth, u32 dstHeight,
                   s32 x, s32 y, u32 width, u32 height, const u8* remap) {
	for (u32 row = 0; row < height; row++) {
		const s32 ty = y + (s32)row;
		if (ty < 0 || ty >= (s32)dstHeight)
			continue;
		const u32 sourceRow = nineSliceSource(row, height, FRAME_HEIGHT);
		for (u32 col = 0; col < width; col++) {
			const s32 tx = x + (s32)col;
			if (tx < 0 || tx >= (s32)dstWidth)
				continue;
			u8 value = frame[sourceRow * FRAME_WIDTH + nineSliceSource(col, width, FRAME_WIDTH)];
			if (value == 0)
				continue;
			if (remap != NULL)
				value = remap[value];
			dst[ty * pitch + tx] = value;
		}
	}
}
```

- [ ] **Step 5: Run the host test and the build**

Run: `cmd //c "tests\\selection_ext_test.bat"` → Expected: `PASS`.
Run: `powershell -ExecutionPolicy Bypass -File tests/verify.ps1` → Expected: `plugin build succeeded`.

- [ ] **Step 6: Commit**

```bash
git add GPTP/SCBW/selection_ext.h GPTP/SCBW/selection_ext_core.cpp GPTP/hooks/selection_ext/sel_selftest.cpp
git commit -m "feat: decode the wireframe frame and draw it stretched (pure)"
```

---

### Task 4: The taller box in game (console art, StatFluf, StatData)

**Files:**
- Modify: `GPTP/hooks/interface/resolution_hud.cpp` (`buildFlufTable`, `placeDialog`, `widenConsoleImage`, `MAX_FLUF_PIECES`)

**Interfaces:**
- Consumes: `consoleraise::raiseArt`, `consoleraise::raisePiece`, `consoleraise::SPANS`, `consoleraise::CUT_ROW` (Task 2); `selext::PANEL_RAISE`, `selext::layOutStatDataBin` (Task 1).
- Produces: in game, a StatData root 24 px higher and taller with its controls laid out; raised art and StatFluf pieces.

No host test can reach these game structures; the pure parts are tested in Tasks 1-2. This task's check is the build, then the user's round 11.

- [ ] **Step 1: Includes and the switch**

At the top of `resolution_hud.cpp`, after `#include <SCBW/api.h>`:

```cpp
#include <SCBW/console_raise.h>
#include <SCBW/selection_ext.h>
```

After `s32 noOffset = 0;` add:

```cpp
//The selection box is raised for the taller selection panel (Console art
//reference in docs/resolution.md). A replacement console drawn with a tall
//box already turns this off, and only the panel moves.
const bool RAISE_SELECTION_BOX = true;
```

- [ ] **Step 2: Split and grow the StatFluf pieces**

Change `const u32 MAX_FLUF_PIECES = 31;` to `const u32 MAX_FLUF_PIECES = 47;` (raised pieces split in up to three).

In `buildFlufTable`, replace the loop

```cpp
  for (const FlufEntry* piece = (const FlufEntry*)flufVanillaTables[race]; piece->x != 0xFFFF; ++piece) {
    const s32 left = mapConsoleX(piece->x, race);
    const s32 lastX = piece->x + piece->width - 1;
```

down to its closing brace, with a loop that first raises each piece:

```cpp
  for (const FlufEntry* vanilla = (const FlufEntry*)flufVanillaTables[race]; vanilla->x != 0xFFFF; ++vanilla) {
    consoleraise::Piece raised[3];
    const consoleraise::Piece whole = { vanilla->x, vanilla->y, vanilla->x + vanilla->width, vanilla->y + vanilla->height };
    u32 count = 1;
    raised[0] = whole;
    if (RAISE_SELECTION_BOX)
      count = consoleraise::raisePiece(whole, consoleraise::SPANS[race], consoleraise::CUT_ROW,
                                       selext::PANEL_RAISE, raised);
    for (u32 i = 0; i < count; ++i) {
      FlufEntry pieceEntry;
      pieceEntry.x = (u16)raised[i].left;
      pieceEntry.y = (u16)raised[i].top;
      pieceEntry.width = (u16)(raised[i].right - raised[i].left);
      pieceEntry.height = (u16)(raised[i].bottom - raised[i].top);
      const FlufEntry* const piece = &pieceEntry;
      const s32 left = mapConsoleX(piece->x, race);
      const s32 lastX = piece->x + piece->width - 1;
```

Keep the rest of the old body unchanged (the `right`, `top`, `bottom`, card cut and `addFluf` calls), then close the inner `for` with one more `}`. The old body's `continue;` now continues the inner loop, which is what it should do.

- [ ] **Step 3: Grow StatData and lay out its controls**

In `placeDialog`, replace

```cpp
    placedMask |= 1u << (&placement - panelPlacements);
    offsetBounds(root, dx, dy, *placement.dw);
    return;
```

with

```cpp
    placedMask |= 1u << (&placement - panelPlacements);
    offsetBounds(root, dx, dy, *placement.dw);
    if (strcmp(name, "StatData") == 0)
      raiseStatData(root, base);
    return;
```

and add above `placeDialog`:

```cpp
//The taller selection panel: StatData starts PANEL_RAISE higher and is as
//much taller (the root stores height - 1 where bottom goes, and the height
//again at 0x38); its controls are laid out by the selection code.
void raiseStatData(u8* root, u8* base) {
  s16* bounds = (s16*)(root + DLG_BOUNDS);
  bounds[1] = (s16)(bounds[1] - selext::PANEL_RAISE);
  bounds[3] = (s16)(bounds[3] + selext::PANEL_RAISE);
  *(u16*)(root + DLG_WIDTH + 2) = (u16)(*(u16*)(root + DLG_WIDTH + 2) + selext::PANEL_RAISE);
  selext::layOutStatDataBin(base);
}
```

- [ ] **Step 4: Raise the art before widening it**

In `widenConsoleImage`, right after the `if (wide == NULL) return;` lines, add:

```cpp
  //Step 3 of the console art (docs/resolution.md, Console art reference):
  //raise the selection box, in the vanilla art, so the widening below
  //copies the raised box and its strip like any other art.
  if (RAISE_SELECTION_BOX)
    consoleraise::raiseArt(consoleImage->pixels, 640, 480, consoleraise::SPANS[layoutRace],
                           consoleraise::CUT_ROW, selext::PANEL_RAISE);
```

- [ ] **Step 5: Build**

Run: `powershell -ExecutionPolicy Bypass -File tests/verify.ps1`
Expected: `plugin build succeeded`.

- [ ] **Step 6: Commit**

```bash
git add GPTP/hooks/interface/resolution_hud.cpp
git commit -m "feat: raise the selection box 24 px in the console art, StatFluf and StatData"
```

---

### Task 5: Page tabs and arrows in game

**Files:**
- Modify: `GPTP/hooks/selection_ext/sel_panel.cpp`, `GPTP/hooks/selection_ext/sel_panel.h`
- Modify: `GPTP/hooks/selection_ext/sel_inject.cpp` (tooltip stub, interact table already sized by `interactTable()`)
- Modify: `tests/check_naked_wrappers.py` (no change expected; the stub touches no frame)
- Modify: `GPTP/SCBW/selection_ext.h`, `GPTP/SCBW/selection_ext_core.cpp` (`panelFileOutdated`)
- Test: `GPTP/hooks/selection_ext/sel_selftest.cpp`

**Interfaces:**
- Consumes: everything from Tasks 1 and 3.
- Produces: `bool panelFileOutdated(u32 dialogWidth, u32 wireframeControls, bool pageControlsPresent)`; in game, the shown, drawn and clickable page controls.

- [ ] **Step 1: Failing test for the warning rule**

In `pages()` add:

```cpp
	CHECK(panelFileOutdated(910, 90, false));		//today's repack: no page buttons
	CHECK(panelFileOutdated(910, 12, true));
	CHECK(!panelFileOutdated(910, WIREFRAME_MAX, true));
```

Run: `cmd //c "tests\\selection_ext_test.bat"` → Expected: compile error (`panelFileOutdated`).

- [ ] **Step 2: The rule**

In `selection_ext.h` after `wireframesMissing`:

```cpp
//Whether statdata.bin is older than the panel: too few wireframes for a
//dialog this wide, or no page controls.
bool panelFileOutdated(u32 dialogWidth, u32 wireframeControls, bool pageControlsPresent);
```

In `selection_ext_core.cpp` after `wireframesMissing`:

```cpp
bool panelFileOutdated(u32 dialogWidth, u32 wireframeControls, bool pageControlsPresent) {
	return wireframesMissing(dialogWidth, wireframeControls) || !pageControlsPresent;
}
```

Run: `cmd //c "tests\\selection_ext_test.bat"` → Expected: `PASS`.

- [ ] **Step 3: Collect, show and redraw the page controls**

In `sel_panel.cpp`:

Add includes: `#include <SCBW/api.h>` is already pulled by sel_panel.h; add `#include <graphics/Font.h>` and `#include <graphics/Bitmap.h>`.

Replace `u32 interact[WIREFRAME_FIRST_ID - 1 + WIREFRAME_MAX];` with `u32 interact[PANEL_LAST_ID];`.

Replace `bool missingWarned;				//this game` with `bool outdatedWarned;			//this game`, and in `reset()` replace `missingWarned = false;` with `outdatedWarned = false;`.

After `u16 shownId[WIREFRAME_MAX];` add:

```cpp
//The page controls as last drawn, to redraw them when these change.
u32 drawnPage = 0xFFFFFFFF;
u32 drawnPages;
PageControls drawnMode;

//The current-page tab (0xA5 brighter, the fill lighter) and greyed arrows.
u8 litRemap[256];
u8 greyRemap[256];
bool remapsBuilt;

//Frame 0x0D of the GRP at [0x68C1C0], decoded once per GRP.
const u8* const* const CMDBTNS_GRP = (const u8**)0x0068C1C0;
const u32 WIREFRAME_FRAME = 0x0D;
const u8* decodedFrom;
u8 frame[FRAME_WIDTH * FRAME_HEIGHT];
bool frameOk;
```

Replace `collectWireframes` with:

```cpp
//The dialog's panel controls by id; returns how many wireframes there are
//(the first gap ends them). Missing tabs and arrows are NULL.
u32 collectControls(BinDlg* dialog, BinDlg** wireframes, BinDlg** tabs, BinDlg** arrows) {
	for (u32 k = 0; k < WIREFRAME_MAX; k++)
		wireframes[k] = NULL;
	for (u32 t = 0; t < PAGE_TABS; t++)
		tabs[t] = NULL;
	for (u32 a = 0; a < 3; a++)
		arrows[a] = NULL;
	for (BinDlg* control = firstChild(dialog); control != NULL; control = control->next) {
		const s32 id = control->index;
		if (id >= (s32)WIREFRAME_FIRST_ID && id < (s32)PAGE_TAB_FIRST_ID)
			wireframes[id - WIREFRAME_FIRST_ID] = control;
		else if (id >= (s32)PAGE_TAB_FIRST_ID && id < (s32)PAGE_UP_ID)
			tabs[id - PAGE_TAB_FIRST_ID] = control;
		else if (id >= (s32)PAGE_UP_ID && id <= (s32)PAGE_DOWN_ID)
			arrows[id - PAGE_UP_ID] = control;
	}
	u32 n = 0;
	while (n < WIREFRAME_MAX && wireframes[n] != NULL)
		n++;
	return n;
}

//Shows the tabs or the arrows for the selection's pages, and redraws them
//when the page, the page count or the kind of control changes.
void updatePageControls(BinDlg** tabs, BinDlg** arrows) {
	const u32 pages = pageCountFor(clientCount, pageSize);
	const PageControls mode = pageControlsFor(pages);
	const bool redraw = pages != drawnPages || selectionPage != drawnPage || mode != drawnMode;
	for (u32 t = 0; t < PAGE_TABS; t++) {
		if (tabs[t] == NULL)
			continue;
		if (mode == PAGE_CONTROLS_TABS && t < pages) {
			selexe::showControl(tabs[t]);
			if (redraw)
				selexe::invalidateControl(tabs[t]);
		}
		else
			selexe::hideControl(tabs[t]);
	}
	for (u32 a = 0; a < 3; a++) {
		if (arrows[a] == NULL)
			continue;
		if (mode == PAGE_CONTROLS_ARROWS) {
			selexe::showControl(arrows[a]);
			if (redraw)
				selexe::invalidateControl(arrows[a]);
		}
		else
			selexe::hideControl(arrows[a]);
	}
	drawnPages = pages;
	drawnPage = selectionPage;
	drawnMode = mode;
}
```

In `fill()`, replace

```cpp
	static BinDlg* wireframes[WIREFRAME_MAX];
	const u32 controls = collectWireframes(dialog, wireframes);
	pageSize = pageSizeFor(dialog->bounds.width, controls);
	if (!missingWarned && wireframesMissing(dialog->bounds.width, controls)) {
		static char text[96];
		sprintf_s(text, sizeof(text), PLUGIN_NAME ": statdata.bin not repacked: %u wireframes", controls);
		scbw::printText(text, GameTextColor::Yellow);
		missingWarned = true;
	}
```

with

```cpp
	static BinDlg* wireframes[WIREFRAME_MAX];
	static BinDlg* tabs[PAGE_TABS];
	static BinDlg* arrows[3];
	const u32 controls = collectControls(dialog, wireframes, tabs, arrows);
	pageSize = pageSizeFor(dialog->bounds.width, controls);
	if (!outdatedWarned && panelFileOutdated(dialog->bounds.width, controls, arrows[2] != NULL)) {
		static char text[112];
		sprintf_s(text, sizeof(text), PLUGIN_NAME ": statdata.bin not repacked or out of date: %u wireframes%s",
		          controls, arrows[2] != NULL ? "" : ", no page buttons");
		scbw::printText(text, GameTextColor::Yellow);
		outdatedWarned = true;
	}
```

and at the end of `fill()`, after `for (; k < controls; k++) selexe::hideControl(wireframes[k]);`, add `updatePageControls(tabs, arrows);`.

In `keyDown`, the Tab branch and the PgUp/PgDn branch already set `selectionPage` and request a refresh; the redraw follows from `updatePageControls`.

- [ ] **Step 4: Draw and click**

Add in the unnamed namespace of `sel_panel.cpp`, after `updatePageControls`:

```cpp
struct Surface {
	u16 width;
	u16 height;
	u8* data;
};
//The bitmap a dialog draw proc draws on (0x41C1DF sets it).
Surface* const* const DRAW_SURFACE = (Surface**)0x006CF4A8;

void buildRemaps() {
	for (u32 i = 0; i < 256; i++)
		litRemap[i] = greyRemap[i] = (u8)i;
	litRemap[0xA5] = 0x7E;
	litRemap[0x29] = 0xA0;
	litRemap[0x2A] = 0xA0;
	greyRemap[0xA5] = 0x91;
	greyRemap[0xA0] = 0x43;
	remapsBuilt = true;
}

bool frameReady() {
	const u8* const grp = *CMDBTNS_GRP;
	if (grp == NULL)
		return false;
	if (grp != decodedFrom) {
		frameOk = decodeGrpFrame(grp, WIREFRAME_FRAME, frame);
		decodedFrom = grp;
	}
	return frameOk;
}

//A triangle 11 px wide, 6 tall, pointing up or down, centred on cx.
void drawTriangle(Surface* surface, s32 cx, s32 top, bool up, u8 colour) {
	for (s32 i = 0; i < 6; i++) {
		const s32 y = top + (up ? i : 5 - i);
		if (y < 0 || y >= surface->height)
			continue;
		for (s32 x = cx - i; x <= cx + i; x++)
			if (x >= 0 && x < surface->width)
				surface->data[y * surface->width + x] = colour;
	}
}

void drawCentredText(Surface* surface, const BinDlg* control, const char* text) {
	const s32 width = graphics::Font::getTextWidth(text, 0);
	const s32 height = graphics::Font::getTextHeight(text, 0);
	const s32 x = control->bounds.left + (control->bounds.right - control->bounds.left + 1 - width) / 2;
	const s32 y = control->bounds.top + (control->bounds.bottom - control->bounds.top + 1 - height) / 2;
	((graphics::Bitmap*)surface)->blitString(text, x, y, 0);
}

//The draw proc of the tabs and arrows (called like 0x456F50: ecx the
//control, two stack arguments, ret 8).
void __fastcall pageButtonDraw(BinDlg* control, u32, u32, void*) {
	Surface* const surface = *DRAW_SURFACE;
	if (surface == NULL || !frameReady())
		return;
	if (!remapsBuilt)
		buildRemaps();
	const s32 id = control->index;
	const s32 left = control->bounds.left, top = control->bounds.top;
	const u32 width = control->bounds.right - control->bounds.left + 1;
	const u32 height = control->bounds.bottom - control->bounds.top + 1;
	const u32 pages = pageCountFor(clientCount, pageSize);
	if (id == (s32)PAGE_LABEL_ID) {
		static char text[16];
		sprintf_s(text, sizeof(text), "\x04%u/%u", selectionPage + 1, pages);
		drawCentredText(surface, control, text);
		return;
	}
	if (id == (s32)PAGE_UP_ID || id == (s32)PAGE_DOWN_ID) {
		const bool up = id == (s32)PAGE_UP_ID;
		const bool atEnd = up ? selectionPage == 0 : selectionPage + 1 >= pages;
		drawNineSlice(frame, surface->data, surface->width, surface->width, surface->height,
		              left, top, width, height, atEnd ? greyRemap : NULL);
		drawTriangle(surface, left + (s32)width / 2, top + 4, up, atEnd ? 0x4A : 0x54);
		return;
	}
	const u32 tab = id - PAGE_TAB_FIRST_ID;
	const bool lit = tab == selectionPage;
	drawNineSlice(frame, surface->data, surface->width, surface->width, surface->height,
	              left, top, width, height, lit ? litRemap : NULL);
	static char text[8];
	sprintf_s(text, sizeof(text), lit ? "\x07%u" : "\x04%u", tab + 1);
	drawCentredText(surface, control, text);
}

void pageButtonClicked(s32 id) {
	const u32 pages = pageCountFor(clientCount, pageSize);
	u32 page = selectionPage;
	if (id >= (s32)PAGE_TAB_FIRST_ID && id < (s32)PAGE_UP_ID)
		page = pageAfterTab(id - PAGE_TAB_FIRST_ID, selectionPage, pages);
	else if (id == (s32)PAGE_UP_ID || id == (s32)PAGE_DOWN_ID)
		page = pageAfterArrow(selectionPage, pages, id == (s32)PAGE_UP_ID);
	if (page == selectionPage)
		return;
	selectionPage = page;
	*REFRESH_STAT_DATA = 1;
}

//The interact proc of the tabs and arrows, called like 0x4583E0: ecx the
//control, edx the event (+0x0C its number, 0x0E a user event; +0 the user
//event's kind: 0 create, 2 activate). Anything else goes to the default
//handler of the control's type, as 0x4583E0 does.
const u16 EVENT_USER = 0x0E;
const u32 USER_CREATE = 0;
const u32 USER_ACTIVATE = 2;
typedef u32 (__fastcall* DialogHandler)(BinDlg* control, u8* event);
const DialogHandler* const DEFAULT_HANDLERS = (const DialogHandler*)0x005014AC;

u32 __fastcall pageButtonInteract(BinDlg* control, u8* event) {
	if (*(const u16*)(event + 0x0C) == EVENT_USER) {
		const u32 kind = *(const u32*)event;
		if (kind == USER_CREATE)
			control->fxnUpdate = (void*)pageButtonDraw;
		else if (kind == USER_ACTIVATE) {
			pageButtonClicked(control->index);
			return 1;
		}
	}
	return DEFAULT_HANDLERS[control->controlType](control, event);
}
```

In `interactTable()`, after the wireframe loop, add:

```cpp
		for (u32 id = PAGE_TAB_FIRST_ID; id <= PANEL_LAST_ID; id++)
			interact[id - 1] = (u32)pageButtonInteract;
```

and change the wireframe loop's bound from `id < WIREFRAME_FIRST_ID + WIREFRAME_MAX` to `id < PAGE_TAB_FIRST_ID`.

- [ ] **Step 5: Tooltips for ids past 0x7F**

In `sel_inject.cpp`, add before `injectSelectionPanelHooks`:

```cpp
//0x457D7B in the StatData tooltip proc: cmp cx, 0x2C; jg 0x457DB6 (6
//bytes), the last wireframe id as a sign-extended imm8, which cannot reach
//past 0x7F. The ids are compared in full here.
static_assert(selext::WIREFRAME_FIRST_ID + selext::WIREFRAME_MAX - 1 == 0xB0,
              "wireframeTooltipStub compares with 0xB0");
const u32 TooltipWireframe = 0x00457D81;
const u32 TooltipOther = 0x00457DB6;
void __declspec(naked) wireframeTooltipStub() {
	__asm {
		CMP CX, 0xB0
		JG other
		JMP TooltipWireframe
	other:
		JMP TooltipOther
	}
}
```

In `injectSelectionPanelHooks`, replace the two lines

```cpp
	//Tooltips: replaced by wireframeTooltipStub in Task 5 (ids past 0x7F).
	memoryPatch(0x00457D7E, (u8)0x7F);
```

with

```cpp
	//Tooltips for every wireframe id.
	jmpPatch(wireframeTooltipStub,	0x00457D7B, 1);
```

- [ ] **Step 6: Build and run every check**

Run: `powershell -ExecutionPolicy Bypass -File tests/verify.ps1`
Expected: `naked wrappers: ok` and `plugin build succeeded`. If the naked check names `wireframeTooltipStub`, it touched the frame; it must not (no `ebp` in it).

- [ ] **Step 7: Commit**

```bash
git add GPTP/hooks/selection_ext/sel_panel.cpp GPTP/hooks/selection_ext/sel_panel.h GPTP/hooks/selection_ext/sel_inject.cpp GPTP/SCBW/selection_ext.h GPTP/SCBW/selection_ext_core.cpp GPTP/hooks/selection_ext/sel_selftest.cpp
git commit -m "feat: page tabs and arrows on the selection panel; tooltips past id 0x7F"
```

---

### Task 6: The new statdata.bin

**Files:**
- Modify: `D:\SC Modding\SCManifold\to-repack\make_statdata_wide.py` (outside the repo; back it up first)
- Output: `D:\SC Modding\SCManifold\to-repack\rez\statdata.bin`

**Interfaces:**
- Consumes: the ids of Task 1 (`WIREFRAME_MAX` 144, tabs 0xB1-0xC0, 0xC1-0xC3).
- Produces: a `statdata.bin` with 144 wireframes and 19 page controls, placeholder rects (the plugin lays them out).

- [ ] **Step 1: Back up**

```bash
cp "D:/SC Modding/SCManifold/to-repack/make_statdata_wide.py" "D:/SC Modding/SCManifold/to-repack/make_statdata_wide.py.bak-20261004"
cp "D:/SC Modding/SCManifold/to-repack/rez/statdata.bin" "D:/SC Modding/SCManifold/to-repack/rez/statdata.bin.bak-20261004"
```

- [ ] **Step 2: Rewrite the generator**

Replace the file with:

```python
# Builds rez\statdata.bin for the GPTP plugin's selection panel pages
# (docs/superpowers/specs/2026-10-04-selection-panel-pages-design.md).
# Vanilla has 12 wireframes (ids 33-44, the last 12 entries). This appends
# wireframes up to WIREFRAME_MAX, then 16 page tabs, then the up arrow, the
# page number and the down arrow, all copies of entry 44 (a button), linked
# after it, so no existing offset moves. Their rects are placeholders: the
# plugin lays the panel out as the file loads (selext::layOutStatDataBin).
# The ids must match selection_ext.h: WIREFRAME_FIRST_ID 0x21,
# WIREFRAME_MAX 144, PAGE_TAB_FIRST_ID 0xB1, PAGE_UP_ID 0xC1,
# PAGE_LABEL_ID 0xC2, PAGE_DOWN_ID 0xC3.
# Ship it only with a plugin that has the panel pages: the vanilla exe has no
# handler for ids above 44 and crashes.
#
# Run with Python 2.7 from this folder: python make_statdata_wide.py
import struct

SOURCE = '../unpacked/rez/statdata.bin'
OUT = 'rez/statdata.bin'
ENTRY = 0x56
LAST_VANILLA = 0x1326      # entry of id 44, the list's tail
FIRST_ID = 0x21
WIREFRAME_MAX = 144
PAGE_TABS = 16
PAGE_CONTROLS = 3          # up arrow, page number, down arrow

d = bytearray(open(SOURCE, 'rb').read())
assert struct.unpack_from('<I', d, LAST_VANILLA)[0] == 0
assert struct.unpack_from('<h', d, LAST_VANILLA + 0x20)[0] == 44
template = d[LAST_VANILLA:LAST_VANILLA + ENTRY]
previous = LAST_VANILLA
last_id = FIRST_ID + WIREFRAME_MAX + PAGE_TABS + PAGE_CONTROLS - 1
for control_id in range(45, last_id + 1):
    entry = bytearray(template)
    struct.pack_into('<I', entry, 0, 0)
    struct.pack_into('<h', entry, 0x20, control_id)
    offset = len(d)
    d += entry
    struct.pack_into('<I', d, previous, offset)
    previous = offset
open(OUT, 'wb').write(d)
print 'ok: %d wireframes, %d page controls, last id 0x%X, %d bytes' % (
    WIREFRAME_MAX, PAGE_TABS + PAGE_CONTROLS, last_id, len(d))
```

- [ ] **Step 3: Run it**

Run: `cd "D:/SC Modding/SCManifold/to-repack" && /c/Python27/python make_statdata_wide.py`
Expected: `ok: 144 wireframes, 19 page controls, last id 0xC3, 17983 bytes`.

- [ ] **Step 4: Check the file**

Run:

```bash
cd "D:/SC Modding/SCManifold/to-repack" && /c/Python27/python -c "
import struct
d=open('rez/statdata.bin','rb').read()
o=struct.unpack_from('<I',d,0x42)[0]; ids=[]
while o: ids.append(struct.unpack_from('<h',d,o+0x20)[0]); o=struct.unpack_from('<I',d,o)[0]
print(len(ids), ids[-20:], min(i for i in ids if i>=33), max(ids))"
```

Expected: `(208, [...], 33, 195)` with the last 20 ids ending `..., 193, 194, 195`, and every id from 33 to 195 present once.

No commit (outside the repo). The user repacks `to-repack\rez\statdata.bin` as `rez\statdata.bin`.

---

### Task 7: Documentation, build stamp and the test round

**Files:**
- Modify: `docs/resolution.md` (§5 Console art reference, §6 TODO)
- Modify: `docs/superpowers/specs/2026-10-04-selection-panel-pages-design.md` (status line)
- Modify: `GPTP/hooks/main/game_hooks.cpp` (touch only, for the build stamp)

**Interfaces:**
- Consumes: the values of Tasks 2 and 4 (spans, cut row, raise, the step order in `widenConsoleImage`).

- [ ] **Step 1: Console art reference**

In `docs/resolution.md`, after the end of §5 (before `## 6.`), add a section with this content (fill nothing in later; every value is final):

```markdown
### Console art reference

What the plugin does to the console art, for anyone replacing it (an SC2
console, say) without reading the code. Code: `hooks/interface/resolution_hud.cpp`
and `SCBW/console_raise.cpp`.

**The image.** `game\<race>console.pcx` (`z`, `t`, `p`; `n` in replays),
640x480, no palette of its own: pixel values are indices into the in-game
palette, 0 is transparent. Loaded at 0x4C3950, kept at 0x597240
({u16 width, u16 height, u8* pixels}). After it loads (0x4C39F5),
`widenConsoleImage()` rebuilds it at the full screen size in three steps, in
this order:

1. **Raise the selection box** (`RAISE_SELECTION_BOX`), on the vanilla
   640x480 art. Per race, the columns of `consoleraise::SPANS` (Zerg 138-408,
   Terran 143-407, Protoss 143-408, replays 150-405): every row above row 420
   moves up 24 px, and rows 396-419 repeat row 420. Every span lies inside
   StatData's vanilla columns [138, 408). Seams where raised columns meet the
   rest: Protoss's gold bar and Zerg's tube on the right, Zerg's left wall,
   the replay console's left slope.
2. **Widen.** Columns left of the race's split (`consoleStretch`: 270 for
   Zerg, Terran and replays, 210 for Protoss) go to the bottom-left corner,
   the rest to the bottom-right one. The gap is filled with pairs of the
   40 px strip that starts at the split: a copy, then a mirrored copy,
   narrowed so a whole number of pairs fits and each pair ends on the split
   column.
3. **Dense command card** (not in replays). The vanilla 3x3 frame (cells
   46 x 40 apart from (496,354)) is rebuilt 5x3 with 36x34 buttons touching:
   196x114, flush with the bottom-right corner (`cardSourceColumn`,
   `cardSourceRow`); the portrait part moves left by the difference.

**What reads the image.**
- Each panel dialog copies its background out of it at its own rect
  (0x4C35F0): Minimap, StatData, StatPort, StatBtn, StatRes, Stat_F10,
  TextBox, placed by `panelPlacements`. StatData is also widened by the gap,
  raised 24 px and made 24 px taller (`raiseStatData`).
- The decorative pieces (StatFluf) paint the parts no panel covers. The
  plugin gives each race its own table (`buildFlufTable`): each vanilla piece
  is split at the raised span's edges with the inside part grown 24 px up,
  mapped like the art, and cut around the dense card.
- The console's transparency mask (0x41D640) and the hit test lines
  (0x4D11A0) are built from the image, so they follow by themselves.

**A replacement console must provide**, at the full screen size or as
640x480 art for the steps above:
- the in-game palette's indices, 0 for transparent;
- every panel's area at the place `panelPlacements` puts it, and the
  selection box with its black area at StatData's rect: 24 px above
  vanilla's 388, 116 tall, as wide as the gap allows;
- the command card's 196x114 frame at the bottom-right corner;
- art for every visible pixel that no panel covers, inside a StatFluf
  piece's rect (or new pieces in `buildFlufTable`).
Art drawn at the full width with its own tall box skips steps 1-3:
`RAISE_SELECTION_BOX` off, and the widening and card rebuild replaced by a
plain copy.
```

- [ ] **Step 2: TODO and spec status**

In `docs/resolution.md` §6, replace the item that starts `- **Fold in page buttons and a page indicator**` with:

```markdown
- **Page buttons and a page indicator** `[BUILT 2026-10-04]`: the selection box is
  raised 24 px; 3 gapless rows; tabs 2 x 8 on the left, arrows past 16 pages
  (spec `docs/superpowers/specs/2026-10-04-selection-panel-pages-design.md`).
  Needs the new `rez\statdata.bin` (144 wireframes, 19 page controls) repacked.
```

In the spec's first lines, change `Status: design approved in chat 2026-10-04 (mock-up rounds 1-6), spec for review.` to `Status: built 2026-10-04 (plan docs/superpowers/plans/2026-10-04-selection-panel-pages.md); in-game round 11.`

- [ ] **Step 3: Build with a fresh stamp**

Run: `touch GPTP/hooks/main/game_hooks.cpp && powershell -ExecutionPolicy Bypass -File tests/verify.ps1`
Expected: `plugin build succeeded`; `GPTP.qdp` copied next to SCManifold.exe. Note the build time from `ls -la "D:/SC Modding/SCManifold/GPTP.qdp"` for the stamp.

- [ ] **Step 4: Commit**

```bash
git -c core.autocrlf=false add docs/resolution.md
git add docs/superpowers/specs/2026-10-04-selection-panel-pages-design.md docs/superpowers/plans/2026-10-04-selection-panel-pages.md
git commit -m "docs: console art reference; selection panel pages built"
```

- [ ] **Step 5: Hand the user round 11**

Tell the user: repack `GPTP.qdp` and `to-repack\rez\statdata.bin`, check the stamp, then run the spec's tests 11.1-11.10 (numbered, one message), including 11.10's colour pick for the current tab.
