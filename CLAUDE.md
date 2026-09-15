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

There is no test suite, no linter, and no CI. This is a game plugin: the only real
verification is building it and running StarCraft. Debug is the usual configuration.

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
than assuming. The convention is a `__DATE__ __TIME__` stamp in the game-start message from
`initializeGame()` (`hooks/main/game_hooks.cpp`) — added on `feature/beam-weapon`, worth
keeping when that branch merges. Caveat: it only refreshes when that file is recompiled, so a
full rebuild is what makes it authoritative.

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

All `inject*Hooks()` calls are registered here. **Large stretches are commented out inside
`/* ... */` blocks** (a deliberate "disable unused hooks" pass). A hook sitting inside one of
those blocks is never installed, and its code will silently never run.

This has already cost one debugging cycle: a beam spawn moved into `fireWeaponHook` produced
no output whatsoever because `injectWeaponFireHooks()` was inside a disabled block. If a hook
seems dead, grep `initialize.cpp` and check whether the line is commented out **before**
suspecting the hook body.

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
much of the hook registry.

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

## Debugging in-game

`scbw::printText()` writes to the in-game message area and works in **every** build
configuration. This is the reliable channel.

`GPTP::logger` is a trap for this workflow: it compiles out entirely unless `_DEBUG` is
defined, and `checkLogFile()` opens a **relative** path, so any file it does write lands in
StarCraft's working directory rather than near the project. Prefer `printText`, capped with a
static counter so a screenful of units doesn't flood the message area.

## Repository layout

- `master` — stable line
- `feature/beam-weapon` — in-memory GRP beam rendering (see `docs/beam-weapons.md`)
- `feature/buttonset-extended` — extending the command card past its 9-button cap; incomplete
- `wip` — archival: the original unsplit dump, kept for reference

Feature branches rebase onto `master`. Build artifacts (`Release/`, `.vs/`, `*.obj`) are
gitignored — an early commit tracked ~280 of them plus a machine-specific `StarCraft.sln`
containing a local install path, which is why `.gitignore` is now broad.

## Design notes

Longer-lived plans and specs live in `docs/`. `docs/beam-weapons.md` covers the beam weapon
system: engine constraints, what is built versus proposed, and the staged roadmap. It uses
`[BUILT]` / `[PROPOSED]` / `[VERIFY]` tags — keep that distinction when editing, since
conflating "works today" with "seems like it should work" is exactly what the tagging exists
to prevent.
