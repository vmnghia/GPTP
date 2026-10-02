# Extended selection, stage 5 follow-up (panel highlight): implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** in the selection panel, the wireframes of units outside the active subgroup are drawn dimmed (option A, chosen by the user from mock-ups on 2026-10-02), so the active subgroup stands out as in SC2.

**Architecture:** the multi-selection wireframe draw proc 0x456F50 fills a colour remap (shields at 0x50CE81–82, the HP sections at 0x50CE91–94 and 0x50CE99–9C) from `game\twire.pcx`'s colours just before it draws the frame (0x40ABBE), and resets the entries after. A stub at 0x456FB6, between the fill and the draw, replaces those 10 entries with darker palette indices when the unit is outside the active subgroup. The darker index of each colour is the game palette entry (0x6CE320) nearest to 40% of its brightness, from a table rebuilt whenever the palette changes.

**Tech stack:** C++ (MSVC, Win32), GPTP hook tools, MSVC inline asm.

**Spec:** `docs/superpowers/specs/2026-09-30-extended-selection-design.md` ("Stage 5", What the player sees: "The active subgroup's wireframes are highlighted and the rest dimmed").

## Global constraints
- Local UI only; nothing here touches synced state.
- New naked stubs in `hooks/selection_ext/sel_inject.cpp`, statics/registers only; `tests/verify.ps1` after every task; files are stored LF, so plain `git add`.

## Verified facts (2026-10-02)
- 0x456F50 is the wireframe control's draw proc (set at 0x45841C by the wireframe handler 0x4583E0; no other user). ECX = control on entry; at 0x456F5E ESI = the unit (`[[control+0x26]]`), EDI = control.
- Colour fill: 0x4567C0 (0x50CE91–94 = `[0x68C20A]` first), 0x4566B0 (EDX = unit; shields 0x50CE81–82), 0x456730 (ECX = unit; 0x50CE99–9C by HP). The values are palette indices from the 24-byte table 0x68C208 (`game\twire.pcx`).
- 0x456FB6: `mov eax, [0x68C1FC]` (5 bytes, `A1 FC C1 68 00`), right after the last fill call and before the frame draw; ESI = unit there. 0x457048–0x457087 put the defaults back (D0–D3, C0–C1, D8–DB), so the dimmed values never outlive one draw.
- Game palette: `PALETTEENTRY[256]` at 0x6CE320 (BWAPI `GamePalette`).
- `selpanel::fill` invalidates a wireframe only when its flag bit 0 is clear, so a Tab (same units, same page) would not redraw them: it must invalidate the shown wireframes when the active subgroup changes.

## Review focus
1. **One subgroup, or one unit selected: nothing is dimmed.** Covered by `selsub::isDimmed` returning false (Task 2) and 6.2.
2. **Palette fades (game start, menus) must not leave a stale dark table.** Covered by rebuilding when the palette bytes change (Task 2).
3. **Tab redraws the panel at once.** Covered by the invalidate in `fill` (Task 2) and 6.1.
4. **Hallucinations and heroes keep their box graphics; only the wireframe colours change.** 6.3.

---

### Task 1: the dim colour (pure, host-tested)
**Files:** `GPTP/SCBW/selection_ext.h`, `selection_ext_core.cpp`, `hooks/selection_ext/sel_selftest.cpp`.

**Produces:**
```cpp
//The palette entry (rgbx, 4 bytes each, 256 of them) nearest by squared RGB
//distance to entry index scaled by percent; ties to the lower index.
u8 dimIndex(const u8* palette, u8 index, u32 percent);
```
- [ ] RED: declare it (after `betterButtonState`), stub `return index;`, and test:
```cpp
void dimColours() {
	static u8 pal[256 * 4];
	memset(pal, 0, sizeof(pal));
	//1: bright green, 2: dark green (40%), 3: mid green, 4: dark red
	pal[4 * 1 + 1] = 250;
	pal[4 * 2 + 1] = 100;
	pal[4 * 3 + 1] = 170;
	pal[4 * 4 + 0] = 100;
	CHECK(dimIndex(pal, 1, 40) == 2);
	CHECK(dimIndex(pal, 3, 40) == 2);	//68 green: 100 (distance 32) beats black (68)
	CHECK(dimIndex(pal, 0, 40) == 0);	//black stays black
	pal[4 * 5 + 1] = 100;	//a second dark green: the lower index wins
	CHECK(dimIndex(pal, 1, 40) == 2);
}
```
Host test: FAIL at the first `dimIndex` line.
- [ ] GREEN:
```cpp
u8 dimIndex(const u8* palette, u8 index, u32 percent) {
	const s32 r = palette[4 * index] * (s32)percent / 100;
	const s32 g = palette[4 * index + 1] * (s32)percent / 100;
	const s32 b = palette[4 * index + 2] * (s32)percent / 100;
	u32 best = 0, bestDistance = 0xFFFFFFFF;
	for (u32 i = 0; i < 256; i++) {
		const s32 dr = palette[4 * i] - r, dg = palette[4 * i + 1] - g, db = palette[4 * i + 2] - b;
		const u32 distance = (u32)(dr * dr + dg * dg + db * db);
		if (distance < bestDistance) {
			best = i;
			bestDistance = distance;
		}
	}
	return (u8)best;
}
```
Host test PASS; verify PASS; commit `feat: dim colour lookup for the panel highlight`.

### Task 2: dim the wireframes outside the active subgroup
**Files:** `hooks/selection_ext/sel_subgroups.h/.cpp`, `sel_panel.cpp`, `sel_inject.cpp`.

**Produces** (namespace `selsub`):
```cpp
//Whether a panel wireframe of unit is drawn dimmed: several subgroups are
//selected and unit is outside the active one.
bool isDimmed(CUnit* unit);
//Replaces the wireframe colour remap (0x50CE81-82, 0x50CE91-94,
//0x50CE99-9C) with its dimmed colours, if unit is dimmed.
void dimWireframe(CUnit* unit);
//Changes whenever the active subgroup does (for the panel's redraw).
u32 activeStamp();
```
- [ ] `sel_subgroups.cpp`:
```cpp
namespace {
u8* const WIRE_REMAP = (u8*)0x0050CE80;
const u8* const GAME_PALETTE = (const u8*)0x006CE320;	//PALETTEENTRY[256]
const u32 DIM_PERCENT = 40;
const u8 DIM_ENTRIES[] = { 0x01, 0x02, 0x11, 0x12, 0x13, 0x14, 0x19, 0x1A, 0x1B, 0x1C };
u8 paletteSeen[256 * 4];
u8 dimOf[256];
bool dimBuilt;
u32 stamp;
}
```
`isDimmed`: `clientCount > 1 && keyOf(unit) != activeKey && keys[0] != keys[clientCount - 1]` (more than one subgroup).
`dimWireframe`: return unless `isDimmed(unit)`; if `!dimBuilt || memcmp(paletteSeen, GAME_PALETTE, 1024)`: copy the palette, `dimOf[i] = dimIndex(paletteSeen, i, DIM_PERCENT)` for all 256, `dimBuilt = true`; then `WIRE_REMAP[e] = dimOf[WIRE_REMAP[e]]` for each entry in `DIM_ENTRIES`.
`activeStamp()` returns `stamp`; `stamp++` wherever `activeKey` changes (in `sortAndPickLeader` when the new key differs, and in `cycle`). `reset()` sets `dimBuilt = false`.
- [ ] `sel_panel.cpp` `fill`: keep `static u32 shownStamp;` — if `selsub::activeStamp() != shownStamp`, invalidate every shown wireframe (`selexe::invalidateControl(control)` for k < shown count, after the loop) and store the stamp.
- [ ] `sel_inject.cpp`:
```cpp
//0x456FB6 (wireframe draw proc, after its colour fill, before the frame is
//drawn): ESI = the unit. Dims the colours, then the replaced
//mov eax, [0x68C1FC], and back.
const u32 WireframeDrawBack = 0x00456FBB;
void __declspec(naked) wireframeDimStub() {
	static CUnit* unit;
	__asm {
		MOV unit, ESI
		PUSHAD
	}
	selsub::dimWireframe(unit);
	__asm {
		POPAD
		MOV EAX, 0x0068C1FC
		MOV EAX, [EAX]
		JMP WireframeDrawBack
	}
}
```
and in `injectCommandCardHooks()`: `jmpPatch(wireframeDimStub, 0x00456FB6, 0);`.
- [ ] verify PASS (naked check included); commit `feat: the panel dims wireframes outside the active subgroup`.

### Task 3: in-game test round (appended to stage 5's)
- 6.1 Mixed army: the active subgroup's wireframes are bright, the rest dimmed; Tab moves the bright block at once, including across pages.
- 6.2 One type selected, or one unit: nothing dimmed. Damaged units keep their yellow/red colours (dimmed when inactive).
- 6.3 Hallucinations and heroes keep their own box graphics.
- 6.4 Start a game, open and close the menu, load a save: the dim colours stay right (no wrong palette after fades).
