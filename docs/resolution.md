# Larger game view (resolution)

Goal: show more of the battlefield than 1.16.1's 640×480. Tags as in `beam-weapons.md`:
`[BUILT]` works today, `[PROPOSED]` planned, `[VERIFY]` inferred but not confirmed.
The trial build (§4) was tested in game on 2026-09-27 at 1280×720, through cnc-ddraw
upscaled to a 1920×1080 monitor. `[BUILT]`, confirmed: terrain, fog, sprites and the
minimap view box across the whole view; mouse hover, clicks and select-all everywhere;
edge scrolling on all four sides; Alt+Tab.

Menus are centred `[BUILT]`, tested 2026-09-27: the menu screens, the cursor clip, briefings
and loading screens, and quitting to the menu with no leftover game frame. The menus still
draw at 640×480 internally:
- Output is moved when it reaches the surface. Menu dialogs are blitted straight onto the
  surface by 0x4172F0, so its surface pointer is offset (0x417354). The buffer-to-surface
  blits (0x41D44D, 0x41D3A0) copy the buffer's 640×480 block to the centre.
- Mouse input is shifted back at the window procedure (0x4D2324).
- The game's 7 `ClipCursor` and 4 `SetCursorPos` calls are offset too.

The console sits at the bottom centre `[BUILT]` (§5), tested 2026-09-28. The art,
minimap, command card, portrait, selection info, resources, MENU button and every tooltip
are drawn and respond in the right places. A 7-minute game ran with no crash.

In-game popups, chat lines and the Space Platform starfield `[BUILT]` (§5), tested
2026-09-28.

Found and fixed during testing:
- BW's console hit test (0x4D1140) treated everything below the console's bottom line as
  console, across the full width. Widening the console image (§5) fixed this at the
  source.
- Vanilla scrolls 24 px past the map's bottom edge, which showed as a black strip.
- Cursor calls from the plugin must go through StarCraft.exe's import slots, or cnc-ddraw
  can't scale them.

## 1. Decisions

- **cnc-ddraw first.** It windows/upscales whatever mode the game sets, so it is needed
  regardless: 8-bit fullscreen DirectDraw is unreliable on Windows 11, and a player on a
  small monitor can still run a large game view.
- **A genuinely larger view in GPTP**, starting as a trial run behind a debug switch.
  As in the Expander, the display mode is the larger size from startup; the 640×480 menus
  are centred in it.
- **The game view is the whole screen, with the console drawn over it.** The trial left
  the console at its vanilla place. Stage 2 (§5) moves the vanilla 640×480 console, as one
  piece, to the bottom centre. The Expander instead split it at x = 400 into corners.
  Either layout can be built on the same mechanism later, because the layout is a table.
- **Multiplayer uses the host's resolution.** A bigger view shows more of the map, so the
  host issues it as a command on the first game frame and every client applies it in the
  same frame; replays record it too. This keeps honest players on the same build equal —
  it is not anti-cheat, since view size is not synced game state. `[PROPOSED]`

## 2. Prior art

| Work | Approach | Useful for us |
|---|---|---|
| McBane's Resolution Hack (2009) | Renders four 640×480 frames with the camera moved and stitches them | No: minimap jitter, input lag |
| BWAPI 3.7.4 `Resolution.cpp` `SetResolution()` | Reallocates screen/HUD buffers, layer 5 size, `ScrLimit`/`ScrSize`, STrans mask | Partly: debug `/resize` only, terrain left undone, removed in 4.x |
| Hellinsect's Resolution Expander 5.1 / v6 (2009) | ~70 in-process hooks; resizes BW's own buffers | **Yes, the map in §3** |
| NimoStar's *Ultimate StarCraft: Age of Darkness* (2026) | The same hack without InsectLoader ("native executable"), 1280×720 | Confirms the approach still holds; not open source |

Expander facts: closed source; VS2008 Debug build (unoptimised, easy to read); configured
by `ResSettings.ini` (up to 9 `ScreenWidthN`/`ScreenHightN` entries, `+`/`-` switch in
game); runs under cnc-ddraw on Windows 11 per a 2024 ModDB comment. Its HUD layout is
minimap in the bottom-left corner, portrait and command card in the bottom-right, and a
stretched console between them.

## 3. The Expander's patch map

Method: the DLL finds each patch site by **byte-signature search**, not fixed address.
Its signature tables were replayed against our `SCManifold/unpacked/StarCraft.exe`:
66 of 70 match exactly once, one matches 16 times, 3 don't match (4 tables in total;
purpose unknown). That gives **81 hook sites in 71 functions**, and 90 of the 218
hardcoded 640/480-family constants found in the exe sit inside those functions.
So our exe is byte-compatible with the hack in all of these regions.

Areas by address. Terrain and cursor are backed by the constants seen there; the rest
are guesses from neighbouring constants. `[VERIFY]` everything not marked otherwise.

| Range | Hook sites | Probable role |
|---|---|---|
| `40AAE0`–`40C4D2` | `40AB26 40AB82`, 16 unrolled blitters `40ABC4`…`40C0B4`, `40C253` (BWAPI `blitMapTiles`), `40C2CD 40C3BC` | Terrain cache → screen blits. Constants `0x2A0` (672), `0x49800` (672×448), `0x4B000` (640×480) confirm |
| `49B9F0`–`49C8C0` | `49B9F0 49BB13 49BBCD 49BC5B 49BD49 49BE2C 49BF46 49C155 49C315 49C543 49C639 49C6F8 49C7B3` | Terrain cache fill; `49C780` is BWAPI `drawMapTiles`. `imul …, 0x2A0` confirms |
| `4BCDC0` | `4BCDD1` | Also terrain cache (`0x2A0`, `0x49800`) |
| `41D420`–`41E450` | `41D44D 41D750 41DA3D 41DE4C 41E0DA 41E3DD` | Screen buffer / layer setup and clip (`640`, `480`, `639`, `479`) |
| `4215E0`–`421730` | `421629 4216D7` | Another 640×480 rect setup |
| `4D1120`–`4D27A0` | `4D113F 4D12FF 4D1472 4D1BFC 4D2324 4D2501` | Cursor clip: writes `639`/`479` to `0x6CDDC4`/`0x6CDDC8` |
| `47FC50`–`480960` | `47FCA6 47FE23 47FF61 4804F8 4808E0` | Screen movement / map-edge clamps (`640`, `400`) |
| `484500`, `48D560`–`48D700` | `48463E 48D5F2 48D663` | More 640×400 view bounds |
| `46FA40`–`470040` | `46FE18` | `+640`/`+400` just before `drawDragSelBox` (`470040`): on-screen selection bounds |
| `4D57B0` | `4D5856` | `640`, `400` |
| Unclassified | `413B30 417325 417394 4173E6 41838B 41BEE7 41C1D8 41C313 41C96A 41CF1E 42D2D4 45642B 497060 4A3FB7 4A4D97 4A53D9 4BD5A8 4BDFF2 4D9938 4E1986 4EE993 4F4DCD` | No nearby screen constants; need the hook bodies read |

Plus direct writes at `4D1B87`, `41EC8F` (4-byte values), a 41-byte call/jump patch at
`41ED02` and a 2-byte one at `41DA09`, and reads of `0x59BD8C` / `0x6D7448`.

**Buffers it allocates**, all sized from the largest configured resolution (W×H):

| Size | Probable role |
|---|---|
| `(W+32) × (H−32)` | Terrain cache — BW's 672×448 generalised |
| `(W/16) × H × 2` | Per-16px-column, per-row bookkeeping `[VERIFY]` |
| `(W/16) × (H/16) × 4` | Dirty-region grid (BW's is 40×30 of 16px cells) `[VERIFY]` |
| `W/4 + 16` | `[VERIFY]` |
| `W × H × 2`, `W × H × 5` | `[VERIFY]` |
| `(W/8+16) × (H/8+16)`, and two of a quarter that size | `[VERIFY]` |
| fixed `0xFD200` | `[VERIFY]` |

Key design point: he **kept BW's terrain cache and resized it** rather than writing a new
terrain renderer. That's why so many hooks sit on the tile blitters.

## 4. Trial build `[BUILT]`

The code is in `GPTP/hooks/interface/resolution*.{h,cpp}`. The size is read at plugin load
from `Manifold.ini` next to `StarCraft.exe` (`[Resolution] Width=`, `Height=`), which is
written with 1280×720 and a list of presets if missing. Values are rounded down to a
width that is a multiple of 32 and a height that is a multiple of 16, within
640×480–2048×1536. The size in use prints in game under the build stamp. 640×480 leaves
every patch out and plays vanilla. `RESOLUTION_HACK_ENABLED` in `resolution.h` compiles it
all out.

The WMode question is only asked at 640×480 with no `ddraw.dll` next to the exe. WMode
assumes 640×480, and it breaks the display when combined with cnc-ddraw, since both
replace DirectDraw.

| File | Covers |
|---|---|
| `resolution.cpp` | sizes and buffers; the dirty-cell grid (0x41E0D0, 0x41DE20); the console mask; cursor clip on entering and leaving a game |
| `resolution_inject.cpp` | display mode; screen-to-surface blit; gameScreenBuffer swap; users of the grid; Storm's dirty blit; mouse clamps; edge scrolling; view rects; game layer size |
| `resolution_terrain.cpp` | the terrain-cache routines, replaced at their entry points; tile counts; scroll limits; minimap view box; sprite-blitter target; the Space Platform starfield |
| `resolution_fog.cpp` | fog buffer sizes and the fog routines, mostly by rewriting immediates in place |
| `resolution_hud.cpp` | stage 2: panel placement, the widened console image, minimap input, tooltip clamps, the StatFluf tables, in-game popups, message lines (§5) |
| `SCBW/api.cpp` | `refreshScreen()` uses the relocated grid |
| `hooks/interface/selection.cpp` | ctrl-click "select all of type on screen" uses the view size |

Where it deliberately differs from the Expander:

- **The view is the full screen height** rather than `h − 80`, with the console drawn over
  it (see §1). The cache and fog grids are sized from that.
- **Whole routines are replaced at their entry points** rather than patched per caller, so
  every caller sees the new geometry.
- **Fog uses the game's own buffers, enlarged where they are allocated.** The Expander
  swapped in its own buffers every frame, which the game would later try to free. It also
  wrote the per-tile grid past its 408-byte allocation at larger sizes.
- **Minimap view box:** the tile counts are changed so BW's scaling still applies. The
  Expander replaced the scaled result, which looks wrong on maps that aren't 128 tiles.
- **Leaving a game** clears our buffer and marks everything dirty. The Expander instead
  wrote through a surface pointer it had saved while the surface was locked.
- **Dropped:** the +/- resolution switching, the Expander's crash reporter, and every hook
  that only served the relocated console.

Not done yet: see §6.

## 5. Stage 2: the console at the bottom centre `[BUILT]`

**How BW builds the console.** It is not one object:
- **Panels.** Each is a separate dialog loaded from `rez\*.bin`: Minimap, StatData
  (selection info), StatPort (portrait), StatBtn (command card), StatRes (resources),
  Stat_F10 (the MENU button), TextBox (chat input), and StatFluf (decorative pieces).
- **Art.** One 640×480 image, `game\<race>console.pcx`, loaded at 0x4C3950 and kept at
  0x597240. It has no palette of its own: its pixels are indices into the in-game palette,
  and 0 is transparent.
- **Everything else comes from that image.** Each panel copies its background out of it
  (0x4C35F0). The console's transparency mask is built from it (0x41D640), and so are the
  lines the console hit test uses (0x4D11A0).

**What the plugin does.** It works in two places, kept consistent:
- **Panels.** Each panel dialog is moved as its `.bin` is read (0x4194E0), per the
  `panelPlacements` table in `resolution_hud.cpp`. Resources go to the top-right corner and
  everything else to the console offset.
- **Art.** The console image is rebuilt at the full screen size right after it loads
  (0x4C39F5), with the vanilla art placed at the console offset.

The mask, hit test and panel backgrounds then follow by themselves. To change the layout
later, edit the table, or build the full-size image from other art in
`widenConsoleImage()`.

`.bin` facts learned the hard way:
- On disk, a root dialog stores width − 1 and height − 1 where right and bottom go, so only
  left and top are moved.
- Child rects are relative to the root, so only the root is moved.
- StatFluf's pieces are placed from per-race tables (0x5152A8, 0x515300, 0x515348,
  0x515388) that override the `.bin` positions, so those tables are offset instead.

Drawing outside 640×480 needed:
- **Dialog layer.** The dialog layer is widened to the whole screen (0x41A049).
- **Screen buffer.** The screen buffer is allocated at the full size (0x41DDD0).
- **Screen limits.** `ScrLimit`/`ScrSize` are widened, but only while in a game: widening
  them for the menus crashed on starting a mission (0x4E1D6B). The limits must already be
  wide when console setup begins (0x4C3BB2), or the panels' first redraws are clipped away
  and never retried.
- **Clipping.** Clipped rect fill and line routines (0x4E1D20, 0x41D810, 0x41D7D0).
  Vanilla ones write past the buffer once the limits are wider.
- **Storm.** Storm's dirty-cell geometry is set to the full size (0x41D52C).

Input and positions fixed to match:
- **Minimap.** Its rect assumes the panel's left is 0 and its top is 315 (0x4A4539,
  0x4A456C). Its click-to-map conversion does too (0x4A3D77, 0x4A3DB2).
- **Command card.** Its cursor rect is at 0x5136CC.
- **Tooltips.** Tooltips clamp to the screen's right and bottom edges (0x4815E6, 0x481620).
  The command-button tooltips have a separate right-edge clamp (0x458889).
- **View centring.** Every routine that centres the view subtracts half of 640×400 before
  calling `setScreenPos` (0x49C440). These are: portrait click / centre on a unit
  (0x4E6040), the last alert (0x45EE52), a double-tapped group hotkey (0x49691B), trigger
  CenterView and location centring (0x4C6E68, 0x4C6EF7), scroll by percent (0x4844BB),
  and a tile-based centring (0x4BD4B0).

Also moved or fixed:
- **In-game popups.** Every in-game popup is opened by 0x4F57A0 (53 callers): the F10
  menu and its sub-menus, objectives, help, save/load, the chat-target dialogs, victory
  and defeat, Ok boxes. Its `.bin` is moved by half the extra screen size before it is
  relocated (0x4F5912), so it opens in the screen centre. This only happens in a game.
- **Message lines.** The message area (0x48CF60) draws 11 chat lines from y = 0x70 at
  x 10–630, the error/cheat line at y = 0x127, and a line at x 420–620, y 24, under the
  resources. The first two move with the console and the third with the resources.
  Their redraw marks (0x48CB80, an inlined copy at 0x4B22DB, and GPTP's
  `cheat_codes.cpp`) wrote into BW's old 40-column dirty grid. They now use the new one.
- **Space Platform starfield.** 0x47EBF0 (dirty cells) and 0x47EE20 (full redraw) draw
  5 parallax layers of stars onto the empty pixels of the view. The stars sit on a
  648×488 field that wraps (`{u16 x, y; image*}` at `[0x658AA8]`). The originals clip to
  640×400 and write with a 640-byte pitch (the blitter, 0x47EA60, too). Both are
  replaced in `resolution_terrain.cpp`, which repeats the field across the view. The
  repeat every 648 px was not noticeable in testing.
- **StatLB** is the UMS leaderboard, pinned at 0,0. It needs no change.

`RESOLUTION_DEBUG` in `resolution.h` prints a layout report at game frames 48 and 480: which
panels were placed, the console image size, the screen limits and every dialog's position.

## 6. Remaining and future work

State on 2026-09-28: `feature/resolution` builds and plays at 1280×720, with the console
at the bottom centre and the in-game popups, chat lines and starfield handled (§5).

**Checked and done:** in-game dialogs, StatLB, chat and message lines (§5).

**Left alone on purpose:**
- **Creep area.** 0x413DB0 builds a tile rect of ±320 × ±200 px around a building. The
  doc used to guess it was the sound range, but its callers are the creep spread and
  recede code (0x47D796, 0x47DE57): BW's creep size comes from the 640×400 screen. It is
  synced game state, so it must stay vanilla.
- **`GetCursorPos` reads** at 0x42164A, 0x44D8EA, 0x4D12AA and 0x4D1783 bypass the
  window-procedure mouse shift. Nothing wrong has shown up in testing.
- **The per-frame layer paint loop** (0x41E2F2–0x41E344) still gives each layer a 640×480
  rect. Changing it might break the menus, and nothing visibly needs it.

**Wanted with the stretched console:** the user wants the message lines (chat, errors,
the plugin's own text) at the left edge of the screen, not over the minimap. The draw and
redraw positions are the `0x48CF60` patches and `markMessageLine()` in
`resolution_hud.cpp`.

**Then the planned features, in this order** (set by the user on 2026-09-28):
1. **Resolution choice without rebuilding** `[BUILT]`, tested 2026-09-28 at 1024×576,
   1280×720, 1920×1072 and 640×480 (see §4). The size is fixed for the whole run, because
   the dirty grid, terrain cache, fog grids, patched immediates, console offsets and
   console image all follow it.
2. **Stretched full-width console:** custom console art. Put the panels in
   `panelPlacements` and build the image in `widenConsoleImage()` from a wide `.pcx`. The
   `.pcx` must use the in-game palette, with index 0 transparent. Possibly one per race.
   Move the message lines to the screen's left edge at the same time (see above).
3. **Extended button set** `[PROPOSED]`: a larger command card than the vanilla 3×3, using
   the room the wide console gives. Scope to be defined.
4. **Extended unit selection** `[PROPOSED]`: more than 12 selected units. Scope to be
   defined. Unlike the view, selections are sent as network commands and recorded in
   replays, so this touches synced game state, unlike everything above.
5. **Multiplayer host resolution** (§1): the host sends its view size as a command on the
   first game frame; every client applies it in the same frame; replays record it. This
   needs the view size switchable at game start. The buffers would be allocated for the
   largest size, and everything item 1 sets once would be redone per game.
6. **Release build:** the plugin is built as Debug into `GPTP\Debug\`. Turn
   `RESOLUTION_DEBUG` back on when changing the layout.

**Working tips for the next session.**
- Touch `hooks/main/game_hooks.cpp` before building, so the build stamp that prints in game
  updates. The plugin must be repacked into `SCManifold.exe` before testing.
- The game runs from `D:\Games\Starcraft 1.16.1\Starcraft.exe`, and cnc-ddraw
  (`windowed=true` with `fullscreen=true`, i.e. borderless, and `maintas=false`) goes next
  to it.
- To find a leftover 640×480 assumption, grep a full disassembly listing for the
  constants: `0x280`/`0x27F`, `0x1E0`/`0x1DF`, `0x190`, `0x140`/`0xC8`, and `0xA`/`6` in tiles
  (§8).

## 7. Testing the trial

1. Put cnc-ddraw's `ddraw.dll` and `ddraw.ini` next to the exe that runs the mod. Either
   fullscreen-upscaled or windowed works, since cnc-ddraw accepts whatever mode the game sets.
2. Use the `GPTP.qdp` built on `feature/resolution`. On this branch it lands in
   `GPTP\Debug\`, not the mod folder.
3. One run should answer most questions:
   - does it reach the menus (640×480 centred, black around them);
   - in a skirmish, does terrain fill the whole screen, with the console at the bottom centre;
   - scrolling by the right and bottom screen edges, by the arrow keys and by minimap clicks;
   - fog-of-war shading across the whole screen, and creep spreading on screen;
   - selecting and commanding units in the new areas (right of x = 640, below y = 480);
   - leaving the game back to the menus.
4. For an A/B comparison, set the ini to 640×480.

## 8. Reproducing the analysis

The Expander zip is on ModDB (`Resolution_Expander_-_5.1.2.zip`, MD5
`103c4abbd550ffb68ea8904b7fca4dd0`; it contains v6 as `ResExpander6.zip`). The DLL was only
disassembled, never run. The working files (decoded tables, the per-site worksheet,
disassembly dumps and scripts) are kept outside the repo in `D:\SC Modding\resexp-analysis\`.
Tooling: Python 2.7 + `capstone==4.0.2` + `pefile`, installed with
`python -m pip install --target pylib capstone==4.0.2 pefile==2019.4.18` into a `pylib`
folder next to the scripts, which add it to their path.

For StarCraft.exe itself, `dumpall.py` writes a linear-sweep listing of `.text`
(`exe_full.asm`, ~375k lines, one instruction per line) for grepping constants and
callers. `bytes.py <hex addr>…` prints the raw bytes at each address, which you need to find the
immediate's offset inside an instruction before patching it.

Table format: `{u32 patternLen, ptr pattern, 6 × {u32 type, u32 value, u32 offset, ptr anchor,
u32 anchorLen}}`. Zero bytes in a pattern are wildcards. Each entry acts at its anchor inside
(or just around) the match:

| Type | Meaning |
|---|---|
| 0 | call to `value` |
| 1 | jmp to `value` |
| 2 | write 4 bytes from `value` after the anchor |
| 3 | read 4 bytes after the anchor into `value` |
| 4 / 5 | write `offset` bytes after / at the anchor |
| 6 / 7 | read `offset` bytes after / at the anchor |
| 8 | store the anchor address plus `offset` in `value` (return addresses) |
