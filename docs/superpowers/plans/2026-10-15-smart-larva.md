# Smart Larva Morphing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A plain press of a morph button morphs one selected unit (the larva of the fullest Hatchery first), Shift+press morphs all, and one table decides which units morph through which egg.

**Architecture:** Pure rules in `SCBW/unit_morphs.*` (the table) and `SCBW/smart_larva.*` (the command id and the pick), host-tested. The hooks in `hooks/recv_commands/smart_morph*` route the exe's morph sites to the table and add command 0x3F, which reuses vanilla's receive 0x4C1990 with only the picked unit let through.

**Tech Stack:** C++17, MSVC v145 Win32, naked asm stubs, `tests/verify.ps1`.

**Spec:** `docs/superpowers/specs/2026-10-15-smart-larva-design.md`

## Global Constraints

- Synced: who morphs is decided on every client from the command; local state never decides it.
- 0x23 keeps vanilla's meaning (morph every selected unit that can); 0x3F `[u16 type]`, 3 bytes, morphs one.
- Eggs are only Egg (36), Cocoon (59), Lurker Egg (97). Zergling (37) → Lurker Egg.
- Naked stubs: static locals only, never `[ebp…]`; names end in `Stub`; their object is checked by `tests/check_naked_wrappers.py`.
- `initialize.cpp`: one hook per line in the enabled list.
- No save change (the chunk stays at version 4).

## Exe facts (the spec's [VERIFY] items, resolved 2026-10-15)

- **Send.** The morph button's action 0x423790 (fastcall ECX = type) builds `0x23 [u16 type]` at [ebp-4] and calls the send 0x485BD0 at **0x423846** (ECX = the bytes, EDX = 3). The dispatcher 0x459912 passes `IS_HOLDING_SHIFT` (0x596A28) as EDX; the stub reads that byte directly.
- **Receive 0x23** 0x4C1990, `stdcall(command)`, `ret 4`, called only from 0x486B4F. It loops on 0x49A850. Per unit: 0x4C19B0 `mov ax,[esi+64]`, 0x4C19B4 `cmp ax,26`, 0x4C19B8 `mov ebx,[ebp+8]` (the command), **0x4C19BB** `jne` …; Hydralisk + type 0x67 → canUseTech(0x20); Larva/Mutalisk → canMakeUnit 0x46E1C0; result `cmp eax,1` at 0x4C19F8. Eligible goes on at **0x4C19FD** (`cmp [esi+4d],2A`: not already Morph1, then 0x467250 pays and queues, then the order); refused goes to **0x4C1A74** (next unit). Only fallthrough reaches 0x4C19BB–0x4C19BF.
- **Morph1** 0x45DEA0, ESI = unit. State 0 at **0x45DFB0**: Larva/Hydralisk/Mutalisk → **0x45DFCA**, else **0x45E07C** (return). Egg choice at **0x45E019**: clears status bit 1 (`[esi+DC] &= ~1`), then pushes 0x24/0x61/0x3B and goes to **0x45E048** (`mov eax,esi; call 0x49FED0`), else 0x45E07C. Both regions are entered only at their start.
- **Cancel** 0x468280 (ECX→ESI = unit), called by the cancel-morph receive 0x4C2EC0 for every selected unit of the player. For an unfinished unit (status bit 1 clear): 0x4682AE refuses Guardian/Lurker/Devourer, then **0x4682E5** refuses Mutalisk/Hydralisk (`je 0x468418`) and goes on at **0x4682F9** (`test al,2`: AL = status, DX = id must survive). After the refund, **0x468351** picks the revert: Cocoon → `push 2B`, Lurker Egg → `push 26`, then **0x46839E** (`mov eax,esi; call 0x49FED0`); else **0x468361** (`cmp ax,0E`, AX = id). 0x468351 is entered at its start (from 0x46831A too).
- **previousUnitType** (+0x88): 0x49F170, reached from 0x49FED0, writes the old id there on every type change. So an egg's previousUnitType is its source, and a unit that hatched from an egg has the egg there.
- **Button gate** 0x428E60: with several units selected, Larva/Mutalisk/Hydralisk go on at 0x428E91; others reach 0x428E89, which smart-build's `canCreateWorkerStub` (sel_inject.cpp) already replaces (workers allowed).
- **0x3F** is in the executor's dispatch slot 50 (0x3C–0x44) with 0x3C/0x3D/0x3E.

## Review Focus

1. Cancelling an egg twice (a reverting Zergling, a hatching Baneling): must refund once; pinned by `keptFromCancel` tests (Task 1).
2. A plain press with nothing eligible (all already eggs, wrong type): nothing happens, no crash; pinned by `pickMorpher(…, 0)` (Task 1).
3. Larvae whose Hatchery died: still morph, each its own group; pinned by the key rule test (Task 1).
4. Mixed selection (larvae + Hydralisks) pressing a larva unit: only larvae are candidates; pinned by `morphCheck` (Task 1).
5. Vanilla sources keep vanilla's reverts and eggs; pinned by `cancelRevert`/`eggOf` tests (Task 1).

---

### Task 1: The morph table and the pick (pure)

**Files:**
- Create: `GPTP/SCBW/unit_morphs.h`, `GPTP/SCBW/unit_morphs.cpp`, `GPTP/SCBW/smart_larva.h`, `GPTP/SCBW/smart_larva.cpp`
- Create: `tests/unit_morphs_test.cpp`, `tests/unit_morphs_test.bat`
- Modify: `tests/verify.ps1` (run the new test after smart rally's)

**Interfaces:**
- Produces: `unitmorphs::NONE/EGG/COCOON/LURKER_EGG`, `struct MorphRow {u16 source, egg, techTarget; u8 tech;}`, `const MorphRow* morphRowOf(u16)`, `bool isMorphSource(u16)`, `u16 eggOf(u16)`, `enum MorphCheck {CHECK_NONE, CHECK_MAKE, CHECK_TECH}`, `MorphCheck morphCheck(u16 unitId, u16 type)`, `u16 cancelRevert(u16 eggId, u16 previousType)`, `bool keptFromCancel(u16 unitId, u16 previousType)`; `smartlarva::CMD_MORPH_ALL = 0x23`, `CMD_MORPH_ONE = 0x3F`, `MORPH_ONE_BYTES = 3`, `u8 morphCommandId(bool shiftHeld)`, `u32 pickMorpher(const u32* keys, u32 n)`.

- [ ] **Step 1: Write the failing test** `tests/unit_morphs_test.cpp`:

```cpp
//Host test of the morph table (SCBW/unit_morphs.cpp) and the smart larva pick
//(SCBW/smart_larva.cpp). Build and run with unit_morphs_test.bat (MSVC x86).
#include <SCBW/unit_morphs.h>
#include <SCBW/smart_larva.h>
#include <cstdio>

using namespace unitmorphs;
using namespace smartlarva;

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

//The game's ids (SCBW/enumerations/UnitId.h).
const u16 MARINE = 0, LARVA = 35, ZERGLING = 37, HYDRALISK = 38, MUTALISK = 43, GUARDIAN = 44, DRONE = 41,
          DEVOURER = 62, LURKER = 103, HATCHERY = 131;

} //unnamed namespace

int main() {
	//The table's rows.
	CHECK(isMorphSource(LARVA) && isMorphSource(HYDRALISK) && isMorphSource(MUTALISK) && isMorphSource(ZERGLING));
	CHECK(!isMorphSource(MARINE) && !isMorphSource(DRONE) && !isMorphSource(HATCHERY) && !isMorphSource(LURKER_EGG));
	CHECK(morphRowOf(MARINE) == NULL && morphRowOf(LARVA) != NULL && morphRowOf(LARVA)->source == LARVA);
	CHECK(eggOf(LARVA) == EGG && eggOf(HYDRALISK) == LURKER_EGG && eggOf(MUTALISK) == COCOON);
	CHECK(eggOf(ZERGLING) == LURKER_EGG && eggOf(MARINE) == NONE);

	//How a morph into a type is tested.
	CHECK(morphCheck(LARVA, DRONE) == CHECK_MAKE && morphCheck(MUTALISK, GUARDIAN) == CHECK_MAKE);
	CHECK(morphCheck(HYDRALISK, LURKER) == CHECK_TECH);
	CHECK(morphCheck(HYDRALISK, GUARDIAN) == CHECK_NONE);	//a Hydralisk becomes only a Lurker
	CHECK(morphCheck(ZERGLING, 200) == CHECK_MAKE);			//the Baneling: the requirements decide
	CHECK(morphCheck(MARINE, DRONE) == CHECK_NONE && morphCheck(HATCHERY, DRONE) == CHECK_NONE);
	CHECK(morphRowOf(HYDRALISK)->tech == 0x20);				//Lurker Aspect

	//What a cancelled egg becomes.
	CHECK(cancelRevert(LURKER_EGG, HYDRALISK) == HYDRALISK);
	CHECK(cancelRevert(LURKER_EGG, ZERGLING) == ZERGLING);
	CHECK(cancelRevert(COCOON, MUTALISK) == MUTALISK);
	CHECK(cancelRevert(LURKER_EGG, MARINE) == HYDRALISK);	//not a source of that egg: vanilla's
	CHECK(cancelRevert(COCOON, ZERGLING) == MUTALISK);
	CHECK(cancelRevert(EGG, LARVA) == NONE);				//removed, as vanilla
	CHECK(cancelRevert(MARINE, LARVA) == NONE);

	//An unfinished unit cancel leaves alone.
	CHECK(keptFromCancel(HYDRALISK, MARINE) && keptFromCancel(MUTALISK, MARINE));	//vanilla's
	CHECK(keptFromCancel(ZERGLING, LURKER_EGG));		//back from its egg
	CHECK(keptFromCancel(200, LURKER_EGG));				//a Baneling hatching
	CHECK(keptFromCancel(GUARDIAN, COCOON) && keptFromCancel(DEVOURER, COCOON) && keptFromCancel(LURKER, LURKER_EGG));
	CHECK(!keptFromCancel(LARVA, MARINE));				//a larva's egg is removed, never reverts
	CHECK(!keptFromCancel(MARINE, MARINE) && !keptFromCancel(DRONE, EGG));	//as vanilla

	//The command a press sends.
	CHECK(morphCommandId(true) == CMD_MORPH_ALL && CMD_MORPH_ALL == 0x23);
	CHECK(morphCommandId(false) == CMD_MORPH_ONE && CMD_MORPH_ONE == 0x3F && MORPH_ONE_BYTES == 3);

	//The pick: the first of the largest group, ties to the group met first.
	const u32 h1 = 1000, h2 = 2000, h3 = 3000;
	const u32 threeTwoOne[] = { h3, h2, h1, h2, h1, h1 };
	CHECK(pickMorpher(threeTwoOne, 6) == 2);			//H1 has 3: its first is at 2
	const u32 tie[] = { h2, h1, h1, h2 };
	CHECK(pickMorpher(tie, 4) == 0);					//2 and 2: H2 comes first
	const u32 alone[] = { 11, 12, 13 };					//no Hatchery, or Hydralisks: each its own
	CHECK(pickMorpher(alone, 3) == 0);
	const u32 one[] = { h1 };
	CHECK(pickMorpher(one, 1) == 0);
	CHECK(pickMorpher(one, 0) == 0);					//none: n
	const u32 later[] = { 11, h1, h1 };
	CHECK(pickMorpher(later, 3) == 1);

	if (failures != 0) {
		printf("FAIL: %u checks (first at unit_morphs_test.cpp line %u)\n", failures, firstLine);
		return 1;
	}
	printf("PASS\n");
	return 0;
}
```

`tests/unit_morphs_test.bat`:

```bat
@echo off
rem Builds and runs the host test of the morph table and the smart larva pick.
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars32.bat" >nul
cd /d "%~dp0"
if not exist out mkdir out
cl /nologo /EHsc /W3 /I..\GPTP /Fo:out\ /Fe:out\unit_morphs_test.exe unit_morphs_test.cpp ..\GPTP\SCBW\unit_morphs.cpp ..\GPTP\SCBW\smart_larva.cpp > out\unit_morphs_build.log
if errorlevel 1 (
	type out\unit_morphs_build.log
	exit /b 1
)
out\unit_morphs_test.exe
```

- [ ] **Step 2: Run it to see it fail**

Run: `cmd /c tests\unit_morphs_test.bat`
Expected: build error (the headers don't exist).

- [ ] **Step 3: Write the rules**

`GPTP/SCBW/unit_morphs.h`:

```cpp
//The morph table (docs/superpowers/specs/2026-10-15-smart-larva-design.md §3):
//which units morph, and through which egg. To make a unit morphable, add its
//row in unit_morphs.cpp. Pure, so the host test covers it
//(tests/unit_morphs_test.cpp); hooks/recv_commands/smart_morph.cpp reads it
//at the exe's morph sites.
#pragma once
#include "../types.h"

namespace unitmorphs {

const u16 NONE = 0xE4;		//UnitId::None
//The three egg units the exe knows (AI, console, birth, cancel): a morph
//uses one of them.
const u16 EGG = 36, COCOON = 59, LURKER_EGG = 97;

struct MorphRow {
	u16 source;		//the unit that morphs
	u16 egg;		//EGG, COCOON or LURKER_EGG
	u16 techTarget;	//NONE: any type the requirements allow (canMakeUnit)
	u8 tech;		//with a techTarget: the tech that allows it (canUseTech)
};

//The row of a unit that morphs, or NULL.
const MorphRow* morphRowOf(u16 unitId);
bool isMorphSource(u16 unitId);
//The egg a unit is while it morphs; NONE if it doesn't morph.
u16 eggOf(u16 unitId);

enum MorphCheck { CHECK_NONE, CHECK_MAKE, CHECK_TECH };
//How to test whether a unit may morph into a type: it can't, the
//requirements decide (canMakeUnit), or the row's tech does (canUseTech).
MorphCheck morphCheck(u16 unitId, u16 type);

//What a cancelled egg becomes (cancel, 0x468351): the unit it was
//(previousType) when that unit morphs through this egg, else vanilla's
//(Cocoon: Mutalisk; Lurker Egg: Hydralisk). NONE for an egg that is removed
//(a larva's Egg) and for any other unit.
u16 cancelRevert(u16 eggId, u16 previousType);
//Whether cancel must leave an unfinished unit alone (0x4682E5): a source
//back from its egg, or a unit hatching from a Cocoon or Lurker Egg. Vanilla
//names Mutalisk and Hydralisk; without this a Zergling back from its egg, or
//a Baneling hatching, would be refunded a second time.
bool keptFromCancel(u16 unitId, u16 previousType);

} //unitmorphs
```

`GPTP/SCBW/unit_morphs.cpp`:

```cpp
#include "unit_morphs.h"
#include <cstddef>

namespace unitmorphs {

namespace {

//The game's ids (SCBW/enumerations/UnitId.h, TechId.h).
const u16 LARVA = 35, ZERGLING = 37, HYDRALISK = 38, MUTALISK = 43, LURKER = 103;
const u8 LURKER_ASPECT = 0x20;

//The morph table. A row makes its source morphable: while it morphs it is
//its egg, and it may become what the requirements allow (FireGraft's "current
//unit is"), or, with a techTarget, only that type once the tech is
//researched. A morph button is Unit_Morph (0x423790) with the target's id.
const MorphRow ROWS[] = {
	{ LARVA,		EGG,		NONE,	0 },
	{ HYDRALISK,	LURKER_EGG,	LURKER,	LURKER_ASPECT },
	{ MUTALISK,		COCOON,		NONE,	0 },
	{ ZERGLING,		LURKER_EGG,	NONE,	0 },	//the Baneling (the user's data)
};

} //unnamed namespace

const MorphRow* morphRowOf(u16 unitId) {
	for (u32 i = 0; i < sizeof(ROWS) / sizeof(ROWS[0]); i++)
		if (ROWS[i].source == unitId)
			return &ROWS[i];
	return NULL;
}

bool isMorphSource(u16 unitId) {
	return morphRowOf(unitId) != NULL;
}

u16 eggOf(u16 unitId) {
	const MorphRow* const row = morphRowOf(unitId);
	return row != NULL ? row->egg : NONE;
}

MorphCheck morphCheck(u16 unitId, u16 type) {
	const MorphRow* const row = morphRowOf(unitId);
	if (row == NULL)
		return CHECK_NONE;
	if (row->techTarget == NONE)
		return CHECK_MAKE;
	return type == row->techTarget ? CHECK_TECH : CHECK_NONE;
}

u16 cancelRevert(u16 eggId, u16 previousType) {
	if (eggId != COCOON && eggId != LURKER_EGG)
		return NONE;
	if (eggOf(previousType) == eggId)
		return previousType;
	return eggId == COCOON ? MUTALISK : HYDRALISK;
}

bool keptFromCancel(u16 unitId, u16 previousType) {
	const u16 egg = eggOf(unitId);
	if (egg != NONE && egg != EGG)
		return true;
	return previousType == COCOON || previousType == LURKER_EGG;
}

} //unitmorphs
```

`GPTP/SCBW/smart_larva.h`:

```cpp
//Smart larva morphing (docs/superpowers/specs/2026-10-15-smart-larva-design.md):
//a plain press of a morph button morphs one selected unit, Shift+press all.
//Pure, so the host test covers it (tests/unit_morphs_test.cpp);
//hooks/recv_commands/smart_morph.cpp applies it.
#pragma once
#include "../types.h"

namespace smartlarva {

//The game's morph command, [0x23][u16 type]: every selected unit that can.
const u8 CMD_MORPH_ALL = 0x23;
//Morph one, [0x3F][u16 type]. Beside the plugin's 0x3C, 0x3D (SCBW/
//selection_ext.h) and 0x3E (SCBW/smart_rally.h).
const u8 CMD_MORPH_ONE = 0x3F;
const u32 MORPH_ONE_BYTES = 3;

//The command a morph button's press sends: with Shift the game's (all), else
//one.
u8 morphCommandId(bool shiftHeld);

//Which candidate a plain press morphs. keys[i] is candidate i's group, in
//selection order: a larva's Hatchery, else the unit itself. The first unit of
//the largest group; ties go to the group met first. n when there is none.
u32 pickMorpher(const u32* keys, u32 n);

} //smartlarva
```

`GPTP/SCBW/smart_larva.cpp`:

```cpp
#include "smart_larva.h"

namespace smartlarva {

u8 morphCommandId(bool shiftHeld) {
	return shiftHeld ? CMD_MORPH_ALL : CMD_MORPH_ONE;
}

u32 pickMorpher(const u32* keys, u32 n) {
	u32 best = n;
	u32 bestCount = 0;
	for (u32 i = 0; i < n; i++) {
		u32 count = 0;
		for (u32 j = 0; j < n; j++)
			if (keys[j] == keys[i])
				count++;
		if (count > bestCount) {
			best = i;
			bestCount = count;
		}
	}
	return best;
}

} //smartlarva
```

- [ ] **Step 4: Run it to see it pass**

Run: `cmd /c tests\unit_morphs_test.bat`
Expected: `PASS`

- [ ] **Step 5: verify.ps1 runs it** (after the smart rally test's two lines):

```powershell
& "$PSScriptRoot\unit_morphs_test.bat" 2>$null
if ($LASTEXITCODE -ne 0) { Write-Output "unit morphs test FAILED"; exit 1 }
```

- [ ] **Step 6: Commit** `Smart larva: the morph table and the pick (host-tested)`.

### Task 2: 0x3F's length and Cancel Last from the table

**Files:**
- Modify: `GPTP/SCBW/selection_ext_core.cpp` (`variableCommandLength`, `cancelsTraining`), `GPTP/SCBW/selection_ext.h` (comment)
- Modify: `GPTP/hooks/selection_ext/sel_selftest.cpp`, `tests/selection_ext_test.bat` (link `unit_morphs.cpp`)

**Interfaces:**
- Consumes: `smartlarva::CMD_MORPH_ONE`, `MORPH_ONE_BYTES`, `unitmorphs::isMorphSource`.

- [ ] **Step 1: Failing self-test checks** in `sel_selftest.cpp`, beside the worker rally length check:

```cpp
	const u8 morphOne[] = { smartlarva::CMD_MORPH_ONE, 0, 0 };
	CHECK(variableCommandLength(morphOne) == smartlarva::MORPH_ONE_BYTES);
```

and after the Cancel Last morph types line:

```cpp
	CHECK(!cancelsTraining(37, true, true));	//a Zergling: a table source (SCBW/unit_morphs.cpp)
```

(include `<SCBW/smart_larva.h>` there.)

- [ ] **Step 2: Run** `cmd /c tests\selection_ext_test.bat`. Expected: FAIL (two checks).
- [ ] **Step 3: Implement.** `variableCommandLength`: `if (id == smartlarva::CMD_MORPH_ONE) return smartlarva::MORPH_ONE_BYTES;` before the 0x09–0x0B line. `cancelsTraining`: drop cases 35/38/43 and add `if (unitmorphs::isMorphSource(unitType)) return false;` before the final return. Comment in `selection_ext.h`: "(a morph table source, SCBW/unit_morphs.h; Hatchery, Lair, Spire, Creep Colony)". Add `..\GPTP\SCBW\unit_morphs.cpp` to `selection_ext_test.bat`'s `cl` line.
- [ ] **Step 4: Run** `cmd /c tests\selection_ext_test.bat`. Expected: PASS.
- [ ] **Step 5: Commit** `Smart larva: 0x3F's length, Cancel Last from the morph table`.

### Task 3: The hooks

**Files:**
- Create: `GPTP/hooks/recv_commands/smart_morph.h`, `smart_morph.cpp`, `smart_morph_inject.cpp`
- Modify: `GPTP/hooks/selection_ext/sel_inject.cpp` (0x3F dispatch; the button gate stub), `GPTP/initialize.cpp`, `GPTP/GPTP.vcxproj`, `tests/verify.ps1`, `tests/check_naked_wrappers.py` (header comment)

**Interfaces:**
- Consumes: Task 1's functions; `selsync::nextSelected`, `selext::SEL_MAX`.
- Produces: `smartmorph::morphSendAs(u8*)`, `u32 morphAllowed(CUnit*, const u8*)`, `void recvMorphOne(const u8*)`, `u32 morphsAtAll(CUnit*)`, `u32 eggForUnit(CUnit*)`, `u32 keptFromCancelUnit(CUnit*)`, `u32 cancelRevertOf(CUnit*)`, `u32 mayCreateWithSeveral(CUnit*)`; `hooks::injectSmartMorphHooks()`.

- [ ] **Step 1: `smart_morph.h`**

```cpp
//Smart larva morphing and the morph table in the game
//(docs/superpowers/specs/2026-10-15-smart-larva-design.md). Synced: it runs
//on every client from the command, except morphSendAs, which only picks the
//command the local player sends. The rules: SCBW/unit_morphs.h,
//SCBW/smart_larva.h.
#pragma once
#include <SCBW/api.h>

namespace smartmorph {

//0x423846: a morph button's command before it is sent, 0x3F without Shift.
void morphSendAs(u8* command);
//0x4C19BB in the game's morph receive (0x4C1990): whether a selected unit
//morphs into the command's type. While 0x3F runs, only its pick does.
u32 morphAllowed(CUnit* unit, const u8* command);
//Command 0x3F: one unit morphs, the pick (SCBW/smart_larva.h), through the
//game's receive.
void recvMorphOne(const u8* packet);
//Morph1 (0x45DFB0): whether the unit morphs at all.
u32 morphsAtAll(CUnit* unit);
//Morph1 (0x45E019): its egg, unitmorphs::NONE if none.
u32 eggForUnit(CUnit* unit);
//Cancel (0x4682E5): whether an unfinished unit is left alone.
u32 keptFromCancelUnit(CUnit* unit);
//Cancel (0x468351): what a cancelled egg becomes, unitmorphs::NONE for the
//game's own path (removed, or not an egg).
u32 cancelRevertOf(CUnit* unit);
//The button gate 0x428E60 with several units selected (0x428E89): a worker
//(smart-build) or a morph source shows its buttons.
u32 mayCreateWithSeveral(CUnit* unit);

} //smartmorph

namespace hooks {

void injectSmartMorphHooks();

} //hooks
```

- [ ] **Step 2: `smart_morph.cpp`**

```cpp
#include "smart_morph.h"
#include "../selection_ext/sel_synced.h"
#include <SCBW/unit_morphs.h>
#include <SCBW/smart_larva.h>
#include <SCBW/selection_ext.h>

namespace smartmorph {

namespace {

//The one unit 0x3F lets through the game's receive; NULL: every unit (0x23).
CUnit* onlyUnit;

bool canMorphInto(CUnit* unit, u16 type) {
	switch (unitmorphs::morphCheck(unit->id, type)) {
	case unitmorphs::CHECK_MAKE:
		return unit->canMakeUnit(type, *ACTIVE_NATION_ID) == 1;
	case unitmorphs::CHECK_TECH:
		return unit->canUseTech(unitmorphs::morphRowOf(unit->id)->tech, *ACTIVE_NATION_ID) == 1;
	default:
		return false;
	}
}

//A larva's group is its Hatchery; a unit without one is its own. Compared
//for equality only, so the same on every client.
u32 groupKey(CUnit* unit) {
	if (unit->id == UnitId::ZergLarva && unit->connectedUnit != NULL)
		return (u32)unit->connectedUnit;
	return (u32)unit;
}

//The game's morph receive, stdcall(command), RET 4.
const u32 Func_RecvUnitMorph = 0x004C1990;
void recvUnitMorph(const u8* packet) {
	static const u8* command;
	command = packet;
	__asm {
		PUSHAD
		PUSH command
		CALL Func_RecvUnitMorph
		POPAD
	}
}

CUnit* candidates[selext::SEL_MAX];
u32 keys[selext::SEL_MAX];

} //unnamed namespace

void morphSendAs(u8* command) {
	command[0] = smartlarva::morphCommandId(*IS_HOLDING_SHIFT != 0);
}

u32 morphAllowed(CUnit* unit, const u8* command) {
	if (onlyUnit != NULL && unit != onlyUnit)
		return 0;
	return canMorphInto(unit, *(const u16*)(command + 1)) ? 1 : 0;
}

void recvMorphOne(const u8* packet) {
	const u16 type = *(const u16*)(packet + 1);
	u32 n = 0;
	*selectionIndexStart = 0;
	for (CUnit* unit = selsync::nextSelected(); unit != NULL && n < selext::SEL_MAX; unit = selsync::nextSelected()) {
		if (unit->mainOrderId != OrderId::Morph1 && canMorphInto(unit, type)) {
			candidates[n] = unit;
			keys[n] = groupKey(unit);
			n++;
		}
	}
	const u32 pick = smartlarva::pickMorpher(keys, n);
	if (pick >= n)
		return;
	onlyUnit = candidates[pick];
	recvUnitMorph(packet);
	onlyUnit = NULL;
}

u32 morphsAtAll(CUnit* unit) {
	return unitmorphs::isMorphSource(unit->id) ? 1 : 0;
}

u32 eggForUnit(CUnit* unit) {
	return unitmorphs::eggOf(unit->id);
}

u32 keptFromCancelUnit(CUnit* unit) {
	return unitmorphs::keptFromCancel(unit->id, unit->previousUnitType) ? 1 : 0;
}

u32 cancelRevertOf(CUnit* unit) {
	return unitmorphs::cancelRevert(unit->id, unit->previousUnitType);
}

u32 mayCreateWithSeveral(CUnit* unit) {
	if (units_dat::BaseProperty[unit->id] & UnitProperty::Worker)
		return 1;
	return unitmorphs::isMorphSource(unit->id) ? 1 : 0;
}

} //smartmorph
```

- [ ] **Step 3: `smart_morph_inject.cpp`**

```cpp
//Smart larva morphing's hooks (hooks/recv_commands/smart_morph.h). The stubs
//are naked: static locals only, never [ebp...]; tests/check_naked_wrappers.py
//checks this file's object too. Addresses: the plan's "Exe facts".
#include "smart_morph.h"
#include <hook_tools.h>

namespace {

//0x423846 in the morph button's action 0x423790: its call of the send
//(ECX = [0x23][u16 type], EDX = 3), which this stub ends in.
const u32 Func_QueueCommandSend = 0x00485BD0;
void __declspec(naked) morphSendStub() {
	static u8* command;
	__asm {
		MOV command, ECX
		PUSHAD
	}
	smartmorph::morphSendAs(command);
	__asm {
		POPAD
		JMP Func_QueueCommandSend
	}
}

//0x4C19BB in the morph receive 0x4C1990: ESI = the unit, EBX = the command.
//Allowed goes on at 0x4C19FD (not already Morph1, pay, order); refused at
//0x4C1A74 (the next unit).
const u32 Recv_MorphAllowed = 0x004C19FD;
const u32 Recv_MorphRefused = 0x004C1A74;
void __declspec(naked) morphAllowedStub() {
	static CUnit* unit;
	static const u8* command;
	static u32 allowed;
	__asm {
		MOV unit, ESI
		MOV command, EBX
		PUSHAD
	}
	allowed = smartmorph::morphAllowed(unit, command);
	__asm {
		POPAD
		CMP allowed, 0
		JE refused
		JMP Recv_MorphAllowed
	refused:
		JMP Recv_MorphRefused
	}
}

//0x45DFB0, Morph1's first state (ESI = the unit): a unit that morphs goes on
//at 0x45DFCA, else Morph1 returns (0x45E07C).
const u32 Morph1_Goes = 0x0045DFCA;
const u32 Morph1_Return = 0x0045E07C;
void __declspec(naked) morph1SourceStub() {
	static CUnit* unit;
	static u32 morphs;
	__asm {
		MOV unit, ESI
		PUSHAD
	}
	morphs = smartmorph::morphsAtAll(unit);
	__asm {
		POPAD
		CMP morphs, 0
		JE stop
		JMP Morph1_Goes
	stop:
		JMP Morph1_Return
	}
}

//0x45E019, Morph1's egg choice: clears status bit 1 (not completed), pushes
//the egg and goes on at 0x45E048 (the type change 0x49FED0).
const u32 Morph1_ToEgg = 0x0045E048;
void __declspec(naked) morph1EggStub() {
	static CUnit* unit;
	static u32 egg;
	__asm {
		MOV unit, ESI
		PUSHAD
	}
	egg = smartmorph::eggForUnit(unit);
	__asm {
		POPAD
		AND DWORD PTR [ESI+0xDC], 0xFFFFFFFE
		CMP egg, 0xE4
		JE stop
		PUSH egg
		JMP Morph1_ToEgg
	stop:
		JMP Morph1_Return
	}
}

//0x4682E5 in cancel 0x468280 (ESI = an unfinished unit; AL = its status, DX
//= its id, both read on at 0x4682F9): a unit kept from cancel returns
//(0x468418).
const u32 Cancel_GoesOn = 0x004682F9;
const u32 Cancel_Return = 0x00468418;
void __declspec(naked) cancelKeptStub() {
	static CUnit* unit;
	static u32 kept;
	__asm {
		MOV unit, ESI
		PUSHAD
	}
	kept = smartmorph::keptFromCancelUnit(unit);
	__asm {
		POPAD
		CMP kept, 0
		JE goesOn
		JMP Cancel_Return
	goesOn:
		JMP Cancel_GoesOn
	}
}

//0x468351 in cancel, after the refund: an egg that reverts pushes the unit
//it becomes and goes on at 0x46839E (the type change); anything else goes on
//at 0x468361 with AX = its id.
const u32 Cancel_Revert = 0x0046839E;
const u32 Cancel_NotReverted = 0x00468361;
void __declspec(naked) cancelRevertStub() {
	static CUnit* unit;
	static u32 revert;
	__asm {
		MOV unit, ESI
		PUSHAD
	}
	revert = smartmorph::cancelRevertOf(unit);
	__asm {
		POPAD
		CMP revert, 0xE4
		JE notReverted
		PUSH revert
		JMP Cancel_Revert
	notReverted:
		MOV AX, WORD PTR [ESI+0x64]
		JMP Cancel_NotReverted
	}
}

} //unnamed namespace

namespace hooks {

void injectSmartMorphHooks() {
	callPatch(morphSendStub,		0x00423846, 0);
	jmpPatch(morphAllowedStub,		0x004C19BB, 0);
	jmpPatch(morph1SourceStub,		0x0045DFB0, 0);
	jmpPatch(morph1EggStub,			0x0045E019, 0);
	jmpPatch(cancelKeptStub,		0x004682E5, 0);
	jmpPatch(cancelRevertStub,		0x00468351, 0);
}

} //hooks
```

- [ ] **Step 4: `sel_inject.cpp`.** Include `"../recv_commands/smart_morph.h"` and `<SCBW/smart_larva.h>`. Add `void __cdecl recvMorphOneC(const u8* packet) { smartmorph::recvMorphOne(packet); }` beside `recvWorkerRallyC`. In `selectChunkDispatch`, after the 0x3E test: `CMP BYTE PTR [ESI], 0x3F` / `JE morphOne`, and the block (before `notOurs:`):

```cpp
	morphOne:
		CMP EAX, 3
		JL notOurs
		SUB EAX, 3
		MOV [EBP+8], EAX
		MOV DWORD PTR [EBP-4], 3
		PUSHAD
		PUSH ESI
		CALL recvMorphOneC
		ADD ESP, 4
		POPAD
		JMP Back_CommandDone
```

Update its comment ("0x3F: smart larva's morph one (3 bytes)"). Replace `canCreateWorkerStub`'s body: a worker or a morph source is allowed:

```cpp
//0x428E89, Can_Create_UnitorBuilding with several units selected and the
//unit none of Larva/Mutalisk/Hydralisk: workers may build too (smart-build),
//and any morph table source may morph (SCBW/unit_morphs.h). EAX (the type),
//ESI (the unit) and EDX (the player) are kept for 0x428E91.
const u32 CanCreate_Allowed = 0x00428E91;
void __declspec(naked) canCreateSeveralStub() {
	static CUnit* unit;
	static u32 allowed;
	__asm {
		MOV unit, ESI
		PUSHAD
	}
	allowed = smartmorph::mayCreateWithSeveral(unit);
	__asm {
		POPAD
		CMP allowed, 0
		JNE allowedHere
		POP EDI
		XOR EAX, EAX
		POP ESI
		POP EBP
		RETN 4
	allowedHere:
		JMP CanCreate_Allowed
	}
}
```

and rename it at its `jmpPatch`.

- [ ] **Step 5: Register and build.** `initialize.cpp`: `#include "hooks/recv_commands/smart_morph.h"` and, after `hooks::injectSmartRallyHooks();`:

```cpp
	//Smart larva morphing and the morph table (hooks/recv_commands/smart_morph, SCBW/unit_morphs.h).
	hooks::injectSmartMorphHooks();
```

`GPTP.vcxproj`: ClCompile `SCBW\unit_morphs.cpp`, `SCBW\smart_larva.cpp`, `hooks\recv_commands\smart_morph.cpp`, `hooks\recv_commands\smart_morph_inject.cpp`; ClInclude the three headers. `verify.ps1`: a third naked check over `smart_morph_inject.obj` ("naked wrapper check FAILED (smart morph)"). `check_naked_wrappers.py`'s header names the three files.

Run: `powershell -ExecutionPolicy Bypass -File tests/verify.ps1`
Expected: every test PASS, `plugin build succeeded`.

- [ ] **Step 6: Commit** `Smart larva: one per press, Shift all; the morph table at the exe's morph sites`.

### Task 4: Docs

- Spec status `[BUILT 2026-10-15]` (not yet run in game), the exe facts pointer to this plan; `CLAUDE.md` module list (smart larva) and the synced list; `docs/resolution.md` §6 entry with the in-game round; memory.
- Commit `Docs: smart larva built (not yet run in game)`.
