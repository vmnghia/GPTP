# ReShade UI mask: effects stay off the console

Status: design 2026-10-11, from the user's answers that day: the plugin feeds the mask to
ReShade directly ("B"); it ships with the mod; the console is the only region now, with room
for more ("3"); a minimal preset now, the user's own preset bundled later ("1"); our own
effect file ("A"). The spike of the same day passed (§2). Built 2026-10-11 (plan
`docs/superpowers/plans/2026-10-11-reshade-ui-mask.md`): the mask's logic is host-tested.
Tested in game 2026-10-11 (build `Oct 11 2026 03:00:27`, ReShade 6.8.0 standard build,
cnc-ddraw `renderer=opengl`, single player): the plan's round of 13 passed. The mask fits
the Terran, Zerg, Protoss and replay consoles, the right way up; it follows a restarted
mission and a loaded save of another race; it is empty in the menus; it survives the
overlay's Reload, a window resize and Alt+Tab; it appears when the techniques are enabled
mid-game; it fits at another resolution and at exactly 640x480; the Add-ons tab lists
"Starcraft: Manifold"; and with `opengl32.dll` renamed away the game runs as before. That
settles the two [VERIFY] notes of §4.2 (techniques enabled mid-game; the launcher loads
the plugin with the two extra exports). Still open: §6's multiplayer and `direct3d9`
notes, neither of which was tried.

Changed 2026-10-11, after that round, on the user's word that ReShade should not work on
the menus: §11. Tested in game 2026-10-11 (build `Oct 11 2026 16:16:13`): §11's round of
7 passed. Where §3, §4 and §6 say that an empty mask lets the effects through, §11 is what
holds.

## 1. Goal

The mod is played through cnc-ddraw with ReShade on top (sharpening, anti-aliasing,
HDR-style effects). Those effects run over the whole frame, console included: small text
gets sharpened or blurred and the panels bloom.

The plugin tells ReShade where the console is, so that a mask effect can put the untouched
console back after the other effects have run. The mask is exact for the race being played
and for the resolution in `Manifold.ini`, and it needs no file, no script and no key.

Nothing is hidden by it: inside the mask the player sees the frame the game drew, buttons,
wireframes, minimap and text included, as if ReShade were off there.

Success: with any effect between the two mask techniques, the game view shows the effect
and the console looks exactly as it does with ReShade's effects switched off, in a Terran,
a Zerg and a Protoss game and in a replay, at 1600x900 and after changing the resolution,
without touching ReShade in between.

## 2. Facts this rests on

**The spike** (2026-10-11, throwaway code in the plugin, removed since; ReShade 6.8.0,
standard build, as `opengl32.dll` next to `StarCraft.exe`, hooking cnc-ddraw's
`renderer=opengl`). From `ReShade.log` and the user's screen:

- The plugin registered as an add-on from where it is loaded today:
  `Registered add-on "FGP8462" v0.0.0.0 using ReShade API version 20`. `FGP8462` is the
  file name FireGraft's launcher loads the plugin under. No `.addon32` file and no special
  ReShade build were needed.
- ReShade is not loaded when the plugin initialises; it comes with cnc-ddraw's renderer.
  Registering at the first game frame worked.
- Events arrive on the renderer's thread (50436), not the game's (47736).
- `find_texture_variable("UIMask.fx", "MaskTex")` found a texture declared inside a
  namespace by its plain name. The scoped name (`UIMask::MaskTex`) did not.
- The texture was 1600x896, format `r8_unorm`: the size of the frame ReShade sees, which
  was the game's size because the window was that size.
- `update_texture` with one byte per pixel at that size put the test pattern on screen
  (left half white), in game, with no error.
- Not tested: an upload after the overlay's Reload button.

**ReShade's source, v6.8.0** (read, not run):

- `runtime::update_texture` (`runtime.cpp`): for an R8 texture the data is one byte per
  pixel; data of another size is resized to the texture's with `stbir_resize`.
- New effect textures are cleared to zero (`runtime.cpp`, "Clear texture to zero since by
  default its contents are undefined").
- `reshade_reloaded_effects` invalidates every texture handle.
- The standard build (`RESHADE_ADDON == 1`) stops calling add-ons while it sees network
  traffic in 10 or more of the last 60 presented frames (`runtime.cpp`, "Detect high
  network traffic"; `addon_manager.hpp`, `if (!addon_enabled) return;`). The build from
  the "Addon" installer has no such limit.
- An add-on's shown name comes from a `NAME` export of its module, then from its file name.

**The plugin** (`hooks/interface/resolution_hud.cpp`):

- The console image is `{u16 width, u16 height, u8* pixels}` at 0x597240, one byte per
  pixel, 0 = transparent (`docs/resolution.md`, "Console art reference").
- `consoleImageLoaded_4C39F5` runs right after the game stores it and calls
  `widenConsoleImage()`, which rebuilds it at the screen size. Its hook is installed by
  `injectConsoleLayoutHooks()`, so it also runs at exactly 640x480. It runs for every new
  game, loaded game and replay.
- `hooks::gameEnd()` (`hooks/main/game_hooks.cpp`) runs when a game ends.

## 3. What the player does

Once, in ReShade's overlay (or by using the preset the mod ships, §7): enable
`ManifoldMask_Top` and `ManifoldMask_Bottom`, put Top above the effects to keep off the
console and Bottom below them. Effects placed after Bottom are not masked (a colour grade
over everything, say).

After that, nothing. A new game, another race, a replay, a new resolution, a resized
window and the overlay's Reload all keep the mask right.

Outside a game the masked effects show nowhere (§11; at first the menus got every effect).

## 4. Design

Three parts, each with one job.

### 4.1 The mask's pixels: `SCBW/ui_mask.h|cpp` (pure)

No game memory, no Windows, no ReShade: buildable and testable on the host, like
`SCBW/buttonsets_file.cpp`.

```
namespace uimask {
struct Mask { u32 width, height; std::vector<u8> pixels; };   //0 = effects, 255 = keep the UI
void reset(Mask& mask, u32 width, u32 height);                //that size, all 0
void addConsoleImage(Mask& mask, const u8* image);            //image is width x height, 0 = transparent
}
```

`addConsoleImage` marks every pixel as UI except the game view, and the game view is the
transparent area connected to the top row: a flood fill (4-connected, an explicit stack, no
recursion) from every transparent pixel of row 0. A transparent hole enclosed by the
console therefore stays masked, which is right for a panel area whose art happens to be
index 0. It only ever sets pixels, so a later region is another `add...` function that sets
more: this is the room for the resource counters or dialogs, none of which is built now.

A zero-sized mask and a null image leave the mask as it is.

### 4.2 The ReShade side: `hooks/interface/reshade_bridge.h|cpp`

The only file that includes ReShade's headers, and it includes none of the plugin's own
headers except its own, as the spike did, so `Windows.h` and the engine's packed structs
never meet. ReShade's eight API headers (v6.8.0, API 20, BSD-3-Clause OR MIT) are vendored
under `GPTP/thirdparty/reshade/` with a short note of version and licence.

```
namespace reshadebridge {
void tick();                                         //game thread, every frame
void publishMask(const unsigned char* pixels, unsigned width, unsigned height);
void clearMask();
void shutdown();                                     //Plugin::TerminatePlugin
}
```

- **Registering.** `tick()` looks for a loaded module that exports `ReShadeRegisterAddon`
  and `ReShadeUnregisterAddon` and registers with it once. While none is there it looks
  again every 100 frames; a refusal is final. Without ReShade this is the whole cost: one
  counter per frame, one module enumeration every 100 frames. `publishMask` and
  `clearMask` also try once, so a mask published before the first frame is not late.
  `shutdown()` unregisters.
- **Hand-off.** `publishMask` copies the pixels into a buffer under a mutex and sets a
  "want upload" flag (an atomic). `clearMask` zeroes the buffer and sets it too. The
  game thread never calls ReShade's runtime.
- **Upload**, in the `reshade_present` callback on the renderer's thread: when the flag is
  set, find the texture (`find_texture_variable("ManifoldUIMask.fx", "MaskTex")`) and call
  `update_texture` with the buffer at its own size; ReShade resizes it to the texture's.
  The mutex is held for the upload, which the game thread can only meet while publishing,
  once per game.
- **Re-upload** when the texture may have been replaced: after `reshade_reloaded_effects`
  (which sets the flag), and when the texture's resource view handle
  (`get_texture_binding`) differs from the one uploaded to. The handle is polled every 30
  presents, which also catches the texture not existing yet (effect still compiling, or
  its techniques enabled later). [VERIFY] that enabling the techniques mid-game gets the
  mask within a second.
- **Name.** The plugin exports `NAME` ("Starcraft: Manifold", as `PLUGIN_NAME`) and
  `DESCRIPTION`, so ReShade's add-on list does not show `FGP8462`. They are `extern "C"
  __declspec(dllexport)` variables in `reshade_bridge.cpp`; `plugin.def` is unchanged.
  [VERIFY] MPQDraft and FireGraft still load the plugin with the two extra exports.

No ReShade call is made from a naked wrapper, and nothing here reads or writes game state.

### 4.3 The glue: `resolution_hud.cpp` and `game_hooks.cpp`

- `consoleImageLoaded_4C39F5` calls a new `consoleImageLoaded()` (plain `__cdecl`), which
  calls `widenConsoleImage()` and then, if the image has pixels, `uimask::reset`,
  `uimask::addConsoleImage` and `reshadebridge::publishMask`. The naked stub still makes
  one call.
- `hooks::gameEnd()` calls `reshadebridge::clearMask()`.
- `hooks::nextFrame()` calls `reshadebridge::tick()`.
- `Plugin::TerminatePlugin()` calls `reshadebridge::shutdown()`.

The flood fill runs once per game start over at most 2048x1536 bytes.

### 4.4 The effect: `ManifoldUIMask.fx`

Ours, short, self-contained (it includes no `ReShade.fxh`, so it works with any shader set
installed), in the namespace `ManifoldUIMask`:

- `BackupTex`, the frame's size: `ManifoldMask_Top` copies the frame into it.
- `MaskTex`, the frame's size, R8, no `source` file: zero until the plugin uploads.
- `ManifoldMask_Bottom`: `lerp(frame, backup, mask)`.
- One option, "Display Mask", which tints the masked area magenta over the picture, for
  checking. (At first it showed the mask instead of the picture; with the mask empty that
  blacked out the menus, so a game could not be started with it on: the user, 2026-10-11.)

With no plugin, or outside a game, the mask is zero and the pair does nothing. (Superseded
by §11: the texture is now `EffectsTex`, zero means no effects, and Bottom is
`lerp(backup, frame, effects)`.)

## 5. Sync safety

Cosmetic only. The mask is built from the console image, which is local art, and goes to a
local post-process. It reads no unit, order or selection state and writes none, so replays
and multiplayer are unaffected whether or not a player has ReShade.

## 6. Limits

- **Multiplayer with the standard ReShade build.** That build stops calling add-ons under
  network traffic (§2), so a mask published during a multiplayer game may never be
  uploaded; the console then gets the effects, as it does today. The "Addon" build of
  ReShade has no such limit and is what the player notes recommend for multiplayer.
  [VERIFY] in a LAN game, with both builds.
- **Aspect-ratio bars.** With cnc-ddraw's `maintas=true` the game's picture is only part
  of the frame ReShade sees, and the mask, stretched over the whole frame, sits in the
  wrong place. The notes recommend `maintas=false`.
- **Only the console.** The resource counters, message lines and dialogs over the game
  view still get the effects (§4.1 leaves room for them).
- **Other renderers.** Only cnc-ddraw's `renderer=opengl` with ReShade as `opengl32.dll`
  was tried. `direct3d9` with ReShade as `d3d9.dll` should work the same way. [VERIFY]

## 7. What ships

A `reshade/` folder at the repository's root, laid out as it sits next to `StarCraft.exe`:

- `reshade-shaders/Shaders/ManifoldUIMask.fx`;
- `ReShadePreset.ini`: the two mask techniques enabled, Top first and Bottom last, and
  nothing else. The user's own preset, with the third-party shaders it needs and their
  licences, replaces it in a later task;
- `README.md` for players: which ReShade to install and as which DLL, the two `ddraw.ini`
  lines (`renderer=opengl`, `maintas=false`), where effects go relative to the mask
  techniques, and the limits of §6.

ReShade itself is not bundled in this round.

## 8. Tests

**Host** (`tests/ui_mask_test.cpp` and `.bat`, added to `tests/verify.ps1`): the game view
above a full-width console is 0 and the console 255; a tall block at one side is masked and
the view above it is not; a transparent hole inside the console stays masked; a transparent
pocket open to the top is not masked; a top row with no transparent pixel masks everything;
a zero-sized mask and a null image change nothing.

**In game** (one build; "Display Mask" on unless said otherwise):

1. Terran game: white exactly over the console, black over the game view, no white specks
   in the view and no black holes in the console.
2. Zerg, Protoss, a replay: the same, each with its own outline, without touching ReShade.
3. Back in the menus: all black.
4. Another resolution in `Manifold.ini`, restart: the mask still fits.
5. The overlay's Reload in game: the mask comes back.
6. Start a game with the two techniques off, then enable them: the mask appears.
7. Display Mask off, a strong effect between the techniques: the view shows it, the console
   does not, and buttons, wireframes and the minimap update as usual.
8. ReShade's add-ons tab lists "Starcraft: Manifold".
9. `opengl32.dll` renamed away: the game runs as before, with no message and no slowdown.
10. Exactly 640x480 in `Manifold.ini`: the mask fits the vanilla-sized console.

## 9. Afterwards

- Remove what the earlier attempts left in the game folder: `reshade-shaders\make_uimask.py`
  and the third-party `reshade-shaders\Shaders\UIMask.fx`.
- `CLAUDE.md`: a line for this module under "The fork's own modules".
- This spec's status line, with what the in-game round showed.

## 10. Out of scope

Masks for anything but the console; bundling ReShade or the user's preset; a mask that
follows aspect-ratio bars; any change to what the console looks like.

## 11. No effects in the menus (2026-10-11)

The user, after the first in-game round: "ReShade should not work on the menu". As built
until then, the texture marked where the frame from before the effects is kept, so the
empty texture of the menus let every effect through.

**The change.** The texture's meaning is inverted. `EffectsTex` (it was `MaskTex`) marks
where effects apply: 255 in the game view, 0 on the UI. An empty texture now means no
effects anywhere.

**Why this way.** ReShade creates the texture zeroed, and the plugin cannot register before
a game starts (ReShade loads with the renderer, and the plugin's only recurring hooks run
in game). With zero meaning "no effects", the menus are free of them from the first frame,
with no code running outside a game. The other way, a "protect everything" mask sent while
in the menus, needs a hook or a thread there, and the effects would still show at launch
until the plugin had registered.

**What changed in the code.**

- `uimask::effectsArea(mask, effects)` (pure, host-tested) gives the mask's inverse; the
  mask itself (`reset`, `addConsoleImage`, 255 = UI) is as before.
- `reshadebridge::publishMask` and `clearMask` are now `publishEffectsArea` and
  `clearEffectsArea`. `clearEffectsArea` still zeroes the buffer: effects nowhere.
- `consoleImageLoaded()` publishes the effects area; `gameEnd()` clears it.
- The effect: `ManifoldMask_Bottom` is `lerp(backup, frame, effects)`, and "Display Mask"
  tints where `effects` is 0, which outside a game is the whole screen.

**What it costs.** Whenever the plugin cannot reach ReShade, the effects between the two
techniques show nowhere, where before they showed everywhere: an older ReShade, the add-on
unticked in ReShade's Add-ons tab, the effect used without the plugin, and multiplayer on
the standard build (§6), where the masked effects may now not show in the game at all. The
player notes say so, and that switching the two techniques off gives the effects back
unmasked.

**In-game checks** (round 5): in the menus from launch, and again after a game, no effect
between the two techniques shows; in a game the view has them and the console does not;
Display Mask tints the whole screen in the menus and only the console in a game; the
overlay's Reload in a game brings the effects back in the view.
