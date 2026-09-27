# Larger game view (resolution)

Goal: show more of the battlefield than 1.16.1's 640×480. Tags as in `beam-weapons.md`:
`[BUILT]` works today, `[PROPOSED]` planned, `[VERIFY]` inferred but not confirmed.
The trial build (§4) was tested in game on 2026-09-27 at 1280×720, through cnc-ddraw
upscaled to a 1920×1080 monitor. `[BUILT]`, confirmed: terrain, fog, sprites and the
minimap view box across the whole view; mouse hover, clicks and select-all everywhere;
edge scrolling on all four sides; Alt+Tab. Known bug: quitting to the menu leaves the last
game frame around the 640×480 menu.

Found and fixed during testing:
- BW's console hit test (0x4D1140) treated everything below the console's bottom line as
  console, across the full width.
- Vanilla scrolls 24 px past the map's bottom edge, which showed as a black strip.
- Cursor calls from the plugin must go through StarCraft.exe's import slots, or cnc-ddraw
  can't scale them.

## 1. Decisions

- **cnc-ddraw first.** It windows/upscales whatever mode the game sets, so it is needed
  regardless: 8-bit fullscreen DirectDraw is unreliable on Windows 11, and a player on a
  small monitor can still run a large game view.
- **A genuinely larger view in GPTP**, starting as a trial run behind a debug switch.
  As in the Expander, the display mode is the larger size from startup; the 640×480 menus
  sit in its top-left corner. `[VERIFY]`
- **Trial layout: the console stays where it is.** The Expander moved the console to the
  bottom of the screen and split it at x = 400, which takes about a dozen coordinated hooks
  on dialog drawing and input. The trial leaves every dialog at its vanilla place and makes
  the game view the whole screen behind the console. Moving the console is stage 2.
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

The code is in `GPTP/hooks/interface/resolution*.{h,cpp}`. The switch and size are in
`resolution.h`: `RESOLUTION_HACK_ENABLED`, and `RESOLUTION_WIDTH`/`HEIGHT` (default 1280×720;
the width must be a multiple of 32 and the height of 16). With the switch on, the plugin
skips the WMode prompt, so use cnc-ddraw instead.

| File | Covers |
|---|---|
| `resolution.cpp` | sizes and buffers; the dirty-cell grid (0x41E0D0, 0x41DE20); the console mask; cursor clip on entering and leaving a game |
| `resolution_inject.cpp` | display mode; screen-to-surface blit; gameScreenBuffer swap; users of the grid; Storm's dirty blit; mouse clamps; edge scrolling; view rects; game layer size |
| `resolution_terrain.cpp` | the terrain-cache routines, replaced at their entry points; tile counts; scroll limits; minimap view box; sprite-blitter target |
| `resolution_fog.cpp` | fog buffer sizes and the fog routines, mostly by rewriting immediates in place |
| `SCBW/api.cpp` | `refreshScreen()` uses the relocated grid |
| `hooks/interface/selection.cpp` | ctrl-click "select all of type on screen" uses the view size |

Where it deliberately differs from the Expander:

- **The console stays in place** (see §1). The view is therefore the full screen height
  rather than `h − 80`, and the cache and fog grids are sized from that.
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

Not done yet: stage 2 (the console at the bottom) and the multiplayer host-resolution
command.

## 5. Testing the trial

1. Put cnc-ddraw's `ddraw.dll` and `ddraw.ini` next to the exe that runs the mod. Either
   fullscreen-upscaled or windowed works, since cnc-ddraw accepts whatever mode the game sets.
2. Use the `GPTP.qdp` built on `feature/resolution`. On this branch it lands in
   `GPTP\Debug\`, not the mod folder.
3. One run should answer most questions:
   - does it reach the menus (640×480 in the top-left, black elsewhere);
   - in a skirmish, does terrain fill the whole screen, with the console at its old place;
   - scrolling by the right and bottom screen edges, by the arrow keys and by minimap clicks;
   - fog-of-war shading across the whole screen, and creep spreading on screen;
   - selecting and commanding units in the new areas (right of x = 640, below y = 480);
   - leaving the game back to the menus.
4. For an A/B comparison, set `RESOLUTION_HACK_ENABLED` to 0 and rebuild.

## 6. Reproducing the analysis

The Expander zip is on ModDB (`Resolution_Expander_-_5.1.2.zip`, MD5
`103c4abbd550ffb68ea8904b7fca4dd0`; it contains v6 as `ResExpander6.zip`). The DLL was only
disassembled, never run. The working files (decoded tables, the per-site worksheet,
disassembly dumps and scripts) are kept outside the repo in `D:\SC Modding\resexp-analysis\`.
Tooling: Python 2.7 + `capstone==4.0.2` + `pefile`.

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
