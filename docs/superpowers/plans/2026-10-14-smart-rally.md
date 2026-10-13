# Smart Rally Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Workers gather and units enter on a rally; Hatchery, Lair and Hive get SC2's separate worker rally; melee games start with each town hall rallied to its nearest mineral field.

**Architecture:** The rules are pure functions in `SCBW/smart_rally.*`, host-tested. The game side is a new hook triplet `hooks/rally/smart_rally_hooks.{h,cpp}` + `smart_rally_inject.cpp`: it replaces the new-unit rally routine (0x466F50), patches the right-click receive's rally (0x45620D), keeps a per-slot worker rally table cleared where the game clears its own rally (0x4A076D, 0x49E63A), and adds the plugin command 0x3E, sent in place of the rally button's targeted command (0x4244A0, 0x4C036C) and received through the selection module's dispatch slot. Saves carry the table in the plugin chunk, version 4.

**Tech Stack:** C++17, MSVC v145 Win32, inline x86 asm (naked wrappers), GPTP's `jmpPatch`/`callPatch`; host tests built with `cl` from `tests/*.bat`.

**Spec:** `docs/superpowers/specs/2026-10-14-smart-rally-design.md` (approved 2026-10-14, commit a8b5867).

## Global Constraints

- Synced game state: the rally a new unit uses, the right-click rule, the 0x3E receipt, the death clearing and the game-start rallies run on every client from synced state. Local state (the rally button's note) only picks which command the local player sends. Never let a local value decide an order.
- Naked wrappers hold no C++ temporaries: static locals only, no `[ebp…]`. `tests/check_naked_wrappers.py` checks `sel_inject.obj` and, from Task 2, `smart_rally_inject.obj`.
- `initialize.cpp`: one hook per line in ENABLED HOOKS; nothing else on a hook's line. Upstream's `//OFF hooks::injectRallyHooks();` stays off (it patches 0x466F50 and 0x45620D too).
- No two source files share a basename (MSBuild writes every `.obj` into `GPTP\Debug\`): the pure module is `SCBW/smart_rally.cpp`, the hooks are `hooks/rally/smart_rally_hooks.cpp` and `hooks/rally/smart_rally_inject.cpp`.
- Build and test: `powershell -ExecutionPolicy Bypass -File tests/verify.ps1` (host tests, MSBuild Debug, naked-wrapper checks). It must end with `plugin build succeeded`.
- `docs/resolution.md` is CRLF in git: edit with the Edit tool, stage with `git -c core.autocrlf=false add docs/resolution.md`. Other files: plain `git add`.
- Docs keep the `[BUILT]` / `[PROPOSED]` / `[VERIFY]` tags; nothing is called tested in game until the user says so.
- Commit on `claude/ecstatic-darwin-ztohwv`; never push, never touch `master` (the user's call). Every commit message ends with:
  ```
  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_0197F7ddEEHANB7StohbBkFy
  ```

## Exe facts this plan relies on (the spec's `[VERIFY]` items, settled 2026-10-14)

- **0x466F50** (new unit to rally): EAX = new unit, ECX = building (a larva's unit passes its Hatchery), plain RET. Callers 0x4684E7 (training), 0x45DE74/0x45DE86 (egg hatch, twin), 0x45DF80. Vanilla: building NULL, not `isFactory` (0x47B2E0, includes CC, Nexus, Hatchery/Lair/Hive), `rally.unit == building` or `rally.pt.x == 0` → nothing; rally unit of the new unit's owner → `orderTo(Follow 0x31, it)`; else `orderTo(Move 6, pt)`. First 5 bytes `85 C9 56 8B F0`.
- **Death:** 0x4A0740 (EAX = dying unit) has EDI = the unit at 0x4A076D `call 0x49EB70` (after `push 1`; 0x49EB70 ends `RET 4`). 0x49EB70 runs 0x49E590 (ECX = each unit, arg = the dying unit) over the visible and hidden units. In 0x49E590 every path reaches 0x49E638 `MOV EAX, ESI` / 0x49E63A `CALL 0x47B2E0` (5 bytes) with ESI = the unit being cleaned, EBX = the dying unit; just after, a factory's `rally.unit == EBX` becomes 0 and `rally.pt` the dying unit's sprite position (0x49E643–0x49E666).
- **Right-click receive** (0x4560D0), RallyPoint1 for a factory: at 0x45620D EAX = clicked unit (may be NULL), EDI = building; 0x45620D–0x456234 set `rally.unit` (NULL → the building) and `rally.pt` = the target's sprite position, then `jmp 0x456384`. The ground case is 0x456256 (stays vanilla).
- **Rally button action 0x4244A0** (`ACT_FUNC` is `__fastcall`: ECX = actVar, EDX = Shift); it never reads ECX. Its first instructions: `B0 28` MOV AL, 0x28; `A2 91 16 64 00` MOV [0x641691], AL; next at 0x4244A7. All 15 vanilla rally buttons: actVar 0, icon 286, string 672; the Hatchery/Lair/Hive one uses condition 0x429740.
- **Targeted command send** 0x4C0300 builds `[0x15][u16 x][u16 y][u16 tag][u16 type][u8 order][u8 queued]` (11 bytes; tag 0 for the ground) and at 0x4C036C `CALL 0x485BD0` with ECX = the command, EDX = 11. A rally click's order is RallyPoint1 0x27 (a unit) or RallyPoint2 0x28 (the ground).
- **0x3E is free:** the executor 0x4865D0 sends ids 0x3C–0x44, 0x48, 0x5B to dispatch slot 50, vanilla's bad-command exit; the plugin handles 0x3C and 0x3D there (`selectChunkDispatch`, `sel_inject.cpp`).
- **Right-click load test:** 0x4E6BA0 (ECX = unit; 0 for a hallucination, for an Overlord without Ventral Sacs, else units.dat space provided ≠ 0) and 0x4E6E00 = `scbw::canBeEnteredBy(transport, unit)`, as the right-click rule calls them at 0x455973–0x45598B (→ EnterTransport 0x5C).
- **Vanilla Follow** (0x47C7B0) already enters the owner's Nydus Canal (0x4E8C20 → 0x2F) and recharges at a Shield Battery (0x493520 → 0x42) on arrival.

## Review Focus

1. **Set Worker Rally Point pressed, then targeting cancelled or switched (Tab) and another targeted order sent:** that order must go out as vanilla's 0x15. Pinned by Task 1's `toWorkerRallyCommand` cases (non-rally orders refused) and Task 3's "note cleared on every send".
2. **A Hatchery dies and a Drone later morphs into a new Hatchery in the same slot:** the new one has no worker rally. Pinned by Task 2's `unitDying` plus the type check in `workerRallyOf`; round step 8.
3. **The worker rally's target dies** (a mined-out patch, a destroyed Extractor): the next Drone walks to the point; no crash. Pinned by Task 2's `referenceCleared`; round step 10.
4. **A save from before this build (chunk version 3) loads** with no worker rallies; a version 4 save keeps them. Pinned by Task 4's reader (`clearAll` before reading, version ≤ 4); round step 9.
5. **A replay of a game using right-click rallies, the button and the game-start rallies plays back the same.** Pinned by keeping every change on the receive side (Global Constraints); round step 9.

---

### Task 1: The rules (pure, host-tested)

**Files:**
- Create: `GPTP/SCBW/smart_rally.h`, `GPTP/SCBW/smart_rally.cpp`
- Create: `tests/smart_rally_test.cpp`, `tests/smart_rally_test.bat`
- Modify: `tests/verify.ps1` (after the player slots test, line 18)
- Modify: `GPTP/GPTP.vcxproj` (beside `SCBW\console_raise.cpp` line 332 and `SCBW\console_raise.h` line 528)

**Interfaces:**
- Consumes: nothing.
- Produces (namespace `smartrally`, header `<SCBW/smart_rally.h>`):
  - `const u8 CMD_WORKER_RALLY = 0x3E; const u32 WORKER_RALLY_BYTES = 7;`
  - `bool hasTwoRallies(u16 buildingId);`
  - `bool isRallyResource(u16 targetId, bool sameOwner);`
  - `enum RightClickSets { SETS_NORMAL, SETS_WORKER, SETS_BOTH }; RightClickSets rightClickSets(bool hasTarget, bool targetIsResource, bool workerRallyOnTarget);`
  - `enum RallySource { USE_NORMAL, USE_WORKER, USE_NONE }; RallySource rallySource(bool twoRallies, bool unitIsWorker, bool workerRallySet);`
  - `enum RallyAction { ACT_VANILLA, ACT_MINERALS, ACT_GAS, ACT_ENTER }; RallyAction rallyAction(bool gathersOnRightClick, u16 targetId, bool targetCompleted, bool sameOwner, bool rightClickLoads);`
  - `bool toWorkerRallyCommand(const u8* targeted, u8* out);`

- [ ] **Step 1: Write the header**

`GPTP/SCBW/smart_rally.h`:

```cpp
//Smart rally's rules (docs/superpowers/specs/2026-10-14-smart-rally-design.md):
//which rally a new unit uses, what it does there, what a right-click sets,
//and the worker rally command. Pure, so the host test covers it
//(tests/smart_rally_test.cpp); hooks/rally/smart_rally_hooks.cpp applies it.
#pragma once
#include "../types.h"

namespace smartrally {

//Set Worker Rally Point's command: [0x3E][u16 x][u16 y][u16 target tag], tag
//0 for the ground. Beside the plugin's 0x3C and 0x3D (SCBW/selection_ext.h).
const u8 CMD_WORKER_RALLY = 0x3E;
const u32 WORKER_RALLY_BYTES = 7;

//Hatchery, Lair and Hive: a normal rally and a worker rally.
bool hasTwoRallies(u16 buildingId);

//A resource for a two-rally building's right-click: a mineral field, a
//Vespene Geyser, or a Refinery, Assimilator or Extractor of the building's
//owner, finished or not.
bool isRallyResource(u16 targetId, bool sameOwner);

enum RightClickSets { SETS_NORMAL, SETS_WORKER, SETS_BOTH };
//What a right-click with a two-rally building sets: the ground or a unit that
//is not a resource, the normal rally; a resource, the worker rally; the
//resource the worker rally is already on, the normal rally too.
RightClickSets rightClickSets(bool hasTarget, bool targetIsResource, bool workerRallyOnTarget);

enum RallySource { USE_NORMAL, USE_WORKER, USE_NONE };
//Which rally a new unit uses: a worker of a two-rally building its worker
//rally, or none (it stays) while there is none; everything else the normal
//rally.
RallySource rallySource(bool twoRallies, bool unitIsWorker, bool workerRallySet);

enum RallyAction { ACT_VANILLA, ACT_MINERALS, ACT_GAS, ACT_ENTER };
//What a new unit does at its rally target: gather where its right-click
//gathers (a mineral field; a finished gas building of its owner, the rule of
//0x468930), else go in where its right-click loads it, else vanilla's Follow
//or Move.
RallyAction rallyAction(bool gathersOnRightClick, u16 targetId, bool targetCompleted,
                        bool sameOwner, bool rightClickLoads);

//A rally button's click, the targeted command 0x15 ([0x15][u16 x][u16 y]
//[u16 tag][u16 type][u8 order][u8 queued], built at 0x4C0300), as the worker
//rally command. False, with out untouched, for any other command or order.
bool toWorkerRallyCommand(const u8* targeted, u8* out);

} //smartrally
```

- [ ] **Step 2: Write the failing test**

`tests/smart_rally_test.cpp`:

```cpp
//Host test of the smart rally rules (SCBW/smart_rally.cpp). Build and run
//with smart_rally_test.bat (MSVC x86); it needs no game.
#include <SCBW/smart_rally.h>
#include <cstdio>
#include <cstring>

using namespace smartrally;

namespace {

u32 failures;
u32 firstLine;

void check(bool ok, u32 line) {
	if (!ok) {
		if (failures == 0)
			firstLine = line;
		failures++;
	}
}

#define CHECK(x) check((x), __LINE__)

//The game's unit ids (SCBW/enumerations/UnitId.h).
const u16 MARINE = 0, OVERLORD = 42, CC = 106, REFINERY = 110, INFESTED_CC = 130, HATCHERY = 131, LAIR = 132,
          HIVE = 133, EXTRACTOR = 149, NEXUS = 154, ASSIMILATOR = 157, MINERAL_1 = 176, MINERAL_2 = 177,
          MINERAL_3 = 178, GEYSER = 188;
//The game's order ids (SCBW/enumerations/OrderId.h).
const u8 ATTACK_UNIT = 0x0A, RALLY_UNIT = 0x27, RALLY_POINT = 0x28;

} //unnamed namespace

int main() {
	//Two rallies: Hatchery, Lair, Hive only.
	CHECK(hasTwoRallies(HATCHERY) && hasTwoRallies(LAIR) && hasTwoRallies(HIVE));
	CHECK(!hasTwoRallies(CC) && !hasTwoRallies(NEXUS) && !hasTwoRallies(INFESTED_CC));

	//Resources: minerals and geysers of anyone, gas buildings of the owner.
	CHECK(isRallyResource(MINERAL_1, false) && isRallyResource(MINERAL_2, true) && isRallyResource(MINERAL_3, false));
	CHECK(isRallyResource(GEYSER, false) && isRallyResource(GEYSER, true));
	CHECK(isRallyResource(REFINERY, true) && isRallyResource(EXTRACTOR, true) && isRallyResource(ASSIMILATOR, true));
	CHECK(!isRallyResource(REFINERY, false) && !isRallyResource(EXTRACTOR, false) && !isRallyResource(ASSIMILATOR, false));
	CHECK(!isRallyResource(MARINE, true) && !isRallyResource(CC, true));

	//The right-click table (spec 2.3).
	CHECK(rightClickSets(false, false, false) == SETS_NORMAL);	//the ground
	CHECK(rightClickSets(true, false, false) == SETS_NORMAL);	//a unit, not a resource
	CHECK(rightClickSets(true, false, true) == SETS_NORMAL);
	CHECK(rightClickSets(true, true, false) == SETS_WORKER);	//a resource
	CHECK(rightClickSets(true, true, true) == SETS_BOTH);		//the resource the worker rally is on

	//Which rally a new unit uses.
	CHECK(rallySource(false, true, true) == USE_NORMAL);		//an SCV of a CC
	CHECK(rallySource(false, false, false) == USE_NORMAL);
	CHECK(rallySource(true, false, true) == USE_NORMAL);		//a Zergling of a Hatchery
	CHECK(rallySource(true, true, true) == USE_WORKER);		//a Drone, worker rally set
	CHECK(rallySource(true, true, false) == USE_NONE);		//a Drone, none: it stays

	//What it does there.
	CHECK(rallyAction(true, MINERAL_1, true, false, false) == ACT_MINERALS);
	CHECK(rallyAction(true, MINERAL_3, true, false, false) == ACT_MINERALS);
	CHECK(rallyAction(true, EXTRACTOR, true, true, false) == ACT_GAS);
	CHECK(rallyAction(true, REFINERY, true, true, false) == ACT_GAS);
	CHECK(rallyAction(true, ASSIMILATOR, true, true, false) == ACT_GAS);
	CHECK(rallyAction(true, EXTRACTOR, false, true, false) == ACT_VANILLA);	//unfinished
	CHECK(rallyAction(true, EXTRACTOR, true, false, false) == ACT_VANILLA);	//an enemy's
	CHECK(rallyAction(true, GEYSER, true, false, false) == ACT_VANILLA);		//bare
	CHECK(rallyAction(false, MINERAL_1, true, false, false) == ACT_VANILLA);	//not a worker
	CHECK(rallyAction(false, CC, true, true, true) == ACT_ENTER);				//a Bunker or transport that loads it
	CHECK(rallyAction(true, OVERLORD, true, true, true) == ACT_ENTER);			//a Drone into an Overlord
	CHECK(rallyAction(false, CC, true, true, false) == ACT_VANILLA);			//full, or a hallucination
	CHECK(rallyAction(true, MINERAL_2, true, false, true) == ACT_MINERALS);	//gathering first

	//The rally button's click as the worker rally command.
	const u8 onUnit[11] = { 0x15, 0x34, 0x12, 0x78, 0x56, 0x05, 0x08, 0xE4, 0x00, RALLY_UNIT, 0 };
	u8 out[WORKER_RALLY_BYTES];
	CHECK(toWorkerRallyCommand(onUnit, out));
	const u8 expectUnit[WORKER_RALLY_BYTES] = { CMD_WORKER_RALLY, 0x34, 0x12, 0x78, 0x56, 0x05, 0x08 };
	CHECK(memcmp(out, expectUnit, WORKER_RALLY_BYTES) == 0);
	const u8 onGround[11] = { 0x15, 0x10, 0x00, 0x20, 0x00, 0x00, 0x00, 0xE4, 0x00, RALLY_POINT, 0 };
	CHECK(toWorkerRallyCommand(onGround, out));
	const u8 expectGround[WORKER_RALLY_BYTES] = { CMD_WORKER_RALLY, 0x10, 0x00, 0x20, 0x00, 0x00, 0x00 };
	CHECK(memcmp(out, expectGround, WORKER_RALLY_BYTES) == 0);
	//Any other order (Tab to another subgroup, then Attack): sent as it is.
	const u8 attack[11] = { 0x15, 1, 0, 2, 0, 3, 0, 0xE4, 0x00, ATTACK_UNIT, 0 };
	memset(out, 0xAA, sizeof(out));
	CHECK(!toWorkerRallyCommand(attack, out) && out[0] == 0xAA);
	const u8 rightClick[10] = { 0x14, 1, 0, 2, 0, 3, 0, 0xE4, 0x00, 0 };
	CHECK(!toWorkerRallyCommand(rightClick, out) && out[0] == 0xAA);

	if (failures != 0) {
		printf("FAIL: %u checks (first at smart_rally_test.cpp line %u)\n", failures, firstLine);
		return 1;
	}
	printf("PASS\n");
	return 0;
}
```

`tests/smart_rally_test.bat`:

```bat
@echo off
rem Builds and runs the host test of the smart rally rules.
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars32.bat" >nul
cd /d "%~dp0"
if not exist out mkdir out
cl /nologo /EHsc /W3 /I..\GPTP /Fo:out\ /Fe:out\smart_rally_test.exe smart_rally_test.cpp ..\GPTP\SCBW\smart_rally.cpp > out\smart_rally_build.log
if errorlevel 1 (
	type out\smart_rally_build.log
	exit /b 1
)
out\smart_rally_test.exe
```

`GPTP/SCBW/smart_rally.cpp`, stubs only, so the test can fail:

```cpp
#include "smart_rally.h"

namespace smartrally {

bool hasTwoRallies(u16 buildingId) { return false; }
bool isRallyResource(u16 targetId, bool sameOwner) { return false; }
RightClickSets rightClickSets(bool hasTarget, bool targetIsResource, bool workerRallyOnTarget) { return SETS_NORMAL; }
RallySource rallySource(bool twoRallies, bool unitIsWorker, bool workerRallySet) { return USE_NORMAL; }
RallyAction rallyAction(bool gathersOnRightClick, u16 targetId, bool targetCompleted,
                        bool sameOwner, bool rightClickLoads) { return ACT_VANILLA; }
bool toWorkerRallyCommand(const u8* targeted, u8* out) { return false; }

} //smartrally
```

- [ ] **Step 3: Run the test to see it fail**

Run: `cmd //c "tests\\smart_rally_test.bat"` (from the repo root, in the Bash tool)
Expected: `FAIL: N checks (first at smart_rally_test.cpp line …)` with N > 0. (A "vswhere.exe is not recognized" line before it is harmless.)

- [ ] **Step 4: Write the rules**

Replace `GPTP/SCBW/smart_rally.cpp`:

```cpp
#include "smart_rally.h"

namespace smartrally {

namespace {

//The game's ids (SCBW/enumerations/UnitId.h, OrderId.h).
const u16 REFINERY = 110, HATCHERY = 131, LAIR = 132, HIVE = 133, EXTRACTOR = 149, ASSIMILATOR = 157,
          MINERAL_FIELD_1 = 176, MINERAL_FIELD_3 = 178, VESPENE_GEYSER = 188;
const u8 TARGETED_ORDER = 0x15, RALLY_UNIT = 0x27, RALLY_POINT = 0x28;

bool isMineralField(u16 id) {
	return id >= MINERAL_FIELD_1 && id <= MINERAL_FIELD_3;
}

bool isGasBuilding(u16 id) {
	return id == REFINERY || id == EXTRACTOR || id == ASSIMILATOR;
}

} //unnamed namespace

bool hasTwoRallies(u16 buildingId) {
	return buildingId == HATCHERY || buildingId == LAIR || buildingId == HIVE;
}

bool isRallyResource(u16 targetId, bool sameOwner) {
	return isMineralField(targetId) || targetId == VESPENE_GEYSER || (isGasBuilding(targetId) && sameOwner);
}

RightClickSets rightClickSets(bool hasTarget, bool targetIsResource, bool workerRallyOnTarget) {
	if (!hasTarget || !targetIsResource)
		return SETS_NORMAL;
	return workerRallyOnTarget ? SETS_BOTH : SETS_WORKER;
}

RallySource rallySource(bool twoRallies, bool unitIsWorker, bool workerRallySet) {
	if (!twoRallies || !unitIsWorker)
		return USE_NORMAL;
	return workerRallySet ? USE_WORKER : USE_NONE;
}

RallyAction rallyAction(bool gathersOnRightClick, u16 targetId, bool targetCompleted,
                        bool sameOwner, bool rightClickLoads) {
	if (gathersOnRightClick && isMineralField(targetId))
		return ACT_MINERALS;
	if (gathersOnRightClick && isGasBuilding(targetId) && targetCompleted && sameOwner)
		return ACT_GAS;
	if (rightClickLoads)
		return ACT_ENTER;
	return ACT_VANILLA;
}

bool toWorkerRallyCommand(const u8* targeted, u8* out) {
	if (targeted[0] != TARGETED_ORDER || (targeted[9] != RALLY_UNIT && targeted[9] != RALLY_POINT))
		return false;
	out[0] = CMD_WORKER_RALLY;
	for (u32 i = 1; i < WORKER_RALLY_BYTES; i++)
		out[i] = targeted[i];	//x, y and the target's tag sit at the same places
	return true;
}

} //smartrally
```

- [ ] **Step 5: Run the test to see it pass**

Run: `cmd //c "tests\\smart_rally_test.bat"`
Expected: `PASS`

- [ ] **Step 6: Wire it into verify.ps1 and the project**

In `tests/verify.ps1`, after the two player-slots lines (17-18), add:

```powershell
& "$PSScriptRoot\smart_rally_test.bat" 2>$null
if ($LASTEXITCODE -ne 0) { Write-Output "smart rally test FAILED"; exit 1 }
```

In `GPTP/GPTP.vcxproj`, after `<ClCompile Include="SCBW\console_raise.cpp" />` add `<ClCompile Include="SCBW\smart_rally.cpp" />`, and after `<ClInclude Include="SCBW\console_raise.h" />` add `<ClInclude Include="SCBW\smart_rally.h" />`.

- [ ] **Step 7: Run the whole check**

Run: `powershell -ExecutionPolicy Bypass -File tests/verify.ps1`
Expected: eight `PASS` lines, `naked wrappers: ok`, `plugin build succeeded`.

- [ ] **Step 8: Commit**

```bash
git add GPTP/SCBW/smart_rally.h GPTP/SCBW/smart_rally.cpp tests/smart_rally_test.cpp tests/smart_rally_test.bat tests/verify.ps1 GPTP/GPTP.vcxproj
git commit -F - <<'EOF'
Smart rally: the rules, host-tested

Which rally a new unit uses, what it does there, what a Hatchery's
right-click sets, and the worker rally command (0x3E) made from a rally
button's targeted command. Spec: 2026-10-14-smart-rally-design.md.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_0197F7ddEEHANB7StohbBkFy
EOF
```

---

### Task 2: The worker rally table, new units and deaths

**Files:**
- Create: `GPTP/hooks/rally/smart_rally_hooks.h`, `GPTP/hooks/rally/smart_rally_hooks.cpp`, `GPTP/hooks/rally/smart_rally_inject.cpp`
- Modify: `GPTP/initialize.cpp` (includes near line 18; ENABLED HOOKS after `hooks::injectGatherQueueHooks();`)
- Modify: `GPTP/GPTP.vcxproj`
- Modify: `GPTP/hooks/main/game_hooks.cpp` (`initializeGame()`, the `*elapsedTimeFrames == 0` block)
- Modify: `tests/verify.ps1` (naked-wrapper check of the new object), `tests/check_naked_wrappers.py` (its header comment)

**Interfaces:**
- Consumes (Task 1): `hasTwoRallies`, `rallySource`, `rallyAction`, `RallySource`, `RallyAction`.
- Produces (namespace `smartrally`, header `"hooks/rally/smart_rally_hooks.h"`):
  - `struct WorkerRally { bool set; CUnit* target; u16 x, y; };`
  - `void clearAll();`
  - `WorkerRally* workerRallyOf(CUnit* building);` (NULL unless a Hatchery, Lair or Hive in the unit table)
  - `void orderNewUnitToRally(CUnit* unit, CUnit* factory);`
  - `void unitDying(CUnit* unit);`
  - `void referenceCleared(CUnit* holder, CUnit* dying);`
  - `namespace hooks { void injectSmartRallyHooks(); }`
  - In `smart_rally_hooks.cpp`'s unnamed namespace, for Tasks 3 and 4: `WorkerRally workerRallies[UNIT_ARRAY_LENGTH];`, `u32 slotOf(const CUnit*)`, `bool inTable(const CUnit*)`.

No host test: this task is game glue. The rules it calls are Task 1's; the check is the build and the naked-wrapper script, then the round.

- [ ] **Step 1: Write the header**

`GPTP/hooks/rally/smart_rally_hooks.h`:

```cpp
//Smart rally in the game (docs/superpowers/specs/2026-10-14-smart-rally-design.md):
//the worker rally of Hatchery, Lair and Hive, what a new unit does at its
//rally, the right-click rule and the worker rally command. Synced: it runs
//on every client from synced state, except noteRallyButton and
//workerRallySendAs, which only pick the command the local player sends.
//The rules: SCBW/smart_rally.h.
#pragma once
#include <SCBW/api.h>

namespace smartrally {

//A building's worker rally, kept like the game's own rally (CUnit::rally).
struct WorkerRally {
	bool set;
	CUnit* target;	//NULL for the ground, or once its unit died
	u16 x, y;
};

//A new game: no worker rallies.
void clearAll();
//The worker rally of a Hatchery, Lair or Hive; NULL for any other unit.
WorkerRally* workerRallyOf(CUnit* building);
//Replaces 0x466F50: orders a new unit to the rally of the building that made
//it (for a larva's unit, its Hatchery).
void orderNewUnitToRally(CUnit* unit, CUnit* factory);
//A unit is dying (0x4A0740, before the game clears references to it): its
//own worker rally goes.
void unitDying(CUnit* unit);
//The game clears holder's references to dying (0x49E590): a worker rally on
//it keeps its point and loses its target, as the game's rally does (0x49E643).
void referenceCleared(CUnit* holder, CUnit* dying);

} //smartrally

namespace hooks {

void injectSmartRallyHooks();

} //hooks
```

- [ ] **Step 2: Write the table, the new-unit rule and the death clearing**

`GPTP/hooks/rally/smart_rally_hooks.cpp`:

```cpp
#include "smart_rally_hooks.h"
#include <SCBW/smart_rally.h>
#include <cstring>

namespace smartrally {

namespace {

WorkerRally workerRallies[UNIT_ARRAY_LENGTH];

u32 slotOf(const CUnit* unit) {
	return (u32)(unit - unitTable);
}

bool inTable(const CUnit* unit) {
	return unit >= unitTable && unit < unitTable + UNIT_ARRAY_LENGTH;
}

//Its right-click gathers: the units.dat right-click action.
bool gathersOnRightClick(const CUnit* unit) {
	const u8 action = units_dat::RightClickAction[unit->id];
	return action == RightClickActions::Harvest || action == RightClickActions::HarvestAndRepair;
}

//The game's 0x4E6BA0 (ECX = unit): a transport able to carry now. Not a
//hallucination, an Overlord only with Ventral Sacs, and units.dat space.
const u32 Func_UnitIsActiveTransport = 0x004E6BA0;
bool unitIsActiveTransport(CUnit* unit) {
	static u32 result;
	__asm {
		PUSHAD
		MOV ECX, unit
		CALL Func_UnitIsActiveTransport
		MOV result, EAX
		POPAD
	}
	return result != 0;
}

//A right-click from unit on target would load it: the right-click rule's
//test before EnterTransport (0x455973-0x45598B).
bool rightClickLoads(CUnit* unit, CUnit* target) {
	return unitIsActiveTransport(target) && scbw::canBeEnteredBy(target, unit);
}

} //unnamed namespace

void clearAll() {
	memset(workerRallies, 0, sizeof(workerRallies));
}

WorkerRally* workerRallyOf(CUnit* building) {
	if (building == NULL || !inTable(building) || !hasTwoRallies(building->id))
		return NULL;
	return &workerRallies[slotOf(building)];
}

void orderNewUnitToRally(CUnit* unit, CUnit* factory) {
	if (factory == NULL || !factory->isFactory())
		return;
	const bool worker = gathersOnRightClick(unit);
	const WorkerRally* const workerRally = workerRallyOf(factory);
	const RallySource source = rallySource(hasTwoRallies(factory->id), worker,
	                                       workerRally != NULL && workerRally->set);
	if (source == USE_NONE)
		return;

	CUnit* target;
	u16 x, y;
	if (source == USE_WORKER) {
		target = workerRally->target;
		x = workerRally->x;
		y = workerRally->y;
	}
	else {
		//Vanilla's: no rally while it is the building itself or at x 0.
		if (factory->rally.unit == factory || factory->rally.pt.x == 0)
			return;
		target = factory->rally.unit;
		x = factory->rally.pt.x;
		y = factory->rally.pt.y;
	}

	if (target != NULL) {
		const bool sameOwner = target->playerId == unit->playerId;
		const RallyAction action = rallyAction(worker, target->id, (target->status & UnitStatus::Completed) != 0,
		                                       sameOwner, sameOwner && rightClickLoads(unit, target));
		if (action == ACT_MINERALS) {
			unit->orderTo(OrderId::MoveToMinerals, target);
			return;
		}
		if (action == ACT_GAS) {
			unit->orderTo(OrderId::HarvestGas1, target);
			return;
		}
		if (action == ACT_ENTER) {
			unit->orderTo(OrderId::EnterTransport, target);
			return;
		}
		//Vanilla: follow a unit of the owner's.
		if (sameOwner) {
			unit->orderTo(OrderId::Follow, target);
			return;
		}
	}
	unit->orderTo(OrderId::Move, x, y);
}

void unitDying(CUnit* unit) {
	if (inTable(unit))
		memset(&workerRallies[slotOf(unit)], 0, sizeof(WorkerRally));
}

void referenceCleared(CUnit* holder, CUnit* dying) {
	if (!inTable(holder))
		return;
	WorkerRally& rally = workerRallies[slotOf(holder)];
	if (!rally.set || rally.target != dying)
		return;
	//As 0x49E64B-0x49E666: the point where the dying unit's sprite is.
	rally.target = NULL;
	rally.x = dying->sprite->position.x;
	rally.y = dying->sprite->position.y;
}

} //smartrally
```

- [ ] **Step 3: Write the wrappers and the injector**

`GPTP/hooks/rally/smart_rally_inject.cpp`:

```cpp
//Smart rally's hooks (hooks/rally/smart_rally_hooks.h). The wrappers are
//naked: static locals only, never [ebp...]; tests/check_naked_wrappers.py
//checks this file's object too.
#include "smart_rally_hooks.h"
#include <hook_tools.h>

namespace {

//0x466F50: EAX = the new unit, ECX = the building; plain RET.
void __declspec(naked) orderNewUnitToRallyWrapper() {
	static CUnit* unit;
	static CUnit* factory;
	__asm {
		MOV unit, EAX
		MOV factory, ECX
		PUSHAD
	}
	smartrally::orderNewUnitToRally(unit, factory);
	__asm {
		POPAD
		RETN
	}
}

//0x4A076D in the unit's death (0x4A0740, EDI = the unit): its call of the
//reference clearing 0x49EB70 (1 already pushed; RET 4), which this stub
//ends in.
const u32 Func_ClearReferences = 0x0049EB70;
void __declspec(naked) unitDyingStub() {
	static CUnit* unit;
	__asm {
		MOV unit, EDI
		PUSHAD
	}
	smartrally::unitDying(unit);
	__asm {
		POPAD
		JMP Func_ClearReferences
	}
}

//0x49E63A in 0x49E590, which clears ESI's references to EBX (the dying
//unit): its call of isFactory (EAX = ESI), which this stub ends in. Every
//path of 0x49E590 passes here, just before a factory's rally is cleared.
const u32 Func_IsFactory = 0x0047B2E0;
void __declspec(naked) referenceClearStub() {
	static CUnit* holder;
	static CUnit* dying;
	__asm {
		MOV holder, ESI
		MOV dying, EBX
		PUSHAD
	}
	smartrally::referenceCleared(holder, dying);
	__asm {
		POPAD
		JMP Func_IsFactory
	}
}

} //unnamed namespace

namespace hooks {

void injectSmartRallyHooks() {
	jmpPatch(orderNewUnitToRallyWrapper,	0x00466F50, 0);
	callPatch(unitDyingStub,				0x004A076D, 0);
	callPatch(referenceClearStub,			0x0049E63A, 0);
}

} //hooks
```

- [ ] **Step 4: Register the hook, the files and the game-start reset**

In `GPTP/initialize.cpp`, add `#include "hooks/rally/smart_rally_hooks.h"` after `#include "hooks/rally_point.h"` (line 18). In ENABLED HOOKS, after `hooks::injectGatherQueueHooks();`, add:

```cpp
	//Smart rally (hooks/rally, SCBW/smart_rally.h).
	hooks::injectSmartRallyHooks();
```

In `GPTP/GPTP.vcxproj`, after `<ClCompile Include="hooks\selection_ext\sel_chain.cpp" />` add:

```xml
    <ClCompile Include="hooks\rally\smart_rally_hooks.cpp" />
    <ClCompile Include="hooks\rally\smart_rally_inject.cpp" />
```

and after `<ClInclude Include="hooks\selection_ext\sel_chain.h" />` add `<ClInclude Include="hooks\rally\smart_rally_hooks.h" />`.

In `GPTP/hooks/main/game_hooks.cpp`, add `#include "../rally/smart_rally_hooks.h"` after `#include "../recv_commands/smart_cast.h"`, and in `initializeGame()` make the first statement inside `if (*elapsedTimeFrames == 0)` (before the `printText` of the build stamp):

```cpp
        // A new game (or replay): no worker rallies. A loaded game reads them from its save.
        smartrally::clearAll();
```

- [ ] **Step 5: Check the new wrappers in verify.ps1**

In `tests/verify.ps1`, after the existing naked-wrapper check (`if ($LASTEXITCODE -ne 0) { Write-Output "naked wrapper check FAILED"; exit 1 }`), add:

```powershell
$asm2 = "$PSScriptRoot\out\smart_rally_inject.asm"
cmd /c "`"C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars32.bat`" >nul 2>&1 && dumpbin /nologo /disasm `"$root\GPTP\Debug\smart_rally_inject.obj`" > `"$asm2`""
& C:\Python27\python.exe "$PSScriptRoot\check_naked_wrappers.py" $asm2
if ($LASTEXITCODE -ne 0) { Write-Output "naked wrapper check FAILED (smart rally)"; exit 1 }
```

In `tests/check_naked_wrappers.py`, change the first comment line to `# Checks that the naked wrappers (sel_inject.cpp, smart_rally_inject.cpp) never touch [ebp...].`

- [ ] **Step 6: Build and check**

Run: `powershell -ExecutionPolicy Bypass -File tests/verify.ps1`
Expected: eight `PASS` lines, `naked wrappers: ok` twice, `plugin build succeeded`.

- [ ] **Step 7: Commit**

```bash
git add GPTP/hooks/rally/smart_rally_hooks.h GPTP/hooks/rally/smart_rally_hooks.cpp GPTP/hooks/rally/smart_rally_inject.cpp GPTP/initialize.cpp GPTP/GPTP.vcxproj GPTP/hooks/main/game_hooks.cpp tests/verify.ps1 tests/check_naked_wrappers.py
git commit -F - <<'EOF'
Smart rally: worker rally table, new units, deaths

0x466F50 is replaced: a worker gathers at a mineral field or its own
finished gas building, a unit goes into its own Bunker or transport where
a right-click would load it, else vanilla. A Hatchery's Drones use its
worker rally, or stay without one. The table is cleared where the game
clears its own rally (0x4A076D, 0x49E63A).

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_0197F7ddEEHANB7StohbBkFy
EOF
```

---

### Task 3: Right-click, the button and command 0x3E

**Files:**
- Modify: `GPTP/hooks/rally/smart_rally_hooks.h`, `GPTP/hooks/rally/smart_rally_hooks.cpp`, `GPTP/hooks/rally/smart_rally_inject.cpp`
- Modify: `GPTP/SCBW/selection_ext_core.cpp` (`variableCommandLength`, line 107)
- Modify: `GPTP/hooks/selection_ext/sel_selftest.cpp` (`lengths()`, line 125)
- Modify: `GPTP/hooks/selection_ext/sel_inject.cpp` (`selectChunkDispatch`, lines 290-338)

**Interfaces:**
- Consumes (Task 1): `rightClickSets`, `isRallyResource`, `toWorkerRallyCommand`, `CMD_WORKER_RALLY`, `WORKER_RALLY_BYTES`. (Task 2): `workerRallyOf`, `WorkerRally`, `workerRallies`, `slotOf`, `inTable`.
- Produces:
  - `void rightClickRally(CUnit* building, CUnit* target);`
  - `void noteRallyButton(u32 actVar);`
  - `u8* workerRallySendAs(const u8* targeted);`
  - `void recvWorkerRally(const u8* packet);`
  - In the unnamed namespace, for Task 4: `void setNormalRally(CUnit* building, CUnit* target);`, `void setWorkerRally(WorkerRally* rally, CUnit* target, u16 x, u16 y);`

- [ ] **Step 1: Write the failing self-test check**

In `GPTP/hooks/selection_ext/sel_selftest.cpp`, add `#include <SCBW/smart_rally.h>` after `#include <SCBW/console_raise.h>`, and at the end of `lengths()`:

```cpp
	const u8 workerRally[] = { smartrally::CMD_WORKER_RALLY, 0, 0, 0, 0, 0, 0 };
	CHECK(variableCommandLength(workerRally) == smartrally::WORKER_RALLY_BYTES);
```

- [ ] **Step 2: Run it to see it fail**

Run: `cmd //c "tests\\selection_ext_test.bat"`
Expected: `FAIL: 1 checks (first at sel_selftest.cpp line …)` (the new line).

- [ ] **Step 3: Give 0x3E its length**

In `GPTP/SCBW/selection_ext_core.cpp`, add `#include "smart_rally.h"` after `#include "selection_ext.h"`, and in `variableCommandLength`, after the `CMD_QUEUED_BUILD` case:

```cpp
	if (id == smartrally::CMD_WORKER_RALLY)
		return smartrally::WORKER_RALLY_BYTES;
```

- [ ] **Step 4: Run it to see it pass**

Run: `cmd //c "tests\\selection_ext_test.bat"`
Expected: `PASS`

- [ ] **Step 5: The right-click rule, the button note, the send and the receipt**

In `GPTP/hooks/rally/smart_rally_hooks.h`, before `} //smartrally`, add:

```cpp
//The right-click receive sets building's rally on target (0x45620D; target
//NULL is none): with two rallies, rightClickSets decides; else vanilla.
void rightClickRally(CUnit* building, CUnit* target);
//Local: the rally button's action ran (0x4244A0) with actVar; 1 is Set
//Worker Rally Point. Only picks the command the local player sends.
void noteRallyButton(u32 actVar);
//Local: a targeted command is being sent (0x4C036C). After Set Worker Rally
//Point, a rally click's command as 0x3E; else NULL, to send it as it is.
//Either way the note is cleared.
u8* workerRallySendAs(const u8* targeted);
//0x3E received: the command player's selected Hatcheries, Lairs and Hives
//get their worker rally on the target, or on the point without one.
void recvWorkerRally(const u8* packet);
```

In `GPTP/hooks/rally/smart_rally_hooks.cpp`, add `#include <SCBW/selection_ext.h>` and `#include "../selection_ext/sel_synced.h"` after `#include <SCBW/smart_rally.h>`. Inside the unnamed namespace, after `rightClickLoads`, add:

```cpp
bool workerRallyTargeting;	//local: the last rally button pressed was Set Worker Rally Point
u8 workerRallyCommand[WORKER_RALLY_BYTES];

//As the game sets a rally on a unit (0x45620D): the building itself for
//none, at the target's position.
void setNormalRally(CUnit* building, CUnit* target) {
	if (target == NULL)
		target = building;
	building->rally.unit = target;
	building->rally.pt.x = target->sprite->position.x;
	building->rally.pt.y = target->sprite->position.y;
}

//A worker rally on a unit (at its position) or, target NULL, on (x, y).
void setWorkerRally(WorkerRally* rally, CUnit* target, u16 x, u16 y) {
	rally->set = true;
	rally->target = target;
	rally->x = target != NULL ? target->sprite->position.x : x;
	rally->y = target != NULL ? target->sprite->position.y : y;
}
```

After `referenceCleared`, before `} //smartrally`, add:

```cpp
void rightClickRally(CUnit* building, CUnit* target) {
	WorkerRally* const workerRally = workerRallyOf(building);
	if (workerRally == NULL) {
		setNormalRally(building, target);
		return;
	}
	const bool hasTarget = target != NULL;
	const RightClickSets sets = rightClickSets(
		hasTarget,
		hasTarget && isRallyResource(target->id, target->playerId == building->playerId),
		hasTarget && workerRally->set && workerRally->target == target);
	if (sets == SETS_WORKER || sets == SETS_BOTH)
		setWorkerRally(workerRally, target, 0, 0);
	if (sets == SETS_NORMAL || sets == SETS_BOTH)
		setNormalRally(building, target);
}

void noteRallyButton(u32 actVar) {
	workerRallyTargeting = (u16)actVar == 1;
}

u8* workerRallySendAs(const u8* targeted) {
	const bool forWorkers = workerRallyTargeting;
	workerRallyTargeting = false;
	if (!forWorkers || !toWorkerRallyCommand(targeted, workerRallyCommand))
		return NULL;
	return workerRallyCommand;
}

void recvWorkerRally(const u8* packet) {
	const u16 x = *(const u16*)(packet + 1);
	const u16 y = *(const u16*)(packet + 3);
	CUnit* const target = selext::unitOfTag(*(const u16*)(packet + 5));	//NULL: the ground, or it died
	*selectionIndexStart = 0;
	for (CUnit* unit = selsync::nextSelected(); unit != NULL; unit = selsync::nextSelected()) {
		WorkerRally* const rally = workerRallyOf(unit);
		if (rally != NULL && unit->playerId == *ACTIVE_NATION_ID)
			setWorkerRally(rally, target, x, y);
	}
}
```

- [ ] **Step 6: The three patches**

In `GPTP/hooks/rally/smart_rally_inject.cpp`, inside the unnamed namespace after `referenceClearStub`, add:

```cpp
//0x45620D in the right-click receive (0x4560D0), RallyPoint1 for a factory:
//EDI = the building, EAX = the clicked unit. Vanilla sets the rally there,
//then jumps on to 0x456384.
const u32 Back_RightClickRally = 0x00456384;
void __declspec(naked) rightClickRallyStub() {
	static CUnit* building;
	static CUnit* target;
	__asm {
		MOV building, EDI
		MOV target, EAX
		PUSHAD
	}
	smartrally::rightClickRally(building, target);
	__asm {
		POPAD
		JMP Back_RightClickRally
	}
}

//0x4244A0, the rally button's action (fastcall: ECX = actVar, EDX = Shift),
//which never reads actVar. Its first two instructions (7 bytes) are redone
//here: MOV AL, 0x28; MOV [0x641691], AL.
const u32 Back_RallyButton = 0x004244A7;
void __declspec(naked) rallyButtonStub() {
	static u32 actVar;
	__asm {
		MOV actVar, ECX
		PUSHAD
	}
	smartrally::noteRallyButton(actVar);
	__asm {
		POPAD
		MOV AL, 0x28
		MOV BYTE PTR DS:[0x00641691], AL
		JMP Back_RallyButton
	}
}

//0x4C036C in 0x4C0300, which sends a click's targeted command 0x15: its call
//of the send (ECX = the command, EDX = 11), which this stub ends in. After
//Set Worker Rally Point, a rally click goes as the 7-byte 0x3E instead.
const u32 Func_QueueCommandSend = 0x00485BD0;
void __declspec(naked) targetedSendStub() {
	static u8* command;
	static u8* replaced;
	__asm {
		MOV command, ECX
		PUSHAD
	}
	replaced = smartrally::workerRallySendAs(command);
	__asm {
		POPAD
		CMP DWORD PTR replaced, 0
		JE sendIt
		MOV ECX, replaced
		MOV EDX, 7
	sendIt:
		JMP Func_QueueCommandSend
	}
}
```

In `injectSmartRallyHooks()`, after the three existing lines, add:

```cpp
	jmpPatch(rightClickRallyStub,			0x0045620D, 35);
	jmpPatch(rallyButtonStub,				0x004244A0, 2);
	callPatch(targetedSendStub,				0x004C036C, 0);
```

- [ ] **Step 7: Receive 0x3E**

In `GPTP/hooks/selection_ext/sel_inject.cpp`, add `#include "../rally/smart_rally_hooks.h"` with the other includes, and after `recvQueuedBuildC` add:

```cpp
void __cdecl recvWorkerRallyC(const u8* packet) {
	smartrally::recvWorkerRally(packet);
}
```

Change the comment line `//0x3C: the select chunk; 0x3D: smart-build's queued build (8 bytes).` to `//0x3C: the select chunk; 0x3D: smart-build's queued build (9 bytes); 0x3E: smart rally's worker rally (7 bytes).` In `selectChunkDispatch`, after the first two lines (`CMP BYTE PTR [ESI], 0x3D` / `JE queuedBuild`) add:

```cpp
		CMP BYTE PTR [ESI], 0x3E
		JE workerRally
```

and before `notOurs:` add:

```cpp
	workerRally:
		CMP EAX, 7
		JL notOurs
		SUB EAX, 7
		MOV [EBP+8], EAX
		MOV DWORD PTR [EBP-4], 7
		PUSHAD
		PUSH ESI
		CALL recvWorkerRallyC
		ADD ESP, 4
		POPAD
		JMP Back_CommandDone
```

- [ ] **Step 8: Build and check**

Run: `powershell -ExecutionPolicy Bypass -File tests/verify.ps1`
Expected: eight `PASS` lines, `naked wrappers: ok` twice, `plugin build succeeded`.

- [ ] **Step 9: Commit**

```bash
git add GPTP/hooks/rally/smart_rally_hooks.h GPTP/hooks/rally/smart_rally_hooks.cpp GPTP/hooks/rally/smart_rally_inject.cpp GPTP/SCBW/selection_ext_core.cpp GPTP/hooks/selection_ext/sel_selftest.cpp GPTP/hooks/selection_ext/sel_inject.cpp
git commit -F - <<'EOF'
Smart rally: right-click, Set Worker Rally Point and command 0x3E

A Hatchery's right-click on a resource sets its worker rally; on the
resource the worker rally is already on, the normal rally too. The rally
button with actVar 1 sends its click as 0x3E, received through the
plugin's dispatch slot beside 0x3C and 0x3D.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_0197F7ddEEHANB7StohbBkFy
EOF
```

---

### Task 4: Game-start rallies and saves

**Files:**
- Modify: `GPTP/hooks/rally/smart_rally_hooks.h`, `GPTP/hooks/rally/smart_rally_hooks.cpp`
- Modify: `GPTP/hooks/main/game_hooks.cpp` (`initializeGame()`, the resource-depot loop)
- Modify: `GPTP/hooks/selection_ext/sel_save.cpp`

**Interfaces:**
- Consumes (Task 3): `setNormalRally`, `setWorkerRally`. (Task 2): `workerRallies`, `workerRallyOf`, `clearAll`.
- Produces:
  - `void setStartRally(CUnit* base, CUnit* mineral);`
  - `struct SavedWorkerRally { u16 targetTag; u16 x, y; u8 set; u8 unused; };` (packed, 8 bytes)
  - `void saveWorkerRallies(SavedWorkerRally* out);`, `void loadWorkerRallies(const SavedWorkerRally* in);` (UNIT_ARRAY_LENGTH entries each)

- [ ] **Step 1: The game-start rally and the save block**

In `GPTP/hooks/rally/smart_rally_hooks.h`, before `} //smartrally`, add:

```cpp
//Melee start: base's rally on its nearest mineral field: a Hatchery's worker
//rally, a CC's or Nexus's rally (spec 2.4).
void setStartRally(CUnit* base, CUnit* mineral);

//The plugin save chunk's block (sel_save.cpp, version 4): every slot's
//worker rally, UNIT_ARRAY_LENGTH of them.
#pragma pack(push, 1)
struct SavedWorkerRally {
	u16 targetTag;	//0: the ground, or none
	u16 x, y;
	u8 set;
	u8 unused;
};
#pragma pack(pop)
void saveWorkerRallies(SavedWorkerRally* out);
void loadWorkerRallies(const SavedWorkerRally* in);
```

In `GPTP/hooks/rally/smart_rally_hooks.cpp`, before `} //smartrally`, add:

```cpp
void setStartRally(CUnit* base, CUnit* mineral) {
	WorkerRally* const workerRally = workerRallyOf(base);
	if (workerRally != NULL)
		setWorkerRally(workerRally, mineral, 0, 0);
	else if (base->isFactory())
		setNormalRally(base, mineral);
}

void saveWorkerRallies(SavedWorkerRally* out) {
	for (u32 i = 0; i < UNIT_ARRAY_LENGTH; i++) {
		const WorkerRally& rally = workerRallies[i];
		out[i].targetTag = rally.set ? selext::tagOf(rally.target) : 0;
		out[i].x = rally.x;
		out[i].y = rally.y;
		out[i].set = rally.set ? 1 : 0;
		out[i].unused = 0;
	}
}

void loadWorkerRallies(const SavedWorkerRally* in) {
	for (u32 i = 0; i < UNIT_ARRAY_LENGTH; i++) {
		WorkerRally& rally = workerRallies[i];
		rally.set = in[i].set != 0;
		rally.target = in[i].targetTag != 0 ? selext::unitOfTag(in[i].targetTag) : NULL;
		rally.x = in[i].x;
		rally.y = in[i].y;
	}
}
```

(`selext::tagOf(NULL)` is 0; `unitOfTag` gives NULL for a unit that died, as the game's rally after 0x49E643.)

- [ ] **Step 2: Rally the starting town halls**

In `GPTP/hooks/main/game_hooks.cpp`, `initializeGame()`, replace:

```cpp
                    harvestTargetFinder.setMainHarvester(base);
                    firstMineral.set(base->playerId, scbw::UnitFinder::getNearestTarget(
                                                         base->getX() - 512, base->getY() - 512, base->getX() + 512,
                                                         base->getY() + 512, base, harvestTargetFinder));
```

with:

```cpp
                    harvestTargetFinder.setMainHarvester(base);
                    CUnit *const mineral = scbw::UnitFinder::getNearestTarget(
                        base->getX() - 512, base->getY() - 512, base->getX() + 512, base->getY() + 512, base,
                        harvestTargetFinder);
                    firstMineral.set(base->playerId, mineral);
                    // Smart rally: the base's (worker) rally on it, as SC2 sets at the start. Game state.
                    if (mineral != NULL && base->playerId < 8)
                        smartrally::setStartRally(base, mineral);
```

- [ ] **Step 3: Save chunk version 4**

In `GPTP/hooks/selection_ext/sel_save.cpp`:
- add `#include "../rally/smart_rally_hooks.h"` after `#include "sel_build.h"`;
- the version comment and constant become:

```cpp
//1: selections (stages 1-3). 2: selections, then control groups (stage 4).
//3: as 2, then a second compressed block: smart-build's stamps.
//4: as 3, then a third compressed block: smart rally's worker rallies.
const u16 VERSION = 4;
```

- after `BuildStamps buildStamps;` add:

```cpp
//Version 4's third block.
smartrally::SavedWorkerRally savedRallies[UNIT_ARRAY_LENGTH];
```

- in `writeLastAndExtension`, after the `buildStamps` block's `writeCompressed` check and before `return count;`:

```cpp
	smartrally::saveWorkerRallies(savedRallies);
	if (!selexe::writeCompressed(file, savedRallies, sizeof(savedRallies)))
		return 0;
```

- in `readLastAndExtension`, right after the vanilla `freadExe` check, add `smartrally::clearAll();`; change `header.version > 3` to `header.version > 4`; and before `return ok ? count : 0;` add:

```cpp
	//Smart rally's worker rallies: version 4, else none.
	if (ok && header.version >= 4) {
		ok = selexe::readCompressed(file, savedRallies, sizeof(savedRallies));
		if (ok)
			smartrally::loadWorkerRallies(savedRallies);
	}
```

- [ ] **Step 4: Build and check**

Run: `powershell -ExecutionPolicy Bypass -File tests/verify.ps1`
Expected: eight `PASS` lines, `naked wrappers: ok` twice, `plugin build succeeded`.

- [ ] **Step 5: Commit**

```bash
git add GPTP/hooks/rally/smart_rally_hooks.h GPTP/hooks/rally/smart_rally_hooks.cpp GPTP/hooks/main/game_hooks.cpp GPTP/hooks/selection_ext/sel_save.cpp
git commit -F - <<'EOF'
Smart rally: melee-start rallies and saves (chunk version 4)

Each starting CC and Nexus is rallied, and each starting Hatchery's worker
rally set, to the nearest mineral field the workers start on. The worker
rallies are saved in the plugin chunk's third block; older saves load with
none.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_0197F7ddEEHANB7StohbBkFy
EOF
```

---

### Task 5: The worker rally line

**Files:**
- Modify: `GPTP/hooks/main/game_hooks.cpp` (after `drawRallyPoint`, line 289; its call at line 416)

**Interfaces:**
- Consumes (Task 2): `smartrally::workerRallyOf`, `smartrally::WorkerRally`.
- Produces: `void plugins::drawWorkerRally(CUnit* unit);` (drawing only).

- [ ] **Step 1: Pick the colour with the user**

Render a preview (scratchpad Python 2.7 PNG, the game's palette from `tunit.pcx`/the in-game palette via `D:\SC Modding\resexp-analysis\mpqget.py`, per memory "use-users-eyes"): a Hatchery, a mineral line and a patch of ground, with the worker rally line drawn three ways, labelled: **A** yellow (135, as a rally on a resource is now), **B** white, **C** cyan (the `graphics.h` colours), the normal rally in its colour 83 beside it. Ask the user with AskUserQuestion (the preview attached to each option). The answer sets `WORKER_RALLY_COLOR` below; until then it is yellow.

- [ ] **Step 2: Draw it**

In `GPTP/hooks/main/game_hooks.cpp`, after `drawRallyPoint`'s closing brace, add (with the user's colour):

```cpp
// Smart rally: the worker rally of a selected Hatchery, Lair or Hive, drawn like its rally point
// (hooks/rally/smart_rally_hooks.h). Its colour was picked by the user from a preview. Drawing only.
const graphics::ColorId WORKER_RALLY_COLOR = graphics::YELLOW;

void drawWorkerRally(CUnit *unit)
{
    const smartrally::WorkerRally *const rally = smartrally::workerRallyOf(unit);
    if (rally == NULL || !rally->set)
        return;

    u16 rallyX = rally->x;
    u16 rallyY = rally->y;

    // Drones follow a unit of their own player there, so the line follows it while it is seen
    if (seenByLocalPlayer(rally->target) && rally->target->playerId == unit->playerId)
    {
        rallyX = rally->target->position.x;
        rallyY = rally->target->position.y;
    }

    graphics::drawChevronLine(unit->position.x, unit->position.y, rallyX, rallyY, WORKER_RALLY_COLOR,
                              graphics::ON_MAP);
    graphics::drawDottedEllipse(rallyX - 8, rallyY - 4, rallyX + 8, rallyY + 4, WORKER_RALLY_COLOR,
                                graphics::ON_MAP);
    graphics::drawDot(rallyX, rallyY, WORKER_RALLY_COLOR, graphics::ON_MAP);
}
```

and at the call site:

```cpp
                if (units_dat::GroupFlags[selUnit->id].isFactory)
                {
                    plugins::drawRallyPoint(selUnit);
                    plugins::drawWorkerRally(selUnit);
                }
```

- [ ] **Step 3: Build and check**

Run: `powershell -ExecutionPolicy Bypass -File tests/verify.ps1`
Expected: eight `PASS` lines, `naked wrappers: ok` twice, `plugin build succeeded`.

- [ ] **Step 4: Commit**

```bash
git add GPTP/hooks/main/game_hooks.cpp
git commit -F - <<'EOF'
Smart rally: the worker rally line

A selected Hatchery, Lair or Hive shows its worker rally beside its rally
point, following a seen unit of its owner's as the Drones do.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_0197F7ddEEHANB7StohbBkFy
EOF
```

---

### Task 6: Docs, the build stamp and the round

**Files:**
- Modify: `CLAUDE.md` (Building: the host test list; Architecture: the fork's modules; the synced list)
- Modify: `docs/resolution.md` §6 (the smart rally entry; "Data edits, the user's to make")
- Modify: `docs/superpowers/specs/2026-10-14-smart-rally-design.md` (status line)

- [ ] **Step 1: CLAUDE.md**

In "Building", the host test list in parentheses gains `the smart rally rules (`smart_rally`)`; the naked-wrapper sentence names `sel_inject.obj` and `smart_rally_inject.obj`. In "The fork's own modules", after the "Worker and Carrier commands" bullet, add:

```markdown
- **Smart rally** (`hooks/rally/`, rules `SCBW/smart_rally*`, spec
  `docs/superpowers/specs/2026-10-14-smart-rally-design.md`): a worker gathers and a
  unit goes into its own Bunker or transport at its rally; Hatchery, Lair and Hive have
  SC2's separate worker rally (right-click a resource, or Set Worker Rally Point:
  command 0x3E); melee games start with each town hall rallied to its minerals. Synced;
  the worker rallies are in the plugin save chunk (version 4).
```

and in the sentence "Selection, smart-build, smart-casting and those two command changes are **synced game state**", add smart rally.

- [ ] **Step 2: docs/resolution.md §6 and the spec's status**

Replace the smart rally entry's tag `[PROPOSED]` with `[BUILT 2026-10-14, not yet run in game]`, and add under it:

```markdown
- Replays recorded before this build play differently: Drones no longer follow a
  Hatchery's rally, and melee games start with rallies set.
```

Under "**Data edits, the user's to make:**" add:

```markdown
- **Set Worker Rally Point** (smart rally, 2026-10-14): in the Manifold Editor, on the
  Hatchery, Lair and Hive sets, a button beside Set Rally Point: icon 286 (the rally
  flag, for now), condition 0x429740 (as Set Rally Point's), action Rally Point
  0x4244A0 with actVar 1, a new string "Set Worker Rally Point" with hotkey G. Save,
  repack.
```

In the spec, the status line becomes: `Status: [BUILT 2026-10-14], not yet run in game. Plan: docs/superpowers/plans/2026-10-14-smart-rally.md.`

- [ ] **Step 3: Build with a fresh stamp**

Run: `touch GPTP/hooks/main/game_hooks.cpp` then `powershell -ExecutionPolicy Bypass -File tests/verify.ps1`
Expected: `plugin build succeeded`. Read the stamp: `grep -a -o "Oct [0-9 ]* 2026 [0-9][0-9]:[0-9][0-9]:[0-9][0-9]" GPTP/Debug/GPTP.qdp | sort -u | tail -1`.

- [ ] **Step 4: Commit**

```bash
git add CLAUDE.md docs/superpowers/specs/2026-10-14-smart-rally-design.md
git -c core.autocrlf=false add docs/resolution.md
git commit -F - <<'EOF'
Docs: smart rally built (not yet run in game)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_0197F7ddEEHANB7StohbBkFy
EOF
```

- [ ] **Step 5: Hand over the round**

Tell the user: the stamp; the data edit (Step 2's text) to make in the Manifold Editor before testing; repack `GPTP.qdp` (no new `rez\` file). Then the numbered round, "yes" = pass:

1. Melee start, each race: the CC/Nexus shows a rally line to a mineral field, the Hatchery a worker rally line; a new SCV, Probe and Drone go mining.
2. Hatchery: right-click a mineral field: the worker rally goes there; Drones mine; Zerglings stay (no normal rally).
3. Right-click the same mineral field again: the normal rally goes there too; Zerglings walk there and stand.
4. Right-click the ground: the normal rally goes there, the worker rally stays.
5. Right-click your own finished Extractor: Drones mine gas. A bare geyser: Drones walk there. An enemy's Extractor: the normal rally goes there.
6. Set Worker Rally Point (G) on the ground and on a unit: the worker rally goes there. Then press it, Esc, Tab to other units and give a targeted order (Attack): it is obeyed normally.
7. Barracks rallied onto your own Bunker (with room) and onto a Dropship: Marines go in. A full Bunker: they walk to it. A Gateway rallied onto a hallucinated Shuttle: Zealots don't board.
8. Lair and Hive keep the worker rally; a Hatchery killed and rebuilt in the same place has none.
9. Save and load: worker rallies kept. Watch the replay of this game: it plays back the same.
10. Destroy your own Extractor that the worker rally is on (or mine out its patch), then make a Drone: it walks to that spot.
