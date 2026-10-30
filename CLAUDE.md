# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A fork of **GPTP** (General Plugin Template Project) — a C++ plugin framework that hooks
StarCraft: Brood War **1.16.1** by patching jumps into the running game binary. The mod is
"Starcraft: Manifold" (`PLUGIN_NAME` / `PLUGIN_ID` in `GPTP/definitions.h`).

Build output is `GPTP.qdp` — a 32-bit DLL with a custom extension, loaded by third-party
1.16.1 loaders (MPQDraft) or baked into an .exe by FireGraft.

Deliberately **not** Remastered: 1.16.1 is far more moddable, and Remastered support is a
non-goal.

## Building

Visual Studio 2026 (v145 toolset), **Win32 only**, C++17. Open `GPTP/GPTP.sln`.

There is no linter and no CI. This is a game plugin: the real verification is building it
and running StarCraft. Debug is the usual configuration; the plugin lands in `GPTP\Debug\`.

`tests/verify.ps1` is the one local check, run on the user's machine: it builds and runs the
host-side tests of the pure logic (`tests/*_test.bat`: the extended selection, the button
set file's parser, the UI mask, the on-map lines and bars' `line_walk`, `order_lines`
and `build_progress`, `player_slots`, the smart rally rules `smart_rally`, and the
morph table with the smart larva pick `unit_morphs`), builds the plugin with MSBuild,
then disassembles `sel_inject.obj`, `smart_rally_inject.obj` and
`smart_morph_inject.obj` and runs `tests/check_naked_wrappers.py` over each. That script fails if a naked wrapper touches
`[ebp…]`: a naked function has no frame of its own, so a Debug-build temporary there writes
into the exe caller's frame. Only the offsets listed in its `ALLOWED` table are allowed.

**Deploying.** A post-build step copies `GPTP.qdp` next to `SCManifold.exe`
(`..\..\SCManifold\` from the solution) when that folder exists. The plugin must then be
repacked into `SCManifold.exe` before testing. The user does it with the Manifold Editor's
**Repack plugin...** and **Repack files...** (spec
`docs/superpowers/specs/2026-10-14-repack-design.md`, tested in game 2026-10-24; split in
two by `docs/superpowers/specs/2026-10-24-editor-qol-design.md`): the first packs
`GPTP.qdp`, the second adds files in a subfolder of `SCManifold\to-repack\` to the exe's
MPQ, so a **new `rez\` file must be in `to-repack\`** to reach the game. Launch (F5) offers
both when the plugin or a to-repack file is newer than the exe's. Adding replaces files in place; it doesn't rebuild the MPQ, so files
already in it (such as the editor's `Manifold\buttonsets.bin`) stay. FireGraft (the plugin)
and PyMPQ (files added by hand, from its own list) remain the fallback. A missing
`rez\statdata.bin` once made the selection panel silently fall back to vanilla's 12
wireframes. `docs/resolution.md` §6 "Working tips" has the paths and helper scripts.

The `reshade/` folder is not in the MPQ: its files sit next to `StarCraft.exe`. A changed
`ManifoldUIMask.fx` is copied into the game folder's `reshade-shaders\Shaders\` by hand and
picked up by Reload in ReShade's overlay, with no rebuild and no repack.

**Claude cannot run this, and in a remote session cannot build it either.** Which it is
depends on where the session runs; check the platform before assuming.

- A **remote session** is Linux with no MSVC, no Windows and no StarCraft install. Nothing
  can be compile-checked before the user builds it.
- A **local session on the user's Windows machine** can build: `tests/verify.ps1` runs
  there, and the plugin lands next to `SCManifold.exe`. It still cannot repack the exe or
  run the game, and the game runs elevated, so its memory cannot be read from the session.

Either way, the consequences worth internalizing:

- In a remote session, include chains, MSVC-specific syntax, and type mismatches are only
  caught by the user's build. Re-read edits for these deliberately — a missing include costs
  a full round trip. In a local session, run `tests/verify.ps1` before handing over.
- Every change costs the user a repack plus an in-game test. Batch related fixes, and when
  debugging, prefer instrumentation that answers several questions in one run over a guess
  that answers one.
- Never claim a change is verified in game. Say what was built or reasoned through and what
  is unverified.

"Is the DLL I'm running actually current?" comes up often enough to be worth settling rather
than assuming. The game-start message from `initializeGame()` (`hooks/main/game_hooks.cpp`)
prints a `__DATE__ __TIME__` build stamp. It only refreshes when that file is recompiled, so
touch `game_hooks.cpp` (or do a full rebuild) before building to make it authoritative.
A stamp older than the build just made means the exe was not repacked: compare
`SCManifold.exe`'s modified time with `GPTP.qdp`'s. With ReShade installed, `ReShade.log`
next to `StarCraft.exe` also names the plugin when a game starts.

## Architecture

### The hook pattern

Nearly every behaviour lives in a triplet under `GPTP/hooks/`:

- `foo.h` — declarations, each annotated with the original game address (`//58BC0`)
- `foo.cpp` — the hook body, written as ordinary C++
- `foo_inject.cpp` — `__declspec(naked)` asm thunks that marshal registers, plus an
  `injectFooHooks()` that calls `jmpPatch(...)` to redirect the game's function to the thunk

Hooks are C++ **reimplementations** of specific game functions. When one is injected, the
original routine at that address no longer runs — the C++ version fully replaces it. Enabling
a hook therefore changes behaviour for everything that used that routine, not just the unit
you care about.

Patching helpers are in `hook_tools.h`: `jmpPatch`, `callPatch`, `memoryPatch`.

### `initialize.cpp` — read this before wondering why a hook never fires

All `inject*Hooks()` calls are registered here, one per line, in two lists: **ENABLED
HOOKS** (only these run, in that order) and **DISABLED HOOKS**, where each line starts with
`//OFF`. Keep that convention: no `/* */` blocks and nothing else on a hook's line, so a grep
for a hook shows at once whether it runs. To turn a hook on, move its line up into the
enabled list.

Most of upstream's hooks are off, including all the weapon hooks. A hook that is off never
installs, and its code silently never runs. This has already cost one debugging cycle: a
beam spawn moved into `fireWeaponHook` produced no output whatsoever because
`injectWeaponFireHooks()` was disabled. If a hook seems dead, grep `initialize.cpp` for it
**before** suspecting the hook body.

Some hooks are installed from inside another module's injector rather than listed here. For
example `resolution::injectConsoleLayoutHooks()` runs even at exactly 640×480, where the rest
of the resolution module is off. Anything that depends on the console layout must be
installed there, not only in `injectHudHooks`.

### The fork's own modules

- **Resolution** (`hooks/interface/resolution*.cpp`, spec `docs/resolution.md`): game view
  at the size in `Manifold.ini` (next to StarCraft.exe), full-width console, 5×3 command
  card (`hooks/interface/buttonsets.cpp`). Cosmetic. `RESOLUTION_DEBUG` in `resolution.h`.
- **Extended selection** (`hooks/selection_ext/`, `SCBW/selection_ext*`, spec
  `docs/superpowers/specs/2026-09-30-extended-selection-design.md`): more than 12 selected
  units, panel pages, control groups, SC2-style subgroups. The pure logic has a host-side
  self-test (`sel_selftest.cpp`).
- **Manifold Editor** (`tools/ManifoldEditor/`, spec
  `docs/superpowers/specs/2026-10-05-button-set-editor-design.md`): a C# FireGraft
  replacement: part 1 button sets, saved as `Manifold\buttonsets.bin` in the mod exe's MPQ
  and applied at game start by `hooks/interface/buttonsets_loader.cpp`; part 2 the strings
  of `rez\stat_txt.tbl` (spec `docs/superpowers/specs/2026-10-08-string-editor-design.md`),
  which follows PyMS's PyTBL; part 4 the 11 `arr\*.dat` files on a Data page, a port of
  PyMS's PyDAT (spec `docs/superpowers/specs/2026-10-24-dat-editor-design.md`; part 2, the
  links (Jump, Used by, copy and paste, TXT import and export, name overrides kept in the
  exe as `Manifold\names\*.txt`; spec `docs/superpowers/specs/2026-10-25-dat-links-design.md`),
  passed on Windows 2026-10-26; part 3, the previews (GRP pictures, icons, sound
  playback; spec `docs/superpowers/specs/2026-10-26-dat-previews-design.md`), passed on
  Windows 2026-10-26; part 4, entry counts (Add Entry, Set Entry Count; spec
  `docs/superpowers/specs/2026-10-26-dat-counts-design.md`) and rows coloured when unsaved, passed
  on Windows 2026-10-27; part 5, improvements, is not built; `MANIFOLD_ARR_DIR` runs the tests on the real files). Unlike the plugin,
  most of it **can** be checked in the Linux session: `dotnet test
  tools/ManifoldEditor/Manifold.Core.Tests` (after `apt-get install -y dotnet-sdk-8.0`),
  and the WinUI window compiles with `dotnet msbuild -restore -t:Compile
  -p:WindowsAppSDKSelfContained=false` in `ManifoldEditor.App/` (it has no `.xaml`, so
  keep it that way). Running it needs Windows; see its README.
- **.dat extension** (`GPTP/DatExtend/`, `hooks/DatExtend/`; from saintofidiocy/GPTP@DatExtender,
  notes in `docs/superpowers/specs/2026-10-24-dat-editor-design.md` §2): images.dat,
  sprites.dat and flingy.dat may have more entries than vanilla, in PyMS's expanded layout;
  the loader moves those tables and re-points the engine at them. An expanded file of any
  other table is refused. The moved arrays in `scbwdata.h` are references read on each use.
  Its loader hook is the first in the enabled list. First run in game 2026-10-27, loading an
  images.dat grown by the editor.
- **Smart-build** (in `hooks/selection_ext/`, spec
  `docs/superpowers/specs/2026-10-03-smart-build-design.md`) and **smart-casting**
  (`hooks/recv_commands/smart_cast.cpp`, `docs/selection.md` §6).
- **ReShade UI mask** (`SCBW/ui_mask*`, `hooks/interface/reshade_bridge*`, `reshade/`, spec
  `docs/superpowers/specs/2026-10-11-reshade-ui-mask-design.md`): the plugin registers with
  ReShade as an add-on, when ReShade is in the process, and uploads where the game view
  is, so `ManifoldUIMask.fx` can keep other effects off the console and the menus. The
  texture's zero means "no effects", which is how ReShade creates it. Cosmetic. ReShade calls back on
  the renderer's thread: only `reshade_bridge.cpp` includes ReShade's headers
  (`GPTP/thirdparty/reshade/`), and the game thread never calls ReShade's runtime.

- **Order lines, rally lines and progress bars** (`plugins::drawOrderQueue`,
  `drawRallyPoint` and `drawBuildProgress` in `hooks/main/game_hooks.cpp`; notes in
  `docs/resolution.md` §6): drawn over the map for the selection (lines) and for every
  unit the player may see at work (bars). Cosmetic. Their rules are pure and host-tested:
  `graphics/line_walk*` (a line walks only its on-screen part), `SCBW/order_lines*`,
  `SCBW/build_progress*`. They name orders, not unit types: a bar shows on anything that
  produces by `Train` or `TrainFighter`, and on any egg (`Morph1`, not completed). Lines show only a chain the player queued with
  Shift: the game's order queue also holds waiting plain orders and its own clean-up
  orders, unmarked, so `hooks/selection_ext/sel_chain*` notes it where a command's order
  enters the queue (local, not saved).
- **Worker and Carrier commands** (in `hooks/selection_ext/sel_synced.cpp`, notes in
  `docs/resolution.md` §6): Cancel Last obeyed by every selected unit, and a worker
  holding cargo goes on with the orders queued behind a gather. Synced.
- **Smart rally** (`hooks/rally/`, rules `SCBW/smart_rally*`, spec
  `docs/superpowers/specs/2026-10-14-smart-rally-design.md`): a worker gathers and a
  unit goes into its own Bunker or transport at its rally; Hatchery, Lair and Hive have
  SC2's separate worker rally (right-click a resource, or Set Worker Rally Point:
  command 0x3E); melee games start with each town hall rallied to its minerals. Synced;
  the worker rallies are in the plugin save chunk (version 4). Upstream's
  `injectRallyHooks()` must stay off: it patches 0x466F50 and 0x45620D too.
- **Smart larva and the morph table** (`hooks/recv_commands/smart_morph*`, rules
  `SCBW/unit_morphs*` and `SCBW/smart_larva*`, spec
  `docs/superpowers/specs/2026-10-15-smart-larva-design.md`): a plain press of a morph
  button morphs one selected unit (command 0x3F), Shift+press all (vanilla 0x23). Which
  units morph, through which of the three eggs, is the table in `SCBW/unit_morphs.cpp`;
  the exe's morph sites (receive, Morph1, cancel, the several-selected button gate) read
  it. Synced. Upstream's `injectUnitMorphHooks()` and `injectCMDRECV_MorphHooks()` stay
  off.

- **FireGraft requirements** (`hooks/interface/requirements_loader.cpp`, parser
  `SCBW/fg_requirements*`, spec `docs/superpowers/specs/2026-10-29-fg-requirements-design.md`):
  FireGraft's own runtime never applies anything in Manifold (upstream's campaign fix at
  0x4100C4 bypasses its storm hook), so the plugin reads the five Dat Requirements tables
  from the `.fgp` in the exe and points the exe's 39 references at its own copies, at the
  first game start. FireGraft's Exe Edits still don't apply.

Selection, smart-build, smart-casting, those two command changes, smart rally and smart
larva are
**synced game state**: they travel as network commands and are recorded in replays. See "Sync safety"
below.

### `SCBW/` — reverse-engineered engine knowledge

`SCBW/structures*.h` describe the game's in-memory structs (`CUnit`, `CSprite`, `CImage`,
`GrpHead`…), and `SCBW/scbwdata.h` maps globals to hardcoded addresses. `SCBW/api.h|cpp`
wraps game functions (`getDistanceFast`, `getAngle`, `getPolarX/Y`, `printText`…).

Unit data members are in `SCBW/structures/Cunitlayout.h`, not `CUnit.h` — `CUnit.h` holds the
methods. Offsets are in the comments.

`SCBW/fgdata.h` maps FireGraft EXE-edit constants, so hooks can read values configured in
FireGraft rather than hardcoding them.

### Per-frame work

`plugins::nextFrame()` in `hooks/main/game_hooks.cpp` runs every frame and iterates visible
units. This is the place for continuous behaviour, and — importantly — it is **live**, unlike
much of the hook registry. Its cost is paid every frame: with selections of up to 400 units,
anything here that walks the whole selection scales with it. So does anything called on
every pass of the main loop, thousands of times per game frame: the command card's rebuild
was, and caused the lag fixed on 2026-10-06 (`docs/resolution.md` §6). To measure, set
`SEL_PROFILE` to 1 in `hooks/selection_ext/sel_profile.h`: it logs per-feature ms per frame
to `Manifold-profile-v2.csv` next to the exe.

The hook is called from 0x4D974E: **after the frame's commands are applied and before the
units update.** What it reads is the state the commands left, a frame before the orders
catch up. A cancel takes the work away at once (the tech, the queue's entry) and leaves the
order to end on its next run, so an order id alone can say "busy" with nothing to do: read
the work as well (`SCBW/build_progress.cpp`). What it draws stays on screen until its next
run, and so trails a moving unit by a frame.

## Working agreements

**Treat upstream GPTP code as correct.** The framework (`SCBW/`, `hooks/`, `hook_tools`,
`logger`) was written by people with years of Brood War reverse-engineering experience.
Apparent oddities are usually hard-won knowledge, not mistakes. Do not "fix" upstream code
speculatively — including struct field signedness, addresses, or asm thunks. Investigate only
when a concrete observed failure points there.

This applies to the framework, not to code written in this fork.

**Angles and directions.** Brood War uses a direction byte: `0` = north, `64` = east,
clockwise (see `scbw::getAngle`, `api.cpp`). Prefer the engine's own helpers —
`scbw::getPolarX/getPolarY`, which read the game's `angleDistance` table — over hand-rolled
`sin`/`cos`. Hand-converting the direction byte to radians produced a persistent 90° error
that was never explained; routing through the engine's table made it correct by construction.

**Sync safety.** Anything affecting game state must execute identically on every client, or
multiplayer and replays desync. Cosmetic-only code (spawning overlay images, drawing) is safe;
keep it that way and never let a visual effect feed back into damage, orders, or timing.

The selection is synced state too: in BW a selection is sent as a command and replayed, and
orders act on the selected units. Changes to what is selected, or to which unit receives a
command (smart-build, smart-casting), must go through the command path so every client and
the replay see the same thing. Local-only state (panel page, highlight, cursor) must never
decide an order. Save-file formats are versioned (the extended selection's chunk is at
version 3); bump the version when the layout changes.

**Naked asm wrappers.** Keep C++ statements with temporaries out of `__declspec(naked)`
functions: in a Debug build the compiler spills them to `[ebp-N]`, which is the exe caller's
frame. `tests/check_naked_wrappers.py` catches this for `sel_inject.cpp`.

**Custom dialog controls must answer the hit test** (user event kind 4) themselves: the
default answer (0x418030) needs flag 0x10, which `statdata.bin`'s buttons lack, so they draw
but never get clicks (see `pageButtonInteract`).

## Debugging in-game

`scbw::printText()` writes to the in-game message area and works in **every** build
configuration. This is the reliable channel.

`GPTP::logger` is a trap for this workflow: it compiles out entirely unless `_DEBUG` is
defined, and `checkLogFile()` opens a **relative** path, so any file it does write lands in
StarCraft's working directory rather than near the project. Prefer `printText`, capped with a
static counter so a screenful of units doesn't flood the message area.

`printText` only works on the game thread. Code that runs on another thread (ReShade's
callbacks arrive on the renderer's) should set flags for the game thread to print, or
write to that host's own log: `reshade::log::message` goes to `ReShade.log` next to
`StarCraft.exe`, which a local session can read without a screenshot.

## Repository layout

- `master` — main line. Resolution, the 5×3 command card, extended selection, smart-build,
  smart-casting, the Manifold Editor and the ReShade UI mask all landed here.
- `claude/ecstatic-darwin-ztohwv` — the branch Claude's sessions work on. It reaches
  `master` by a pull request or a local fast-forward, when the user says so; pushing is the
  user's call too. A local `master` can be well ahead of `origin/master`.
- `feature/beam-weapon` — in-memory GRP beam rendering (see `docs/beam-weapons.md`)
- `wip` — archival: the original unsplit dump, kept for reference

Feature branches rebase onto `master`. Older docs and spec headers mention branches such as
`feature/resolution`, `feature/smart-build` and `feature/selection-pages`; those were local
and are merged into `master`. Build artifacts (`Release/`, `.vs/`, `*.obj`) are
gitignored — an early commit tracked ~280 of them plus a machine-specific `StarCraft.sln`
containing a local install path, which is why `.gitignore` is now broad.

## Design notes

Longer-lived plans and specs live in `docs/`:

- `docs/resolution.md` — the larger game view, full-width console and 5×3 card, with the
  exe's patch map. **§6 is the project's running TODO**: known bugs, the next item, data
  edits the user has to make, the feature order, and working tips. Read it first when
  picking up work.
- `docs/selection.md` — the original survey for 400-unit selection, pages and smart-casting.
  Where it disagrees with the extended-selection spec, the spec wins.
- `docs/superpowers/specs/` — one design spec per feature; `docs/superpowers/plans/` — the
  step-by-step implementation plans. Plans are historical records of how a feature was
  built; specs carry the current status.
- `docs/beam-weapons.md` — the beam weapon system: engine constraints, what is built versus
  proposed, and the staged roadmap. The current copy is on `feature/beam-weapon`.

The docs use `[BUILT]` / `[PROPOSED]` / `[VERIFY]` tags — keep that distinction when
editing, since conflating "works today" with "seems like it should work" is exactly what the
tagging exists to prevent.
