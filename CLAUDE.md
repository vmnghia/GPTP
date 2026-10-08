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
host-side tests of the extended selection's pure logic (`tests/selection_ext_test.bat`) and
of the button set file's parser (`tests/buttonsets_file_test.bat`),
builds the plugin with MSBuild, then disassembles `sel_inject.obj` and runs
`tests/check_naked_wrappers.py` over it. That script fails if a naked wrapper touches
`[ebp…]`: a naked function has no frame of its own, so a Debug-build temporary there writes
into the exe caller's frame. Only the offsets listed in its `ALLOWED` table are allowed.

**Deploying.** A post-build step copies `GPTP.qdp` next to `SCManifold.exe`
(`..\..\SCManifold\` from the solution) when that folder exists. The plugin must then be
repacked into `SCManifold.exe` before testing: the user adds files from
`SCManifold\to-repack\` into the exe's MPQ by hand with PyMPQ, so any **new `rez\` file must
be added by hand too**. Adding replaces files in place; it doesn't rebuild the MPQ, so files
already in it (such as the Manifold Editor's `Manifold\buttonsets.bin`) stay. A missing `rez\statdata.bin`
once made the selection panel silently fall back to vanilla's 12 wireframes.
`docs/resolution.md` §6 "Working tips" has the paths and helper scripts.

**Claude cannot build or run this.** The remote session is Linux with no MSVC, no Windows,
and no StarCraft install. Nothing here can be compile-checked before the user builds it.
Consequences worth internalizing:

- Include chains, MSVC-specific syntax, and type mismatches are only caught by the user's
  build. Re-read edits for these deliberately — a missing include costs a full round trip.
- Every change costs the user a rebuild plus an in-game test. Batch related fixes, and when
  debugging, prefer instrumentation that answers several questions in one run over a guess
  that answers one.
- Never claim a change is verified. Say what was reasoned through and what is unverified.

"Is the DLL I'm running actually current?" comes up often enough to be worth settling rather
than assuming. The game-start message from `initializeGame()` (`hooks/main/game_hooks.cpp`)
prints a `__DATE__ __TIME__` build stamp. It only refreshes when that file is recompiled, so
touch `game_hooks.cpp` (or do a full rebuild) before building to make it authoritative.

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
  which follows PyMS's PyTBL. Unlike the plugin,
  most of it **can** be checked in the Linux session: `dotnet test
  tools/ManifoldEditor/Manifold.Core.Tests` (after `apt-get install -y dotnet-sdk-8.0`),
  and the WinUI window compiles with `dotnet msbuild -restore -t:Compile
  -p:WindowsAppSDKSelfContained=false` in `ManifoldEditor.App/` (it has no `.xaml`, so
  keep it that way). Running it needs Windows; see its README.
- **Smart-build** (in `hooks/selection_ext/`, spec
  `docs/superpowers/specs/2026-10-03-smart-build-design.md`) and **smart-casting**
  (`hooks/recv_commands/smart_cast.cpp`, `docs/selection.md` §6).

Selection, smart-build and smart-casting are **synced game state**: they travel as network
commands and are recorded in replays. See "Sync safety" below.

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

## Repository layout

- `master` — main line. Resolution, the 5×3 command card, extended selection, smart-build
  and smart-casting all landed here.
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
