# Selection panel pages: taller box, 3 gapless rows, page tabs

Status: built 2026-10-04 (plan docs/superpowers/plans/2026-10-04-selection-panel-pages.md); in-game round 11.
Follows stage 3 of `2026-09-30-extended-selection-design.md`, which built the
pages (Ctrl+PgUp/PgDn only) on vanilla's 2 rows.

## 1. What the user chose

The final mock-up ("E", round 6), at 1280x720:

- The selection box is **raised 24 px**. The minimap, portrait and command card
  stay where they are.
- **Three rows of full-size wireframes with no gaps**: boxes 33x34, edge to
  edge (33 px across, 34 px down). 75 to a page at 1280x720.
- **Page tabs on the left**: 2 columns x 8 rows, numbered 1-16, column-major
  (1-8 down the first column, 9-16 down the second). The tab column's top and
  bottom are level with the grid's top and bottom.
- **More than 16 pages: arrows instead of tabs**. A ▲ button level with the
  grid's top, a ▼ button level with its bottom, both 15 px tall, and the page
  number ("9/23") between them.
- **Tabs and arrows reuse the wireframe's frame** (section 5). The current
  page's tab has a brighter middle line, a lighter fill and a green number.
- **The console art is raised by the plugin** (option A of 2026-10-04): no art
  edits from the user. Protoss and Zerg show small seams where the raised part
  meets the unraised art; accepted for now, since the user may replace the
  consoles with SC2 art later. That is why section 7 documents the console
  art in full.

## 2. Layout

Coordinates are relative to the StatData root dialog after the raise (its top
is 24 px higher and it is 24 px taller: 116 instead of 92).

| Part | x | y | Size |
|---|---|---|---|
| Wireframe grid | 58 + 33·column | 8 + 34·row (rows 0-2) | 33 x 34 each |
| Tabs, column c (0-1), row r (0-7) | 14 + 20·c | 8 + E(r) | 20 x (E(r+1) − E(r)) |
| ▲ arrow | 14 | 8 | 40 x 15 |
| Page number | 14 | between the arrows | 40 wide, centred |
| ▼ arrow | 14 | 8 + 102 − 15 = 95 | 40 x 15 |

- E(r) = round(102·r / 8): 0, 13, 26, 38, 51, 64, 77, 89, 102. So the tabs are
  13 or 12 px tall and together exactly as tall as the grid (3 x 34 = 102).
- **Wireframes per page** = 3 x columns, at most the wireframe controls
  present. Columns = (dialog width − 10 − 58 − 33) / 33 + 1 (the last box ends
  at least 10 px from the dialog's right edge, inside the box art):

  | Screen width | StatData width | Columns | Per page | Pages for 400 |
  |---|---|---|---|---|
  | 640 | 270 | 6 | 18 | 23 (arrows) |
  | 1280 | 910 | 25 | 75 | 6 (tabs) |
  | 1920 | 1550 | 44 | 132 | 4 (tabs) |
  | 2048 (max) | 1678 | 48 | 144 | 3 (tabs) |

  So `WIREFRAME_MAX` becomes **144**.
- **The vanilla single-unit display** (name, portrait-side text, hit points,
  queue, progress bar; every vanilla control of StatData other than the
  wireframes) moves down **12 px**, so it sits centred in the taller box.
  Ruling to confirm in game: if it looks wrong, 0 (top-aligned) or 24 (at its
  vanilla screen position) are one constant away.

## 3. Behaviour

- **Tabs.** Shown when the selection spans 2-16 pages; one tab per page. A
  left click shows that page. No tabs with a single page (nothing to choose),
  nor while one unit is selected (the single-unit display is up).
- **Arrows.** Shown instead of tabs at 17 pages or more. ▲ shows the previous
  page, ▼ the next one. At the first page ▲ is greyed and does nothing; at the
  last page ▼ is. The page number reads "page/pages".
- **The current page** is the panel's existing local `selectionPage`. Tabs,
  arrows, Ctrl+PgUp/PgDn and Tab's jump to the active subgroup's page all set
  it; the tabs and arrows redraw when it changes.
- **Local UI only.** Page buttons change nothing synced (no commands sent),
  like Ctrl+PgUp/PgDn today.
- **Tooltips.** Wireframes keep vanilla's unit-name tooltips on every id.
  Tabs and arrows have none.
- **Hover and press.** Tabs and arrows give the same click sound as the
  wireframes (the dialog's default); no hover change, as wireframes have none.

## 4. statdata.bin and the panel's controls

- **The generator** (`SCManifold\to-repack\make_statdata_wide.py`) appends
  controls to vanilla's file and links them after its last entry, as today:
  - wireframes up to `WIREFRAME_MAX` = 144 (ids 0x21-0xB0);
  - 16 tabs (ids 0xB1-0xC0);
  - the ▲ arrow, the page number and the ▼ arrow (ids 0xC1-0xC3).
  It writes placeholder rects only. The file is then repacked by the user, as
  `rez\statdata.bin`.
- **The plugin lays the panel out** as the `.bin` loads (the existing
  `placeDialog` at 0x4194E0, before offsets become pointers): the root moves
  up 24 and grows 24 taller (plus the existing width growth); every vanilla
  control moves down 12; the wireframes, tabs and arrows get the rects of
  section 2. The layout lives in one place, `selection_ext_core.cpp`, as pure
  functions the host test covers, so the generator and the plugin cannot
  disagree about positions.
- **Interact table.** The existing table (vanilla's 44 entries, then the
  wireframe handler 0x4583E0) grows to cover the new ids; tabs and arrows get
  the plugin's own interact proc: `__fastcall (BinDlg* control, event*)`,
  the convention 0x4583E0 uses (event number at +0x0C; 0x0E = user event,
  subtype at +0: 0 create, 1 destroy, 2 activate). At create it sets the
  control's draw proc (+0x2E) to the plugin's; at activate it changes the
  page; anything else goes to the default handling, as 0x4583E0's fall-through
  does.
- **Tooltip ids above 0x7F.** The tooltip check at 0x457D7B compares the
  control id against a sign-extended imm8 (`cmp cx, 0x2C`, today patched to
  0x7A). 0x21 + 144 − 1 = 0xB0 does not fit, so the check is replaced by a
  small hook comparing against `WIREFRAME_FIRST_ID + WIREFRAME_MAX − 1`.
- **Fewer controls than the panel fits** (an old `statdata.bin`): the
  existing once-per-game warning (909bef5) covers it; tabs and arrows missing
  from the file are simply not shown.

## 5. Drawing tabs and arrows

- **The frame.** Wireframes draw frame 0x0D (other states 0x13, 0x19, 0x1F)
  of the GRP at `[0x68C1C0]`, the race's `unit\cmdbtns\<race>cmdbtns.grp`,
  loaded at 0x459C0C (0x456D30 draws it). It is 33x34: a 3 px bevel (palette
  indices 0xA0, 0xA5, 0xA0 outward-in) with 2 px rounded corners, filled with
  0x29 (0x2A at the inner corners). It is the same in all three races' files,
  and frame 0x0E is identical.
- **Tabs and arrows draw that frame 9-sliced** to their size: the four 4x4
  corners as they are, the edges repeated, the fill repeated. Drawn from the
  GRP itself, not hard-coded, so new art in `cmdbtns.grp` carries over.
- **The current page's tab**: the same frame with the bright line (0xA5) and
  the fill (0x29) remapped to lighter blues, and its number in green. The
  exact palette indices are picked in game against the mock-up.
- **Numbers** use the game's smallest font, centred; "page/pages" the same.
- **Greyed arrows** remap the frame and the triangle to greys.

## 6. Raising the console art

All in `widenConsoleImage()` (`hooks/interface/resolution_hud.cpp`), as one
separate step after the widening, so a replacement console can skip it:

- **Per race, a column span [left, right) of the vanilla 640x480 art** is
  raised, in the vanilla image before it is widened, so the widening copies
  the raised box (and its strip) like any other art. Values from the
  2026-10-04 trial on the real art:

  | Race (`consoleRace()`) | Art | left | right |
  |---|---|---|---|
  | 0 Zerg | `zconsole.pcx` | 138 | 408 |
  | 1 Terran | `tconsole.pcx` | 143 | 407 |
  | 2 Protoss | `pconsole.pcx` | 143 | 408 |
  | 3 replays | `nconsole.pcx` | 150 | 405 |

  The replay console is raised too (user, 2026-10-04: replays share
  `statdata.bin`, so their panel needs the taller box; the seam on its left
  slope is accepted).
- **Every span lies inside StatData's vanilla columns [138, 408)**, so the
  rows the raise rewrites below the old box top are always painted by
  StatData itself (section "Follows by itself").

- **The cut row is 420** (inside the box's black area in all races). In those
  columns, every row above it moves up 24 px; the 24 rows freed just above
  the cut repeat row 420 (the box's black interior and its side walls).
- **Seams** remain where the raised columns meet unraised art: Protoss's gold
  bar and Zerg's tube on the right, Zerg's stretched left wall, the replay
  console's left slope. Accepted (section 1).
- **Decorative pieces (StatFluf)** paint the art at their own rects, so
  `buildFlufTable` splits every piece at the span's edges and **grows the part
  inside the span 24 px upward** (its bottom stays). Every raised pixel came
  from 24 px lower, where a piece covered it, so the grown piece covers it
  again; the extra rows it also covers repaint the same image. All vanilla
  pieces end above y 388, so above the cut.
- **Follows by itself**, since it is built from the image: the console's
  transparency mask (0x41D640), its hit test lines (0x4D11A0), and StatData's
  background (0x4C35F0). Clicks in the raised strip go to the panel.
- **Message lines**: the error line (y 288 + consoleY and down) stays above
  the raised box (art top about y 321 at its highest); checked in game.

## 7. Console art documentation

The user may replace the console with SC2 art, so `docs/resolution.md` gets a
**Console art reference** section (and the code's comments point to it):

- **Every change the plugin makes to the console image**, in order:
  1. widening: left part, right part, the mirrored strip from the split
     (`consoleStretch`);
  2. the dense command card's frame (`cardSourceColumn`/`cardSourceRow`);
  3. raising the selection box (section 6);
  each with its coordinates, per-race values and the function that does it.
- **What reads the image**: each panel's background (0x4C35F0), the mask
  (0x41D640), the hit test lines (0x4D11A0), and the StatFluf pieces with
  their table (`buildFlufTable`), including the cuts around the card and the
  raised span.
- **What a replacement console must provide**, without reading the code:
  image size and palette rules (in-game palette, 0 transparent); where each
  panel goes (`panelPlacements`); the selection box's rect at each width, the
  split column and strip, or art drawn at the full width instead; the card's
  area; the raised box's top rows. And which steps it then skips (widening,
  card rebuild, raise), each one switchable by itself.

## 8. Pure logic and host tests

In `selection_ext_core.cpp`, covered by `sel_selftest.cpp`:

- `pageSizeFor(width, controls)`: the new formula (270 → 18, 910 → 75,
  1550 → 132, 1678 → 144, capped by the controls).
- `wireframesMissing` keeps working with the new formula.
- `pageControlsFor(pages)`: none (≤ 1), tabs (2-16), arrows (> 16).
- Layout functions giving each control's rect: wireframe k (column-major, 3
  rows), tab p (column-major, 2 x 8, E(r) heights summing to 102), the
  arrows and page number; and the vanilla controls' offset.
- `pageAfterTab`, `pageAfterArrow(page, pages, up)` (stops at the ends).
- `nineSliceSource(x, size, 33 or 34)`: which frame column/row feeds pixel x
  of a stretched frame.

## 9. In-game tests (round 11)

- 11.1 30 units: 3 rows, no gaps, no tabs (one page).
- 11.2 100 units at 1280x720: tabs 1-2, page 1 lit; click 2 shows units 76-100;
  Ctrl+PgUp returns to 1 and the lit tab follows.
- 11.3 400 units: 6 tabs; each shows its page; the last page is partial.
- 11.4 640x480 with 400 units: arrows with "1/23"; ▲ greyed; ▼ to 23/23, then
  ▼ greyed.
- 11.5 The raised box in each race (Terran, Protoss, Zerg) and a replay:
  no holes or stale pixels around the box, decorative pieces in place.
- 11.6 Click in the raised strip: nothing happens on the map; the map just
  above it still takes clicks.
- 11.7 One unit selected: its display centred in the taller box; no tabs.
- 11.8 Wireframe tooltips on the last wireframes of a page at 1920 or 2048
  (ids above 0x7F).
- 11.9 Error messages ("Not enough minerals") still show above the console.
- 11.10 The current-page tab colours match the mock-up (pick the remap).

## 10. Out of scope

- **The lag that grows with the units selected** (reported 2026-10-04): next,
  after this.
- **SC2 console art**: later; section 7 prepares for it.
- Other `docs/resolution.md` §6 items.
