# Smart-build implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** several selected workers build SC2-style: a plain placement goes to the nearest able worker; Shift-placement queues buildings shared out across the workers (balanced; Drones one each, recycled by earliest assignment); Shift keeps placing; releasing it keeps the build menu.

**Architecture:** the build conditions are patched in place (no one-unit requirement; workers allowed in Can_Create_UnitorBuilding). Plain placement keeps command 0x0C, whose receive now picks the nearest able worker and feeds it to vanilla's placement path. Shift-placement sends a new 0x3D; its receive picks a worker and either starts the build now (worker not building) through the same path, or appends it to the worker's BW order queue with the type marked 0x8000. The main order dispatcher (0x4EC4D0) sets up a marked build when it becomes current, running 0x48DE70's command-time checks. Local: the placement click sends 0x3D and stays in placing mode with Shift; a per-frame check ends placing when Shift is released.

**Tech stack:** C++ (MSVC, Win32), GPTP hook tools, MSVC inline asm.

**Spec:** `docs/superpowers/specs/2026-10-03-smart-build-design.md`.

## Global constraints
- Synced (receive, the order hook, assignment numbers): command data and game state only; never `LOCAL_*`, the UI arrays or the subgroup state.
- Hooks one per line in `initialize.cpp` (enabled list); naked stubs in `hooks/selection_ext/sel_inject.cpp` (the naked-wrapper check), statics and registers only.
- Files are stored LF: plain `git add`. `tests/verify.ps1` (host test, plugin build, naked check; run with `powershell -ExecutionPolicy Bypass`) after every task.

## Verified facts (2026-10-03)
- Build conditions' count branches (after `cmp byte [0x59723D], 1`, with only push/mov between): `jne` at 0x42899E (2 bytes), 0x428A1E (2), 0x428ADE (6), 0x428B8E (6), 0x428C3E (2), 0x428CBE (2). 0x428E60: count ≤ 1 → `jbe 0x428E91`; Larva 0x23 / Mutalisk 0x2B / Hydralisk 0x26 → 0x428E91; else 0x428E89 `pop edi; xor eax,eax; pop esi; pop ebp; ret 4` (8 bytes). At 0x428E91 `push edx; call 0x46E1C0` uses EAX (the type), ESI (unit), EDX: all must survive.
- Placement click (placing mode's left-click proc 0x48E5D0): 0x48E62E `call 0x485BD0` (ECX = the 8-byte command at [ebp-0xC], EDX = 8), 0x48E633 `push 0`, 0x48E635 `call 0x4843F0` (input mode back to normal = placing ends, card back to basic), 0x48E63A `mov esp, ebp`. 0x48E310 cancels placing (as Esc). Placing: `[0x640880]` nonzero; placed type `[0x64088A]` (u16), order `[0x64088D]` (u8).
- 0x48DDA0: `ecx = [0x597248]` (portrait), `ax = [0x64088A]`, `dl = [0x64088D]`, `jmp 0x48DBD0`; returns EAX. Vanilla cancels placing when a constructing SCV makes it false.
- 0x0C receive 0x4C23C0 (ESI = packet, `ret`); 0x48DBD0 (ECX unit, DL order, AX type → EAX); 0x48E190 (CL order, AX type, push dword tiles, `ret 4`); its builders come from `call 0x49A850` at 0x48E01E / 0x48E0B1.
- 0x48DE70's command-time checks: `[0x6CA51C + 4*p]` = mineral cost, `[0x6CA4EC + 4*p]` = gas cost, 0x42CF70 (stdcall player, type, showError) supply, minerals then gas compared with errors 0x352 / 0x353 and sfx `Zerg_Advisor_ZAdErr00_WAV_2` / `Zerg_Advisor_ZAdErr01_WAV` + `[0x57F1E2]` (u8); then 0x466E80 (EAX unit) and 0x467250 (EDI unit, push type). GPTP wrappers for all of these are in `hooks/recv_commands/CMDRECV_Build.cpp` (unnamed namespace; copy them).
- Vanilla queueing (0x4754F0): append when the unit isn't idle-ish, at most 8 (`orderQueueCount` +0x84, error stat_txt 0x367), global count `[0x641698]` < 1800 (error 0x369); append via `performAnotherOrder` (0x4745F0, `unkOrder` NULL = at the end). 0x475000 copies a queued entry's unit type into `orderUnitType` (+0x50) unless 0xE4.
- Main order dispatcher 0x4EC4D0: `push ebx; push esi; mov esi, eax; movzx eax, byte [esi+0x4D]` (8 bytes), then 0x4EC4D8. EAX = unit.
- Dispatch slot 50 (0x486ED0) stub `selectChunkDispatch` (ESI = command, EAX = bytes left, `[ebp-4]` = length, `[ebp+8]` = bytes left after it; done → 0x486D7A, other ids → 0x486DA3). 0x3D is unused and recorded in replays.
- `CUnit::getIndex()` = `this - unitTable + 1` (1-based, `UNIT_ARRAY_LENGTH` 1700). GPTP's per-frame hook: `nextFrame()` in `hooks/main/game_hooks.cpp` (enabled).

## Review focus
1. **A plain placement with one unit selected is vanilla** (same builder, checks, errors). 9.5.
2. **Synced only:** the 0x3D pick and the order hook read the iterator's selection, positions, queues and the stamps; nothing local. Host tests for the picks; 9.8 replay.
3. **A marked build that fails its checks moves on** (next queued order or idle) and never runs with an empty build slot. 9.4.
4. **Shift released or Esc while placing** never leaves the cursor stuck. 9.2.
5. **Saves mid-queue:** the queue is in the units (CGame), the marks in the queue entries; stamps in SELX v3; v1/v2 saves load with stamps 0. 9.7.

---

### Task 1: picks and the new command's length (pure, host-tested)
**Files:** `GPTP/SCBW/selection_ext.h`, `selection_ext_core.cpp`, `hooks/selection_ext/sel_selftest.cpp`.

**Produces** (namespace `selext`):
```cpp
//-------- Smart-build --------//

//Queued build: [0x3D][order][u16 x tile][u16 y tile][u16 type], as 0x0C.
const u8 CMD_QUEUED_BUILD = 0x3D;
//A queued build order's type, marked so the order hook sets it up when it
//becomes current (vanilla never starts a build from the queue).
const u16 QUEUED_BUILD_MARK = 0x8000;
//Index of the able entry nearest (x, y) (squared distance), ties to the
//lowest index, or -1.
int nearestIndex(const s32* xs, const s32* ys, const bool* able, u32 n, s32 x, s32 y);
//Balanced pick: the able entry with the fewest builds; then nearest (x, y)
//from fromX/fromY; then the lowest index; or -1.
int pickBalanced(const u32* builds, const s32* fromX, const s32* fromY, const bool* able,
                 u32 n, s32 x, s32 y);
//Drone pick: a free one, nearest (x, y); else the recyclable one with the
//lowest stamp, ties to the lowest index; or -1.
int pickDrone(const bool* isFree, const bool* recyclable, const u32* stamps,
              const s32* xs, const s32* ys, u32 n, s32 x, s32 y);
```
and `variableCommandLength` returns 8 for `CMD_QUEUED_BUILD`.
- [ ] RED: declarations; stubs returning -2; `variableCommandLength` unchanged; tests:
```cpp
void buildPicks() {
	const s32 xs[4] = { 100, 10, 50, 50 }, ys[4] = { 100, 10, 50, 50 };
	const bool able[4] = { true, false, true, true };
	CHECK(nearestIndex(xs, ys, able, 4, 0, 0) == 2);	//1 nearer but unable; 2 and 3 tie
	CHECK(nearestIndex(xs, ys, able, 0, 0, 0) == -1);
	const s32 far[2] = { 8191, 0 };
	const bool both[2] = { true, true };
	CHECK(nearestIndex(far, far, both, 2, 0, 8191) == 0);	//no overflow at map size

	const u32 builds[4] = { 2, 0, 1, 1 };
	CHECK(pickBalanced(builds, xs, ys, able, 4, 0, 0) == 2);	//fewest able (1 unable)
	const u32 even[4] = { 1, 1, 1, 1 };
	CHECK(pickBalanced(even, xs, ys, able, 4, 200, 200) == 0);	//tie: nearest
	CHECK(pickBalanced(even, xs, ys, able, 4, 50, 50) == 2);	//tie on distance: index
	const bool none[4] = { false, false, false, false };
	CHECK(pickBalanced(even, xs, ys, none, 4, 0, 0) == -1);

	const bool isFree[3] = { false, true, true };
	const bool recyclable[3] = { true, false, true };
	const u32 stamps[3] = { 7, 0, 3 };
	const s32 dx[3] = { 0, 90, 10 }, dy[3] = { 0, 0, 0 };
	CHECK(pickDrone(isFree, recyclable, stamps, dx, dy, 3, 0, 0) == 2);	//free, nearest
	const bool busy[3] = { false, false, false };
	CHECK(pickDrone(busy, recyclable, stamps, dx, dy, 3, 0, 0) == 2);	//earliest stamp 3
	const u32 zeros[3] = { 0, 0, 0 };
	CHECK(pickDrone(busy, recyclable, zeros, dx, dy, 3, 90, 0) == 0);	//old save: lowest index
	const bool stuck[3] = { false, false, false };
	CHECK(pickDrone(busy, stuck, stamps, dx, dy, 3, 0, 0) == -1);
}
```
and in `lengths()`: `const u8 queued[] = { CMD_QUEUED_BUILD, 0x1E, 0, 0, 0, 0, 0, 0 }; CHECK(variableCommandLength(queued) == 8);`. Host test: FAIL (first at the `nearestIndex` line).
- [ ] GREEN:
```cpp
int nearestIndex(const s32* xs, const s32* ys, const bool* able, u32 n, s32 x, s32 y) {
	int best = -1;
	u32 bestDistance = 0;
	for (u32 i = 0; i < n; i++) {
		if (!able[i])
			continue;
		const s32 dx = xs[i] - x, dy = ys[i] - y;
		const u32 distance = (u32)(dx * dx) + (u32)(dy * dy);
		if (best < 0 || distance < bestDistance) {
			best = (int)i;
			bestDistance = distance;
		}
	}
	return best;
}

int pickBalanced(const u32* builds, const s32* fromX, const s32* fromY, const bool* able,
                 u32 n, s32 x, s32 y) {
	u32 fewest = 0xFFFFFFFF;
	for (u32 i = 0; i < n; i++)
		if (able[i] && builds[i] < fewest)
			fewest = builds[i];
	static bool candidate[SEL_MAX];
	for (u32 i = 0; i < n && i < SEL_MAX; i++)
		candidate[i] = able[i] && builds[i] == fewest;
	return nearestIndex(fromX, fromY, candidate, n < SEL_MAX ? n : SEL_MAX, x, y);
}

int pickDrone(const bool* isFree, const bool* recyclable, const u32* stamps,
              const s32* xs, const s32* ys, u32 n, s32 x, s32 y) {
	const int free = nearestIndex(xs, ys, isFree, n, x, y);
	if (free >= 0)
		return free;
	int best = -1;
	for (u32 i = 0; i < n; i++)
		if (recyclable[i] && (best < 0 || stamps[i] < stamps[best]))
			best = (int)i;
	return best;
}
```
and in `variableCommandLength`: `if (id == CMD_QUEUED_BUILD) return 8;`. Host test PASS; verify PASS; commit `feat: smart-build picks and the queued build command's length`.

### Task 2: the build conditions allow several workers
**Files:** `hooks/selection_ext/sel_inject.cpp`, `selection_ext_hooks.h`, `initialize.cpp`.
- [ ] Stub for 0x428E89 (EAX = type, ESI = unit, EDX = player; all kept):
```cpp
//0x428E89, Can_Create_UnitorBuilding with several units selected and the
//unit none of Larva/Mutalisk/Hydralisk: workers may build too (smart-build).
const u32 CanCreate_Allowed = 0x00428E91;
void __declspec(naked) canCreateWorkerStub() {
	__asm {
		PUSH EAX
		PUSH ECX
		MOVZX EAX, WORD PTR [ESI+0x64]
		MOV ECX, 0x00664080				//units_dat::BaseProperty
		TEST BYTE PTR [ECX+EAX*4], 0x08	//UnitProperty::Worker
		POP ECX
		POP EAX
		JNZ allowed
		POP EDI
		XOR EAX, EAX
		POP ESI
		POP EBP
		RETN 4
	allowed:
		JMP CanCreate_Allowed
	}
}
```
and `hooks::injectSmartBuildHooks()` (declared in `selection_ext_hooks.h`: "Smart-build: several workers build; Shift queues buildings."):
```cpp
void injectSmartBuildHooks() {
	//The build-menu conditions no longer need one unit selected (each
	//button's condition already runs once per member, stage 5).
	static const u8 NOP2[2] = { 0x90, 0x90 };
	static const u8 NOP6[6] = { 0x90, 0x90, 0x90, 0x90, 0x90, 0x90 };
	memoryPatch(0x0042899E, NOP2, 2);	//SCV basic
	memoryPatch(0x00428A1E, NOP2, 2);	//SCV advanced
	memoryPatch(0x00428ADE, NOP6, 6);	//Probe basic
	memoryPatch(0x00428B8E, NOP6, 6);	//Probe advanced
	memoryPatch(0x00428C3E, NOP2, 2);	//Drone basic
	memoryPatch(0x00428CBE, NOP2, 2);	//Drone advanced
	jmpPatch(canCreateWorkerStub,	0x00428E89, 3);
}
```
(if `hook_tools.h` has no `memoryPatch(addr, const void*, size)` overload, use `nops(addr, count)` or one `memoryPatch(addr, (u8)0x90)` per byte). `initialize.cpp`: `hooks::injectSmartBuildHooks();` after `injectCommandCardHooks()`. Verify (naked check); commit `feat: the build conditions allow several workers`.

### Task 3: receive (0x0C nearest, 0x3D picks, queued builds starting)
**Files:** create `hooks/selection_ext/sel_build.h/.cpp` (namespace `selbuild`); modify `sel_exe.h/.cpp`, `sel_inject.cpp`, `sel_local.cpp` (`gameStartClear` calls `selbuild::reset()`), `GPTP.vcxproj`.

**Produces:**
```cpp
namespace selbuild {
//Synced: per unit (index - 1) the assignment stamp of its last smart-build
//pick, and per player the next stamp. Saved (SELX v3).
extern u32 stamps[1700];
extern u32 nextStamp[8];
//While 0x48E190 runs for a picked builder, the builder 0x48E010/0x48E0A0
//take instead of the selection's first unit; NULL otherwise.
extern CUnit* chosenBuilder;
void recvBuild(const u8* packet);		//0x0C (0x4C23C0)
void recvQueuedBuild(const u8* packet);	//0x3D
//Before the order dispatcher runs unit's order: sets up a marked queued build.
void beforeOrder(CUnit* unit);
void reset();
}
```
- [ ] `sel_exe`: wrappers (copied from `CMDRECV_Build.cpp`'s helpers, in `sel_exe.cpp`'s style): `bool placeBuildingAllowed(CUnit*, u8 order, u16 type)` (0x48DBD0), `void placeBuilding(u8 order, u16 type, u32 tiles)` (0x48E190), `u32 placementCheck(CUnit*, u8 player, s32 tileX, s32 tileY, u16 type)` (0x473FB0 with unk 1,0,0,0), `bool placementMessage(u32 result)` (0x48D930), `bool hasSupplies(u16 type, u8 player)` (0x42CF70, showError 1), `void refundQueueSlots(CUnit*)` (0x466E80), `bool fillBuildSlot(CUnit*, u16 type)` (0x467250), `void queueOrderAtEnd(CUnit*, u8 order, s16 x, s16 y, u16 type)` (0x4745F0 via `CUnit::performAnotherOrder(order, x, y, NULL, type, NULL)`).
- [ ] `sel_build.cpp` (helpers in the unnamed namespace):
```cpp
const u8 ORDER_DRONE_START_BUILD = 0x19, ORDER_DRONE_BUILD = 0x1A, ORDER_DRONE_LAND = 0x46;
const u8 ORDER_BUILD_TERRAN = 0x1E, ORDER_BUILD_PROTOSS1 = 0x1F;
bool isBuildOrder(u8 order) {
	return order == ORDER_DRONE_START_BUILD || order == ORDER_BUILD_TERRAN || order == ORDER_BUILD_PROTOSS1;
}
//The site's centre in pixels, as 0x48E010 computes it.
void siteCentre(u16 tileX, u16 tileY, u16 type, s32* x, s32* y) {
	s32 w = (s16)units_dat::BuildingDimensions[type].x, h = (s16)units_dat::BuildingDimensions[type].y;
	if (w < 0) w++;
	if (h < 0) h++;
	*x = tileX * 32 + w / 2;
	*y = tileY * 32 + h / 2;
}
//Build orders a unit holds: current plus queued; last queued site.
u32 buildsOf(CUnit* unit, s32* lastX, s32* lastY) {
	u32 n = isBuildOrder(unit->mainOrderId) ? 1 : 0;
	*lastX = unit->position.x; *lastY = unit->position.y;
	if (n) { *lastX = unit->orderTarget.pt.x; *lastY = unit->orderTarget.pt.y; }
	for (COrder* o = unit->orderQueueHead; o != NULL; o = o->next)
		if (isBuildOrder((u8)o->orderId)) { n++; *lastX = o->target.pt.x; *lastY = o->target.pt.y; }
	return n;
}
//0x48DE70's test for a Drone already landing or morphing.
bool droneCommitted(CUnit* unit) {
	return unit->mainOrderId == ORDER_DRONE_BUILD ||
	       (unit->mainOrderId == ORDER_DRONE_LAND &&
	        (unit->status & (UnitStatus::NoBrkCodeStart | UnitStatus::CanNotReceiveOrders)));
}
//The selection (synced, through the iterator), max SEL_MAX.
u32 collectSelection(CUnit** units) { ... *selectionIndexStart = 0; loop getActivePlayerNextSelection() ... }
//Starts a build now with builder through vanilla's path.
void buildNow(CUnit* builder, u8 order, u16 type, u32 tiles) {
	chosenBuilder = builder;
	selexe::placeBuilding(order, type, tiles);
	chosenBuilder = NULL;
}
```
`recvBuild`: read order/tiles/type; return if `type >= UNIT_TYPES` or the tiles are off the map (`mapTileSize`); collect; `able[i] = placeBuildingAllowed(u, order, type) && (code = placementCheck(u, player, tileX, tileY, type)) == 0`, keeping the first nonzero `code` (rule 1: the check includes reaching the site, code 7); positions; `k = nearestIndex(...)` to the site centre; if `k >= 0` `buildNow(units[k], order, type, tiles)`, else if a code was kept `placementMessage(code)`.
`recvQueuedBuild`: as `recvBuild` up to `able`; if the order isn't Drone/Terran/Protoss1 → `recvBuild(packet)` and return; per unit `builds[i] = buildsOf(u, &fx[i], &fy[i])`; for Drones (`order == ORDER_DRONE_START_BUILD`): `isFree[i] = able[i] && builds[i] == 0`, `recyclable[i] = able[i] && builds[i] > 0 && !droneCommitted(u)`, `k = pickDrone(isFree, recyclable, stampOf, xs, ys, n, cx, cy)`; else `k = pickBalanced(builds, fx, fy, able, n, cx, cy)`. `k < 0` → `placementMessage(firstCode)` if one was kept, and return. Stamp: `stamps[idx] = ++nextStamp[player]`. If `builds[k] == 0` or Drone → `buildNow(...)`. Else (append): `if (u->orderQueueCount >= 8)` show stat_txt 0x367 to the player and return; `if (*(u32*)0x00641698 >= 1800)` 0x369 and return; `queueOrderAtEnd(u, order, cx, cy, type | QUEUED_BUILD_MARK)` (the site already passed `placementCheck` for this worker in `able`).
`beforeOrder(unit)`: `if (!(unit->orderUnitType & QUEUED_BUILD_MARK) || !isBuildOrder(unit->mainOrderId)) return;` `type = orderUnitType & ~MARK`; `unit->orderUnitType = type`; `p = unit->playerId`; costs into 0x6CA51C/0x6CA4EC as 0x48DE70; `ok = hasSupplies(type, p)`; minerals then gas with the 0x352/0x353 errors (`scbw::showErrorMessageWithSfx(p, id, sfx + *(u8*)0x0057F1E2)`), `ok` false on any; if `ok`: `refundQueueSlots(unit); ok = fillBuildSlot(unit, type);`; if `!ok` → `unit->orderToIdle()`.
- [ ] `sel_inject.cpp`: `recvBuildWrapper` at 0x4C23C0 (ESI = packet; `jmpPatch(..., 0x004C23C0, 3)` after `disat.py 4C23C0` confirms 8 bytes); `builderStub` (`MOV EAX, [chosenBuilder]` through a file-static pointer; nonzero → `RETN`; else `JMP 0x0049A850`) callPatched at 0x48E01E and 0x48E0B1; `selectChunkDispatch`: before `notOurs`, a 0x3D branch: `CMP BYTE PTR [ESI], 0x3D / JNE notOurs / CMP EAX, 8 / JL notOurs / SUB EAX, 8 / MOV [EBP+8], EAX / MOV DWORD PTR [EBP-4], 8 / PUSHAD / PUSH ESI / CALL recvQueuedBuildC / ADD ESP, 4 / POPAD / JMP Back_CommandDone`; `orderRootStub` at 0x4EC4D0 (`jmpPatch(..., 3)`): `PUSHAD`, `PUSH EAX`, `CALL beforeOrderC`, `ADD ESP, 4`, `POPAD`, then the 8 replaced bytes `PUSH EBX / PUSH ESI / MOV ESI, EAX / MOVZX EAX, BYTE PTR [ESI+0x4D]` and `JMP 0x004EC4D8` (re-read [ESI+0x4D] after the call: beforeOrder may change the order). All in `injectSmartBuildHooks()`.
- [ ] verify; commit `feat: smart-build receive: nearest builder, queued builds shared out`.

### Task 3b: disruptions on the way and on arrival (rules 2 and 3)
**Files:** `hooks/selection_ext/sel_inject.cpp` (stubs, in `injectSmartBuildHooks()`), `sel_exe.h/.cpp` (`bool withinReach(CUnit*, s32 x, s32 y, u32 distance)` = 0x401240: ECX unit, EAX y, push x, push distance; `void showStatTextTo(u32 stringId, u8 player)` = 0x48CF00 with `statTxtTbl->getString(id)`), `sel_build.h/.cpp`.
- [ ] `selbuild::gaveUp(CUnit* unit)` (synced; the message only shows on its owner's screen): `if (!selexe::withinReach(unit, unit->orderTarget.pt.x, unit->orderTarget.pt.y, 128)) selexe::showStatTextTo(0x35E, unit->playerId);` ("Couldn't reach the building site."; a money failure is within reach and has shown its own error).
- [ ] Rule 3 stubs, callPatched over the `call 0x4753A0` give-up exits, ECX = unit: SCV 0x46817C, Probe 0x4E4EDB:
```cpp
const u32 Func_ToIdle = 0x004753A0;
void __declspec(naked) buildGaveUpStub() {
	static CUnit* unit;
	__asm {
		MOV unit, ECX
		PUSHAD
	}
	selbuild::gaveUp(unit);
	__asm {
		POPAD
		JMP Func_ToIdle
	}
}
```
- [ ] Rule 2 stub, callPatched at 0x468125 (SCV, createUnit failed; ESI = unit, CL = idle order; vanilla `call 0x475310` replaces every order): with a queued order left, go on to it instead.
```cpp
const u32 Func_OrderComputerCL = 0x00475310;
void __declspec(naked) scvSiteBlockedStub() {
	__asm {
		CMP DWORD PTR [ESI+0x74], 0		//orderQueueHead
		JE vanilla
		MOV ECX, ESI
		JMP Func_ToIdle
	vanilla:
		JMP Func_OrderComputerCL
	}
}
```
- [ ] verify (naked check); commit `feat: queued builds go on after a blocked site or a cut path, with the message`.

### Task 4: placing with Shift
**Files:** `sel_build.h/.cpp` (`bool sendAsQueued(u8* cmd)`, `void frame()`), `sel_inject.cpp`, `hooks/main/game_hooks.cpp`.
- [ ] `sendAsQueued(cmd)` (local): returns false unless Shift is held (`[0x596A28]`) and `cmd[1]` is a Drone/Terran/Protoss1 build order; then `cmd[0] = CMD_QUEUED_BUILD`, `shiftPlacing = true`, true.
- [ ] Stub replacing 0x48E62E–0x48E639 (`jmpPatch(placeSendStub, 0x0048E62E, 7)`): `MOV cmdPtr, ECX / PUSHAD / (call sendAsQueued(cmdPtr), store result) / POPAD / CALL 0x485BD0 / CMP queued, 0 / JNE back / PUSH 0 / CALL 0x4843F0 / back: JMP 0x0048E63A`.
- [ ] 0x48DDA0 replaced (`jmpPatch(placementStillValidWrapper, 0x0048DDA0, 2)`, EAX out): true if any member of the active subgroup (`selsub::activeMembers`) passes `placeBuildingAllowed(m, [0x64088D], [0x64088A])`; with no members, the portrait as vanilla.
- [ ] `frame()`: if `shiftPlacing`: placing ended (`[0x640880] == 0`) → `shiftPlacing = false`; else if Shift is up → `selexe::cancelPlacement(); shiftPlacing = false;`. Called from `nextFrame()` in `game_hooks.cpp` (in game only).
- [ ] verify; commit `feat: Shift keeps placing and queues the building`.

### Task 5: save stamps (SELX version 3)
**Files:** `hooks/selection_ext/sel_save.cpp`.
- [ ] Write version 3: after the v2 payload, a second compressed block `u32 stamps[1700]`, `u32 nextStamp[8]`. Read: v3 reads both blocks; v1/v2 → `selbuild::reset()` (stamps 0). Verify; commit `feat: save chunk version 3 with the smart-build stamps`.

### Task 5b: Shift-placements prepaid (user, after testing)
- Remove the build-menu refresh after each Shift-placement (`afterQueuedSend`): the cursor keeps the building.
- Pure (host-tested): `void reconcilePaid(u32* paidM, u32* paidG, u32 heldM, u32 heldG, u32* refundM, u32* refundG)` — what a unit has paid but no longer holds is refunded; `bool canAfford(s32 minerals, s32 gas, u32 costM, u32 costG)`.
- Synced per unit (index - 1): `paidMinerals`, `paidGas` (all prepaid builds it holds), `paidCurrentType` (the current build's type if it is prepaid, else 0xFFFF). Saved in SELX v3's second block.
- 0x3D: check `canAfford` (else 0x352/0x353 with sfx, return). Start-now: `buildNow`, then if the unit now holds a build at this site, take the cost and set `paidCurrentType`. Append: take the cost, then queue the marked entry.
- `beforeOrder`: a marked current build sets `paidCurrentType` (no money check: prepaid); then for units with paid > 0: held = (`paidCurrentType` valid and `holdsBuild(mainOrderId)` ? its cost : 0, else clear it) + the costs of marked queue entries; `reconcilePaid` and give the refund.
- Arrival (paying sites): `callPatch` over `call 0x467030` at 0x468064 (SCV) and 0x4E4DF5 (Probe), EAX = unit, and over `call 0x42CF70` at 0x45E189 (Drone, ESI = unit): if `paidCurrentType` is set, give its cost back, take it off `paid*`, clear it; then vanilla (which spends it).
- Death: in the dying unit's remove-from-selections hook (0x49A7F0), refund everything it has paid.

### Task 6: in-game test round
- 9.1 10 SCVs (mining): Build Structure shows; place a Supply Depot → the nearest builds, the rest keep mining.
- 9.2 Shift-place 6 depots, 2 barracks and a bunker: the cursor stays while Shift is held (switch building from the open menu); each SCV builds its share in order. Release Shift: placing ends, the build menu stays. Esc while Shift-placing: placing ends, nothing stuck.
- 9.3 Probes: the same.
- 9.4 Queue builds with too few minerals: when a queued one starts, "Not enough minerals" shows and the Probe/SCV goes on to its next queued one.
- 9.5 One SCV: plain and Shift placement as vanilla (Shift: queued behind its current build).
- 9.6 3 Drones, 5 Shift-placements: three morph sites taken one each; the 4th and 5th move the earliest two Drones (if still walking).
- 9.7 Save with queued builds, load: the queues carry on. Load a save from before this feature.
- 9.8 Replay of all of it: same builders, no desync.
- 9.9 A plain Move clears a worker's queue; Shift+Move after the builds runs after them.
- 9.10 Rule 1: select SCVs on two islands, Shift-place on one: only that island's SCVs take it; with only the far island's SCVs selected: "Couldn't reach the building site.", nothing queued.
- 9.11 Rule 2: Shift-queue 3 depots on one SCV, then land a Barracks (or park units that can't move) on the 2nd site before it gets there: "You can't build there.", and it goes on to the 3rd.
- 9.12 Rule 3: Shift-queue 2 buildings outside a wall, then close the path (land a building, or wall with units held in place): once it gives up, "Couldn't reach the building site.", and it goes on to the next. A short jam only delays it.

### Task 7: docs
Spec status; `docs/resolution.md` §6; memory; commit.
