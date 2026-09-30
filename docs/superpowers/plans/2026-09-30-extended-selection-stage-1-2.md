# Extended selection, stages 1+2: implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** selections above 12 units (limit `SEL_MAX`, currently 400) that take orders, draw circles, survive save/load, and travel as a new chunked select command.

**Architecture:** new `SEL_MAX`-sized arrays are the real selections; vanilla's 12-slot arrays become a mirror of the first 12, so exe code that isn't converted keeps working. Every function that *writes* a selection is replaced (stage 1), then the select command gets a chunked form and the client-side list builders grow (stage 2).

**Tech stack:** C++ (MSVC, Win32, VS 18), GPTP hook tools (`jmpPatch`, `callPatch`, `memoryPatch`), MSVC inline asm for the exe's register calling conventions.

**Spec:** `docs/superpowers/specs/2026-09-30-extended-selection-design.md` (read its "Architecture", "Stage 1" and "Stage 2" sections first; every address and calling convention below comes from there).

## Global constraints
- The limit lives only in `selext::SEL_MAX` (`GPTP/SCBW/selection_ext.h`). No file, function or variable name contains the number.
- Synced arrays (`playersSel`, `pending`, the control groups, the ring stamps) are written only from command receive code and game state, never from local state (screen, local player, UI arrays).
- Converted code never writes vanilla's 12-slot arrays except through the `mirror*()` functions.
- Hooks are listed one per line in `GPTP/initialize.cpp` under "ENABLED HOOKS"; no `/* */` blocks (memory: check-block-comments).
- Build: `"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" "D:\SC Modding\GPTP\GPTP\GPTP.sln" /p:Configuration=Debug /p:Platform=Win32`. Touch `GPTP/hooks/main/game_hooks.cpp` first so the build stamp changes. The post-build step copies `GPTP.qdp` to `D:\SC Modding\SCManifold\`; the user repacks and runs.
- There is no unit-test framework. Automated checks: the build, and `selext::selfTest()` (pure logic) which prints its result in game at frame 0. Behaviour is checked in numbered in-game test rounds.
- Code style: match the surrounding GPTP files (tabs, `//` comments, `namespace hooks` for inject functions, naked wrappers with `static` variables and `PUSHAD`/`POPAD`).

## Review focus
1. **A unit dying while selected by a player other than the local one** (e.g. an ally in a team game): it must leave `playersSel[p]` and the mirror, or a later order touches a dead unit. Covered by Task 3's `removeFromAllSelections` and test round step 1.6.
2. **Saving right after a selection changed**: the save path calls 0x499A60, which strips and redraws every circle; units past 12 must keep circles. Covered by Task 4's `redrawCircles` (hooked in Task 8) and test step 1.9.
3. **Loading a save made before this build**: must load with the vanilla 12, not fail. Covered by Task 6 (short read → `fillFromVanilla`) and test step 1.10.
4. **A select command that arrives while its units are already dead** (the tag's uniqueness no longer matches): skipped, not selected. Covered by `unitOfTag` and self-test `tagRoundTrip`.
5. **Chunks interleaved with other commands or a missing "first" chunk**: the packet is dropped, never half-applied. Covered by self-test `chunkFeedRejects` (Task 1) and test step 2.6.

---

## File structure

| File | Responsibility |
|---|---|
| `GPTP/SCBW/selection_ext.h/.cpp` (new) | Storage, list helpers, mirrors, unit tags, chunk format, ring-slot choice. Pure except for the mirror writes. |
| `GPTP/hooks/selection_ext/sel_exe.h/.cpp` (new) | Wrappers for the exe functions this feature calls (register conventions). |
| `GPTP/hooks/selection_ext/sel_synced.h/.cpp` (new) | Synced logic: iterator, removal, receive 0x09/0x0A/0x0B/0x13/0x3C, control groups, ring. |
| `GPTP/hooks/selection_ext/sel_local.h/.cpp` (new) | Local logic: build the drawn selection, local removal, circles after save, client copy, game start. |
| `GPTP/hooks/selection_ext/sel_send.h/.cpp` (new) | Sending: `CMDACT_Select`, `CMDACT_HotkeyUnit`, vanilla packets or chunks. |
| `GPTP/hooks/selection_ext/sel_save.h/.cpp` (new) | The save-file chunk. |
| `GPTP/hooks/selection_ext/sel_inject.cpp` + `selection_ext_hooks.h` (new) | Naked wrappers and `hooks::injectSelectionExtHooks()` / `hooks::injectSelectChunkHooks()`. |
| `GPTP/hooks/selection_ext/sel_selftest.cpp` (new) | `selext::selfTest()`. |
| `GPTP/hooks/recv_commands/CMDRECV_MergeArchon.cpp` | Bound the templar buffers. |
| `GPTP/hooks/recv_commands/receive_command.cpp`, `smart_cast.cpp` | Buffers to `SEL_MAX`. |
| `GPTP/hooks/interface/selection.cpp` | Client list builders to `SEL_MAX` (stage 2). |
| `GPTP/initialize.cpp`, `GPTP/hooks/main/game_hooks.cpp`, `GPTP/GPTP.vcxproj` | Register hooks, self-test print, project entries. |

---

# Stage 1

### Task 1: storage module and self-test

**Files:**
- Create: `GPTP/SCBW/selection_ext.h`, `GPTP/SCBW/selection_ext.cpp`
- Create: `GPTP/hooks/selection_ext/sel_selftest.cpp`, `GPTP/hooks/selection_ext/selection_ext_hooks.h`
- Modify: `GPTP/GPTP.vcxproj` (add the new files), `GPTP/hooks/main/game_hooks.cpp` (print the self-test result)

**Interfaces:**
- Produces: everything declared in `selection_ext.h` below; `u32 selext::selfTest(u32* firstFailedLine)`.

- [ ] **Step 1: Write `selection_ext.h`**

File: `GPTP/SCBW/selection_ext.h`
```cpp
//Selections larger than vanilla's 12 units.
//Design: docs/superpowers/specs/2026-09-30-extended-selection-design.md
//
//The arrays here are the real selections. Vanilla's 12-slot arrays are kept
//as a mirror of the first 12 entries, for exe code that still reads them.
//Every list is compact: its units come first, then nulls.
#pragma once
#include "../types.h"

struct CUnit;

namespace selext {

//The selection limit. The number is not settled; it is changed only here.
const u32 SEL_MAX = 400;
//Vanilla's limit, the size of the mirrored arrays.
const u32 VANILLA_MAX = 12;
const u32 PLAYERS = 8;

//Synced: each player's selection.
extern CUnit* playersSel[PLAYERS][SEL_MAX];
//Local: the local player's selection as drawn, with selection circles.
extern CUnit* activeSel[SEL_MAX];
//Local: the selection shown in the console, and its count.
extern CUnit* clientSel[SEL_MAX];
extern u32 clientCount;
//Local: the list last sent in a select command.
extern CUnit* lastSent[SEL_MAX];
//Local: the local player's selection, selected again when a game starts
//(vanilla 0x596B7C).
extern CUnit* reselect[SEL_MAX];

//-------- Lists. cap is the list's capacity. --------//

u32 listCount(CUnit* const* list, u32 cap);
//Index of unit among the entries before the first null, or -1.
int listFind(CUnit* const* list, u32 cap, const CUnit* unit);
//Removes unit, keeping the order (vanilla 0x49A170). Returns the new count.
u32 listRemove(CUnit** list, u32 cap, const CUnit* unit);
//Removes unit by moving the last entry into its place (vanilla 0x4BF8C0).
void listRemoveSwapLast(CUnit** list, u32 cap, const CUnit* unit);
void listClear(CUnit** list, u32 cap);

//-------- Mirrors: copy the first 12 entries into vanilla's arrays. --------//

void mirrorPlayer(u32 player);	//0x6284E8
void mirrorActive();			//0x6284B8
void mirrorClient();			//0x597208, and the count 0x59723D
void mirrorLastSent();			//0x59724C

//-------- Unit tags: 1-based unit index | uniqueness << 11, 0 for none. --------//

u16 tagOf(const CUnit* unit);
//The unit for a tag, or NULL unless it exists, is not dying and has the tag's
//uniqueness (the rules of vanilla 0x4CEDA0).
CUnit* unitOfTag(u32 tag);

//-------- The select-chunk command: [0x3C][flags][u8 count][u16 tag x count] --------//

const u8 CMD_SELECT_CHUNK = 0x3C;
//A command may be at most 254 bytes (the replay block size is one byte).
const u32 CHUNK_MAX_UNITS = 125;
const u32 CHUNK_MAX_BYTES = 3 + 2 * CHUNK_MAX_UNITS;
enum ChunkMode { CHUNK_REPLACE = 0, CHUNK_ADD = 1, CHUNK_REMOVE = 2 };
const u8 CHUNK_MODE_MASK = 0x03;
const u8 CHUNK_FIRST = 0x04;
const u8 CHUNK_LAST = 0x08;

//How many chunks a packet of count tags takes (at least 1).
u32 chunkCountFor(u32 count);
//Writes chunk number index of a packet into out (CHUNK_MAX_BYTES at least)
//and returns its length.
u32 chunkBuild(u8* out, ChunkMode mode, const u16* tags, u32 count, u32 index);

//A packet being received, one per player. Synced.
struct PendingPacket {
	bool open;
	u8 mode;
	u32 count;
	u16 tags[SEL_MAX];
};
extern PendingPacket pending[PLAYERS];
//Feeds one whole chunk command. Returns true when it completes the packet,
//which is then in p (and p is closed).
bool chunkFeed(PendingPacket& p, const u8* cmd);

//Length of any command from its first bytes: the select chunk, the
//variable-length 0x09-0x0B, or vanilla's table 0x5005F8.
u32 commandLength(const u8* cmd);

//-------- Recent-selection ring --------//

//The ring slot to reuse: the oldest of the 8 stamps, ties going to the
//higher slot; 0xFF when every stamp is 0xFFFF (vanilla 0x496560).
u8 oldestRingSlot(const u16* stamps);

//Clears every array, its mirror, and the pending chunks.
void clearAll();

//Runs the pure-logic checks. Returns the number that failed, and the source
//line of the first failure.
u32 selfTest(u32* firstFailedLine);

} //selext
```

- [ ] **Step 2: Write `selection_ext.cpp`**

File: `GPTP/SCBW/selection_ext.cpp`
```cpp
#include "selection_ext.h"
#include <SCBW/api.h>
#include <cstring>

namespace selext {

CUnit* playersSel[PLAYERS][SEL_MAX];
CUnit* activeSel[SEL_MAX];
CUnit* clientSel[SEL_MAX];
u32 clientCount;
CUnit* lastSent[SEL_MAX];
CUnit* reselect[SEL_MAX];
PendingPacket pending[PLAYERS];

namespace {

CUnit** const VANILLA_PLAYERS_SEL	= (CUnit**)	0x006284E8;	//[8][12], synced
CUnit** const VANILLA_ACTIVE_SEL	= (CUnit**)	0x006284B8;
CUnit** const VANILLA_CLIENT_SEL	= (CUnit**)	0x00597208;
u8* const VANILLA_CLIENT_COUNT		= (u8*)		0x0059723D;
CUnit** const VANILLA_LAST_SENT		= (CUnit**)	0x0059724C;
const u32* const COMMAND_LENGTHS	= (u32*)	0x005005F8;

void copyFirst12(CUnit** dest, CUnit* const* src) {
	for (u32 i = 0; i < VANILLA_MAX; i++)
		dest[i] = src[i];
}

} //unnamed namespace

u32 listCount(CUnit* const* list, u32 cap) {
	u32 n = 0;
	while (n < cap && list[n] != NULL)
		n++;
	return n;
}

int listFind(CUnit* const* list, u32 cap, const CUnit* unit) {
	for (u32 i = 0; i < cap && list[i] != NULL; i++)
		if (list[i] == unit)
			return (int)i;
	return -1;
}

u32 listRemove(CUnit** list, u32 cap, const CUnit* unit) {
	const u32 n = listCount(list, cap);
	const int i = listFind(list, cap, unit);
	if (unit == NULL || i < 0)
		return n;
	for (u32 k = (u32)i; k + 1 < n; k++)
		list[k] = list[k + 1];
	list[n - 1] = NULL;
	return n - 1;
}

void listRemoveSwapLast(CUnit** list, u32 cap, const CUnit* unit) {
	const u32 n = listCount(list, cap);
	const int i = listFind(list, cap, unit);
	if (unit == NULL || i < 0)
		return;
	list[i] = list[n - 1];
	list[n - 1] = NULL;
}

void listClear(CUnit** list, u32 cap) {
	memset(list, 0, cap * sizeof(CUnit*));
}

void mirrorPlayer(u32 player) {
	if (player < PLAYERS)
		copyFirst12(&VANILLA_PLAYERS_SEL[player * VANILLA_MAX], playersSel[player]);
}

void mirrorActive() {
	copyFirst12(VANILLA_ACTIVE_SEL, activeSel);
}

void mirrorClient() {
	copyFirst12(VANILLA_CLIENT_SEL, clientSel);
	*VANILLA_CLIENT_COUNT = (u8)(clientCount < VANILLA_MAX ? clientCount : VANILLA_MAX);
}

void mirrorLastSent() {
	copyFirst12(VANILLA_LAST_SENT, lastSent);
}

u16 tagOf(const CUnit* unit) {
	if (unit == NULL)
		return 0;
	const u32 index = unit->getIndex();
	if (index < 1 || index > UNIT_ARRAY_LENGTH)
		return 0;
	return (u16)(index | (unit->targetOrderSpecial << 11));
}

CUnit* unitOfTag(u32 tag) {
	if (tag == 0)
		return NULL;
	CUnit* unit = CUnit::getFromIndex((u16)(tag & 0x7FF));
	if (unit == NULL || unit->sprite == NULL)
		return NULL;
	if (unit->mainOrderId == OrderId::Die && unit->mainOrderState == 1)
		return NULL;
	if (unit->targetOrderSpecial != (tag >> 11))
		return NULL;
	return unit;
}

u32 chunkCountFor(u32 count) {
	return count == 0 ? 1 : (count + CHUNK_MAX_UNITS - 1) / CHUNK_MAX_UNITS;
}

u32 chunkBuild(u8* out, ChunkMode mode, const u16* tags, u32 count, u32 index) {
	const u32 chunks = chunkCountFor(count);
	const u32 start = index * CHUNK_MAX_UNITS;
	u32 n = count > start ? count - start : 0;
	if (n > CHUNK_MAX_UNITS)
		n = CHUNK_MAX_UNITS;
	u8 flags = (u8)mode;
	if (index == 0)
		flags |= CHUNK_FIRST;
	if (index + 1 >= chunks)
		flags |= CHUNK_LAST;
	out[0] = CMD_SELECT_CHUNK;
	out[1] = flags;
	out[2] = (u8)n;
	memcpy(&out[3], &tags[start], n * sizeof(u16));
	return 3 + 2 * n;
}

bool chunkFeed(PendingPacket& p, const u8* cmd) {
	const u8 flags = cmd[1];
	const u32 n = cmd[2];
	const u8 mode = flags & CHUNK_MODE_MASK;
	if (n > CHUNK_MAX_UNITS || mode > CHUNK_REMOVE) {
		p.open = false;
		return false;
	}
	if (flags & CHUNK_FIRST) {
		p.open = true;
		p.mode = mode;
		p.count = 0;
	}
	else
	if (!p.open || p.mode != mode) {
		p.open = false;
		return false;
	}
	if (p.count + n > SEL_MAX) {
		p.open = false;
		return false;
	}
	memcpy(&p.tags[p.count], &cmd[3], n * sizeof(u16));
	p.count += n;
	if (flags & CHUNK_LAST) {
		p.open = false;
		return true;
	}
	return false;
}

u32 commandLength(const u8* cmd) {
	const u8 id = cmd[0];
	if (id == CMD_SELECT_CHUNK)
		return 3 + 2 * cmd[2];
	if (id >= 0x09 && id <= 0x0B)
		return 2 + 2 * cmd[1];
	return COMMAND_LENGTHS[id];
}

u8 oldestRingSlot(const u16* stamps) {
	u32 best = 0xFFFF;
	u8 slot = 0xFF;
	for (int k = 7; k >= 0; k--)
		if (stamps[k] < best) {
			best = stamps[k];
			slot = (u8)k;
		}
	return slot;
}

void clearAll() {
	for (u32 p = 0; p < PLAYERS; p++) {
		listClear(playersSel[p], SEL_MAX);
		mirrorPlayer(p);
	}
	listClear(activeSel, SEL_MAX);
	mirrorActive();
	listClear(clientSel, SEL_MAX);
	clientCount = 0;
	mirrorClient();
	listClear(lastSent, SEL_MAX);
	mirrorLastSent();
	listClear(reselect, SEL_MAX);
	memset(pending, 0, sizeof(pending));
}

} //selext
```

- [ ] **Step 3: Write the self-test**

File: `GPTP/hooks/selection_ext/sel_selftest.cpp`
```cpp
//Checks of the pure selection logic (SCBW/selection_ext.h). Run once at the
//start of a game; the result is printed in game.
#include <SCBW/selection_ext.h>
#include <cstring>

namespace selext {

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

//Fake unit pointers: the list helpers never dereference them.
CUnit* fake(u32 n) { return (CUnit*)(0x1000 + 0x10 * n); }

void lists() {
	CUnit* list[5] = { fake(1), fake(2), fake(3), NULL, NULL };
	CHECK(listCount(list, 5) == 3);
	CHECK(listFind(list, 5, fake(2)) == 1);
	CHECK(listFind(list, 5, fake(9)) == -1);
	CHECK(listRemove(list, 5, fake(9)) == 3);
	CHECK(listRemove(list, 5, fake(1)) == 2);
	CHECK(list[0] == fake(2) && list[1] == fake(3) && list[2] == NULL);

	CUnit* full[3] = { fake(1), fake(2), fake(3) };
	CHECK(listCount(full, 3) == 3);
	CHECK(listRemove(full, 3, fake(3)) == 2 && full[2] == NULL);

	CUnit* swap[4] = { fake(1), fake(2), fake(3), fake(4) };
	listRemoveSwapLast(swap, 4, fake(2));
	CHECK(swap[0] == fake(1) && swap[1] == fake(4) && swap[2] == fake(3) && swap[3] == NULL);
}

void chunkRoundTrip() {
	u16 tags[SEL_MAX];
	for (u32 i = 0; i < SEL_MAX; i++)
		tags[i] = (u16)(i + 1);
	const u32 count = 300;
	CHECK(chunkCountFor(0) == 1);
	CHECK(chunkCountFor(125) == 1);
	CHECK(chunkCountFor(126) == 2);
	CHECK(chunkCountFor(count) == 3);

	PendingPacket p;
	memset(&p, 0, sizeof(p));
	u8 buf[CHUNK_MAX_BYTES];
	bool done = false;
	for (u32 c = 0; c < chunkCountFor(count); c++) {
		const u32 len = chunkBuild(buf, CHUNK_ADD, tags, count, c);
		CHECK(len <= CHUNK_MAX_BYTES && len == commandLength(buf));
		CHECK(!done);
		done = chunkFeed(p, buf);
	}
	CHECK(done && p.count == count && p.mode == CHUNK_ADD && !p.open);
	CHECK(p.tags[0] == 1 && p.tags[count - 1] == count);
}

void chunkFeedRejects() {
	u16 tags[200];
	for (u32 i = 0; i < 200; i++)
		tags[i] = (u16)(i + 1);
	PendingPacket p;
	memset(&p, 0, sizeof(p));
	u8 first[CHUNK_MAX_BYTES], second[CHUNK_MAX_BYTES];
	chunkBuild(first, CHUNK_REPLACE, tags, 200, 0);
	chunkBuild(second, CHUNK_REPLACE, tags, 200, 1);

	//A later chunk with no first one is dropped.
	CHECK(!chunkFeed(p, second) && !p.open);
	//A chunk of another mode in between drops the packet.
	chunkFeed(p, first);
	u8 other[CHUNK_MAX_BYTES];
	chunkBuild(other, CHUNK_REMOVE, tags, 200, 1);
	CHECK(!chunkFeed(p, other) && !p.open);
	CHECK(!chunkFeed(p, second));
	//A new first chunk restarts the packet.
	chunkFeed(p, first);
	CHECK(chunkFeed(p, second) && p.count == 200);
	//A count above the chunk limit is dropped.
	first[2] = (u8)(CHUNK_MAX_UNITS + 1);
	CHECK(!chunkFeed(p, first) && !p.open);
}

void lengths() {
	const u8 select[] = { 0x09, 3, 0, 0, 0, 0, 0, 0 };
	CHECK(commandLength(select) == 8);
	const u8 chunk[] = { CMD_SELECT_CHUNK, CHUNK_FIRST | CHUNK_LAST, 2, 0, 0, 0, 0 };
	CHECK(commandLength(chunk) == 7);
	const u8 hotkey[] = { 0x13, 0, 0 };
	CHECK(commandLength(hotkey) == 3);
}

void ring() {
	u16 stamps[8] = { 5, 3, 9, 3, 7, 8, 6, 4 };
	CHECK(oldestRingSlot(stamps) == 3);	//ties go to the higher slot
	u16 zeros[8] = { 0 };
	CHECK(oldestRingSlot(zeros) == 7);
	u16 full[8];
	for (u32 i = 0; i < 8; i++)
		full[i] = 0xFFFF;
	CHECK(oldestRingSlot(full) == 0xFF);
}

} //unnamed namespace

u32 selfTest(u32* firstFailedLine) {
	failures = 0;
	firstLine = 0;
	lists();
	chunkRoundTrip();
	chunkFeedRejects();
	lengths();
	ring();
	*firstFailedLine = firstLine;
	return failures;
}

} //selext
```

- [ ] **Step 4: Write `selection_ext_hooks.h`** (declares the inject functions of later tasks; the file is created now so the project entries are complete)

File: `GPTP/hooks/selection_ext/selection_ext_hooks.h`
```cpp
//Hooks of the extended selection (SCBW/selection_ext.h).
#pragma once

namespace hooks {

//Stage 1: storage, iterator, every writer of a selection, circles, saves.
void injectSelectionExtHooks();
//Stage 2: the select-chunk command and the command-length sites.
void injectSelectChunkHooks();

} //hooks
```

- [ ] **Step 5: Add every new file to `GPTP.vcxproj`**

After `<ClCompile Include="hooks\recv_commands\smart_cast.cpp" />` add:
```xml
    <ClCompile Include="SCBW\selection_ext.cpp" />
    <ClCompile Include="hooks\selection_ext\sel_exe.cpp" />
    <ClCompile Include="hooks\selection_ext\sel_synced.cpp" />
    <ClCompile Include="hooks\selection_ext\sel_local.cpp" />
    <ClCompile Include="hooks\selection_ext\sel_send.cpp" />
    <ClCompile Include="hooks\selection_ext\sel_save.cpp" />
    <ClCompile Include="hooks\selection_ext\sel_inject.cpp" />
    <ClCompile Include="hooks\selection_ext\sel_selftest.cpp" />
```
After `<ClInclude Include="hooks\recv_commands\smart_cast.h" />` add:
```xml
    <ClInclude Include="SCBW\selection_ext.h" />
    <ClInclude Include="hooks\selection_ext\sel_exe.h" />
    <ClInclude Include="hooks\selection_ext\sel_synced.h" />
    <ClInclude Include="hooks\selection_ext\sel_local.h" />
    <ClInclude Include="hooks\selection_ext\sel_send.h" />
    <ClInclude Include="hooks\selection_ext\sel_save.h" />
    <ClInclude Include="hooks\selection_ext\selection_ext_hooks.h" />
```
The `.cpp` files of Tasks 2–10 don't exist yet; create each as an empty file containing only its `#include` line now, so the project builds after every task.

- [ ] **Step 6: Print the self-test result at frame 0**

In `GPTP/hooks/main/game_hooks.cpp`, add `#include <SCBW/selection_ext.h>` after `#include "../recv_commands/smart_cast.h"`, and in `initializeGame()` right after `resolution::printSettings();`:
```cpp
        u32 failedLine;
        const u32 failed = selext::selfTest(&failedLine);
        char selfTestText[96];
        if (failed == 0)
            sprintf_s(selfTestText, "extended selection: self-test passed");
        else
            sprintf_s(selfTestText, "extended selection: %u self-test checks FAILED (first at line %u)",
                      failed, failedLine);
        scbw::printText(selfTestText);
```
(`sprintf_s` needs `<cstdio>`; add `#include <cstdio>` at the top if the file doesn't have it.)

- [ ] **Step 7: Build**

Run the build command from "Global constraints". Expected: `Build succeeded`, 0 errors.

- [ ] **Step 8: Commit**
```bash
git add GPTP/SCBW/selection_ext.h GPTP/SCBW/selection_ext.cpp GPTP/hooks/selection_ext GPTP/GPTP.vcxproj GPTP/hooks/main/game_hooks.cpp
git commit -m "feat: extended selection storage and self-test"
```

### Task 2: exe call wrappers

**Files:**
- Create: `GPTP/hooks/selection_ext/sel_exe.h`, `GPTP/hooks/selection_ext/sel_exe.cpp`

**Interfaces:**
- Produces (namespace `selexe`): `createSelectionCircle(CUnit*, u32 slot)`, `removeSelectionCircle(CUnit*)`, `isTeamAlly(u32 player) -> bool`, `removeDashedCircle(CSprite*)`, `addDashedCircle(CUnit*)`, `freeImage(CImage*)`, `canMultiSelect(CUnit*) -> bool`, `outranks(CUnit* unit, CUnit* best) -> bool`, `selectRecentGroupOf(u32 tag) -> bool`, `centerViewOnGroup(u32 group)`, `queueCommand(const void*, u32)`, `cancelPlacement()`, `cancelTargetOrder()`, `writeCompressed(FILE*, const void*, u32) -> bool`, `readCompressed(FILE*, void*, u32) -> bool`, `fwriteExe(...)`, `freadExe(...)`.

- [ ] **Step 1: Write `sel_exe.h`**

File: `GPTP/hooks/selection_ext/sel_exe.h`
```cpp
//Calls into StarCraft.exe used by the extended selection. Each wrapper
//follows the exe function's own register convention (see the spec).
#pragma once
#include <SCBW/api.h>
#include <cstdio>

namespace selexe {

//Selection circle and health bar; slot goes to CSprite::selectionIndex.
void createSelectionCircle(CUnit* unit, u32 slot);			//0x4E6180
void removeSelectionCircle(CUnit* unit);					//0x4E6290
//Whether a player is a team-melee ally of the local human (local state:
//only gates dashed ally circles, which are local visuals).
bool isTeamAlly(u32 player);								//0x49A110
void removeDashedCircle(CSprite* sprite);					//0x497590
void addDashedCircle(CUnit* unit);							//0x4E65C0
void freeImage(CImage* image);								//0x4D4FA0
//Whether the unit may join a selection of more than one.
bool canMultiSelect(CUnit* unit);							//0x47B770
//Whether unit ranks above best for the console portrait.
bool outranks(CUnit* unit, CUnit* best);					//0x49A350
//Alt-click: selects the recent selection holding the unit, if any.
bool selectRecentGroupOf(u32 tag);							//0x496D30
void centerViewOnGroup(u32 group);							//0x4967E0
void queueCommand(const void* data, u32 size);				//0x485BD0
//Cancels building placement (0x48D9A0, then 0x48E310).
void cancelPlacement();
void cancelTargetOrder();									//0x48CA10
bool writeCompressed(FILE* file, const void* data, u32 size);	//0x4C3450
bool readCompressed(FILE* file, void* data, u32 size);			//0x4C3280
//The exe's own CRT: the save file's FILE* belongs to it.
size_t fwriteExe(const void* data, size_t size, size_t count, FILE* file);	//0x411931
size_t freadExe(void* data, size_t size, size_t count, FILE* file);			//0x4117DE

} //selexe
```

- [ ] **Step 2: Write `sel_exe.cpp`**

File: `GPTP/hooks/selection_ext/sel_exe.cpp`
```cpp
#include "sel_exe.h"

namespace selexe {

namespace {

const u32 Func_CreateSelectionCircle	= 0x004E6180;
const u32 Func_RemoveSelectionCircle	= 0x004E6290;
const u32 Func_IsTeamAlly				= 0x0049A110;
const u32 Func_RemoveDashedCircle		= 0x00497590;
const u32 Func_AddDashedCircle			= 0x004E65C0;
const u32 Func_FreeImage				= 0x004D4FA0;
const u32 Func_CanMultiSelect			= 0x0047B770;
const u32 Func_Outranks					= 0x0049A350;
const u32 Func_SelectRecentGroupOf		= 0x00496D30;
const u32 Func_CenterViewOnGroup		= 0x004967E0;
const u32 Func_QueueCommand				= 0x00485BD0;
const u32 Func_RefreshLayer3And4		= 0x0048D9A0;
const u32 Func_Sub48E310				= 0x0048E310;
const u32 Func_CancelTargetOrder		= 0x0048CA10;
const u32 Func_WriteCompressed			= 0x004C3450;

typedef u32 (__stdcall *ReadCompressedFn)(void* data, u32 size, FILE* file);
const ReadCompressedFn readCompressedFn = (ReadCompressedFn)0x004C3280;
typedef size_t (__cdecl *FwriteFn)(const void* data, size_t size, size_t count, FILE* file);
const FwriteFn fwriteFn = (FwriteFn)0x00411931;
typedef size_t (__cdecl *FreadFn)(void* data, size_t size, size_t count, FILE* file);
const FreadFn freadFn = (FreadFn)0x004117DE;

} //unnamed namespace

void createSelectionCircle(CUnit* unit, u32 slot) {
	__asm {
		PUSHAD
		PUSH slot
		MOV EAX, unit
		CALL Func_CreateSelectionCircle
		POPAD
	}
}

void removeSelectionCircle(CUnit* unit) {
	__asm {
		PUSHAD
		MOV EAX, unit
		CALL Func_RemoveSelectionCircle
		POPAD
	}
}

bool isTeamAlly(u32 player) {
	u32 result;
	__asm {
		PUSHAD
		MOV EAX, player
		CALL Func_IsTeamAlly
		MOV result, EAX
		POPAD
	}
	return result != 0;
}

void removeDashedCircle(CSprite* sprite) {
	__asm {
		PUSHAD
		MOV EAX, sprite
		CALL Func_RemoveDashedCircle
		POPAD
	}
}

void addDashedCircle(CUnit* unit) {
	__asm {
		PUSHAD
		MOV EAX, unit
		CALL Func_AddDashedCircle
		POPAD
	}
}

void freeImage(CImage* image) {
	__asm {
		PUSHAD
		MOV ESI, image
		CALL Func_FreeImage
		POPAD
	}
}

bool canMultiSelect(CUnit* unit) {
	u32 result;
	__asm {
		PUSHAD
		MOV ECX, unit
		CALL Func_CanMultiSelect
		MOV result, EAX
		POPAD
	}
	return result != 0;
}

bool outranks(CUnit* unit, CUnit* best) {
	u32 result;
	__asm {
		PUSHAD
		MOV EDI, unit
		MOV ESI, best
		CALL Func_Outranks
		MOV result, EAX
		POPAD
	}
	return result != 0;
}

bool selectRecentGroupOf(u32 tag) {
	u32 result;
	__asm {
		PUSHAD
		PUSH tag
		CALL Func_SelectRecentGroupOf
		MOV result, EAX
		POPAD
	}
	return result != 0;
}

void centerViewOnGroup(u32 group) {
	__asm {
		PUSHAD
		MOV ECX, group
		CALL Func_CenterViewOnGroup
		POPAD
	}
}

void queueCommand(const void* data, u32 size) {
	__asm {
		PUSHAD
		MOV ECX, data
		MOV EDX, size
		CALL Func_QueueCommand
		POPAD
	}
}

void cancelPlacement() {
	__asm {
		PUSHAD
		CALL Func_RefreshLayer3And4
		CALL Func_Sub48E310
		POPAD
	}
}

void cancelTargetOrder() {
	__asm {
		PUSHAD
		CALL Func_CancelTargetOrder
		POPAD
	}
}

bool writeCompressed(FILE* file, const void* data, u32 size) {
	u32 result;
	__asm {
		PUSHAD
		PUSH file
		PUSH size
		MOV EAX, data
		CALL Func_WriteCompressed
		MOV result, EAX
		POPAD
	}
	return result != 0;
}

bool readCompressed(FILE* file, void* data, u32 size) {
	return readCompressedFn(data, size, file) != 0;
}

size_t fwriteExe(const void* data, size_t size, size_t count, FILE* file) {
	return fwriteFn(data, size, count, file);
}

size_t freadExe(void* data, size_t size, size_t count, FILE* file) {
	return freadFn(data, size, count, file);
}

} //selexe
```

- [ ] **Step 3: Build.** Expected: success (nothing calls these yet).
- [ ] **Step 4: Commit** `git add GPTP/hooks/selection_ext/sel_exe.* && git commit -m "feat: exe wrappers for the extended selection"`

### Task 3: synced logic (iterator, removal, receive, groups, ring)

**Files:**
- Create: `GPTP/hooks/selection_ext/sel_synced.h`, `GPTP/hooks/selection_ext/sel_synced.cpp`

**Interfaces:**
- Consumes: `selext::*` (Task 1), `selexe::*` (Task 2).
- Produces (namespace `selsync`): `CUnit* nextSelected()`, `void removeFromAllSelections(CUnit*)`, `void clearSelection(u32 player)`, `void recvSelect(const u8*)`, `void recvShiftSelect(const u8*)`, `void recvShiftDeselect(const u8*)`, `void recvHotkey(const u8*)`, `void recvSelectChunk(const u8*)`.

- [ ] **Step 1: Write `sel_synced.h`**

File: `GPTP/hooks/selection_ext/sel_synced.h`
```cpp
//Synced selection logic. Everything here runs on every computer from the same
//commands, so it may read game state only, never local state.
#pragma once
#include <SCBW/api.h>

namespace selsync {

//The next unit of the command player's selection (0x49A850). Writing 0 to
//selectionIndexStart restarts it, as in vanilla.
CUnit* nextSelected();
//Removes a unit from every player's selection (0x49A7F0).
void removeFromAllSelections(CUnit* unit);
//Clears a player's selection (0x49A740).
void clearSelection(u32 player);

void recvSelect(const u8* packet);			//0x09, 0x4C2750
void recvShiftSelect(const u8* packet);		//0x0A, 0x4C2560
void recvShiftDeselect(const u8* packet);	//0x0B, 0x4BFB40
void recvHotkey(const u8* packet);			//0x13, 0x4C2870
void recvSelectChunk(const u8* packet);		//0x3C (selext::CMD_SELECT_CHUNK)

} //selsync
```

- [ ] **Step 2: Write `sel_synced.cpp`**

File: `GPTP/hooks/selection_ext/sel_synced.cpp`
```cpp
#include "sel_synced.h"
#include "sel_exe.h"
#include <SCBW/selection_ext.h>
#include <cstring>

using namespace selext;

namespace {

u32* const CONTROL_GROUPS			= (u32*)		0x0057FE60;	//tags [8][18][12], in CGame
u16* const RING_STAMPS				= (u16*)		0x0063FE40;	//[8][8]
const u32* const FRAME_COUNTER		= (const u32*)	0x0057EEBC;
const u32 GROUP_COUNT = 18;
const u32 FIRST_RING_GROUP = 10;
//Units per control group: vanilla's 12 until the groups grow (stage 4).
const u32 GROUP_CAP = 12;

u32 iteratorCursor;

u32 activePlayer() {
	return (u32)*ACTIVE_PLAYER_ID;
}

bool isDying(const CUnit* unit) {
	return unit->mainOrderId == OrderId::Die && unit->mainOrderState == 1;
}

u32* groupSlots(u32 player, u32 group) {
	return &CONTROL_GROUPS[(player * GROUP_COUNT + group) * GROUP_CAP];
}

//One fewer dashed ally circle on the sprite (sprite flags bits 1-2 count them).
void dropDashedCircle(CSprite* sprite) {
	u8 count = (sprite->flags >> 1) & 3;
	if (count == 0)
		return;
	count--;
	sprite->flags = (u8)((sprite->flags & ~6) | (count << 1));
	if (count == 0)
		selexe::removeDashedCircle(sprite);
}

//Puts unit at slot of the player's selection (vanilla 0x49AF80).
bool addUnit(u32 player, CUnit* unit, u32 slot) {
	if (slot >= SEL_MAX)
		return false;
	if (unit->sprite->flags & CSprite_Flags::Hidden)
		return false;
	if (slot > 0 && !(selexe::canMultiSelect(unit) && unit->playerId == *ACTIVE_NATION_ID))
		return false;
	playersSel[player][slot] = unit;
	if (selexe::isTeamAlly(player))
		selexe::addDashedCircle(unit);
	return true;
}

//Stores the player's selection in a control group (vanilla 0x4965D0).
void groupAssign(u32 player, u32 group, bool replace) {
	u32* slots = groupSlots(player, group);
	u32 n = 0;
	if (!replace) {
		CUnit* first = unitOfTag(slots[0]);
		if (first != NULL && !selexe::canMultiSelect(first))
			return;
		while (n < GROUP_CAP && slots[n] != 0)
			n++;
	}
	else
		memset(slots, 0, GROUP_CAP * sizeof(u32));
	for (u32 j = 0; j < SEL_MAX; j++) {
		CUnit* unit = playersSel[player][j];
		if (unit == NULL || unit->playerId != *ACTIVE_NATION_ID)
			return;
		const u32 tag = tagOf(unit);
		if (tag == 0)
			continue;
		if (!replace && n > 0) {
			bool present = false;
			for (u32 k = 0; k < n && !present; k++)
				present = (slots[k] == tag);
			if (present || !selexe::canMultiSelect(unit))
				continue;
		}
		if (n >= GROUP_CAP)
			return;	//vanilla writes one past the group here
		slots[n++] = tag;
		if (n >= GROUP_CAP)
			return;
	}
}

//Copies the selection into the oldest recent-selection group.
void ringPush(u32 player) {
	u16* stamps = &RING_STAMPS[player * 8];
	const u8 slot = oldestRingSlot(stamps);
	//0xFF + 10 wraps to group 9, as in vanilla; its out-of-bounds stamp is skipped.
	groupAssign(player, (u8)(slot + FIRST_RING_GROUP), true);
	if (slot < 8)
		stamps[slot] = (u16)*FRAME_COUNTER;
}

//Selects a control group (vanilla 0x496940).
void groupRecall(u32 player, u32 group) {
	u32* slots = groupSlots(player, group);
	u32 n = 0;
	while (n < GROUP_CAP && slots[n] != 0)
		n++;
	if (n == 0)
		return;
	selsync::clearSelection(player);
	u32 i = 0;
	while (i < n) {
		CUnit* unit = unitOfTag(slots[i]);
		if (unit != NULL && unit->playerId == *ACTIVE_NATION_ID
			&& !(unit->sprite->flags & CSprite_Flags::Hidden)
			&& (selexe::canMultiSelect(unit) || n <= 1))
		{
			playersSel[player][i] = unit;
			if (selexe::isTeamAlly(player))
				selexe::addDashedCircle(unit);
			i++;
		}
		else {
			n--;
			slots[i] = slots[n];
			slots[n] = 0;
		}
	}
	mirrorPlayer(player);
	if (group >= FIRST_RING_GROUP)
		RING_STAMPS[player * 8 + group - FIRST_RING_GROUP] = (u16)*FRAME_COUNTER;
	else
		ringPush(player);
}

//The rules of 0x09 (vanilla 0x4C2750), for any count.
void commitReplace(u32 player, const u16* tags, u32 count) {
	selsync::clearSelection(player);
	if (player >= PLAYERS)
		return;
	u32 added = 0;
	for (u32 i = 0; i < count; i++) {
		CUnit* unit = unitOfTag(tags[i]);
		if (unit == NULL || listFind(playersSel[player], SEL_MAX, unit) >= 0)
			continue;
		if (unit->id == UnitId::TerranNuclearMissile)
			continue;
		if (addUnit(player, unit, added))
			added++;
	}
	mirrorPlayer(player);
	if (added > 1)
		ringPush(player);
}

//The rules of 0x0A (vanilla 0x4C2560): refused whole past the limit.
void commitAdd(u32 player, const u16* tags, u32 count) {
	if (player >= PLAYERS)
		return;
	u32 current = listCount(playersSel[player], SEL_MAX);
	if (count + current > SEL_MAX)
		return;
	for (u32 i = 0; i < count; i++) {
		CUnit* unit = unitOfTag(tags[i]);
		if (unit == NULL || listFind(playersSel[player], SEL_MAX, unit) >= 0)
			continue;
		if (addUnit(player, unit, current))
			current++;
	}
	mirrorPlayer(player);
	if (current > 1)
		ringPush(player);
}

//The rules of 0x0B (vanilla 0x4BFB40).
void commitRemove(u32 player, const u16* tags, u32 count) {
	u32 left = 0;
	for (u32 i = 0; i < count; i++) {
		CUnit* unit = unitOfTag(tags[i]);
		if (unit == NULL)
			continue;
		if (player >= PLAYERS) {
			left = 0;
			continue;
		}
		if (selexe::isTeamAlly(player))
			dropDashedCircle(unit->sprite);
		left = listRemove(playersSel[player], SEL_MAX, unit);
	}
	if (player >= PLAYERS)
		return;
	mirrorPlayer(player);
	if (left > 1)
		ringPush(player);
}

} //unnamed namespace

namespace selsync {

CUnit* nextSelected() {
	if (*selectionIndexStart == 0)
		iteratorCursor = 0;
	*selectionIndexStart = 1;
	const u32 player = activePlayer();
	if (player >= PLAYERS)
		return NULL;
	CUnit** const selection = playersSel[player];
	while (iteratorCursor < SEL_MAX) {
		CUnit* unit = selection[iteratorCursor];
		if (unit == NULL)
			return NULL;
		if (unit->sprite != NULL && !isDying(unit)) {
			iteratorCursor++;
			return unit;
		}
		//The removal moves the next unit into this slot.
		removeFromAllSelections(unit);
	}
	return NULL;
}

void removeFromAllSelections(CUnit* unit) {
	for (u32 p = 0; p < PLAYERS; p++) {
		listRemove(playersSel[p], SEL_MAX, unit);
		mirrorPlayer(p);
	}
	CSprite* sprite = unit->sprite;
	if (sprite != NULL && (sprite->flags & 0x06)) {
		sprite->flags &= ~0x06;
		for (CImage* image = sprite->images.tail; image != NULL; image = image->link.prev)
			if (image->id >= 0x23B && image->id <= 0x244) {
				selexe::freeImage(image);
				break;
			}
	}
	listRemoveSwapLast(lastSent, SEL_MAX, unit);
	mirrorLastSent();
}

void clearSelection(u32 player) {
	if (player >= PLAYERS)
		return;
	if (selexe::isTeamAlly(player))
		for (u32 i = 0; i < SEL_MAX && playersSel[player][i] != NULL; i++)
			dropDashedCircle(playersSel[player][i]->sprite);
	listClear(playersSel[player], SEL_MAX);
	mirrorPlayer(player);
}

void recvSelect(const u8* packet) {
	const u32 count = packet[1];
	if (count > VANILLA_MAX)
		return;
	commitReplace(activePlayer(), (const u16*)&packet[2], count);
}

void recvShiftSelect(const u8* packet) {
	const u32 count = packet[1];
	if (count > VANILLA_MAX)
		return;
	commitAdd(activePlayer(), (const u16*)&packet[2], count);
}

void recvShiftDeselect(const u8* packet) {
	const u32 count = packet[1];
	if (count == 0 || count > VANILLA_MAX)
		return;
	commitRemove(activePlayer(), (const u16*)&packet[2], count);
}

void recvHotkey(const u8* packet) {
	const u32 group = packet[2];
	if (group >= GROUP_COUNT)
		return;
	const u32 player = activePlayer();
	if (player >= PLAYERS)
		return;
	switch (packet[1]) {
		case 0: groupAssign(player, group, true); break;
		case 1: groupRecall(player, group); break;
		case 2: groupAssign(player, group, false); break;
	}
}

void recvSelectChunk(const u8* packet) {
	const u32 player = activePlayer();
	if (player >= PLAYERS)
		return;
	PendingPacket& p = pending[player];
	if (!chunkFeed(p, packet))
		return;
	switch (p.mode) {
		case CHUNK_REPLACE:	commitReplace(player, p.tags, p.count); break;
		case CHUNK_ADD:		commitAdd(player, p.tags, p.count); break;
		case CHUNK_REMOVE:	if (p.count > 0) commitRemove(player, p.tags, p.count); break;
	}
}

} //selsync
```

- [ ] **Step 3: Build.** Expected: success.
- [ ] **Step 4: Commit** `git commit -m "feat: synced logic of the extended selection"` (with `sel_synced.*`).

### Task 4: local logic (drawn selection, local removal, circles, client copy, game start)

**Files:**
- Create: `GPTP/hooks/selection_ext/sel_local.h`, `GPTP/hooks/selection_ext/sel_local.cpp`

**Interfaces:**
- Consumes: `selext::*`, `selexe::*`, `selsend::cmdactSelect(u32, CUnit**)` (Task 5; declared in `sel_send.h`).
- Produces (namespace `sellocal`): `buildActive(CUnit** list, u32 count)`, `localRemove(CUnit*)`, `redrawCircles()`, `clientCopy()`, `deselectAndSend(CUnit*)`, `reselectAtStart()`, `gameStartClear()`, `gameStartKeepLocal()`, `addTwin(CUnit*)`, `requestRefresh()`.

- [ ] **Step 1: Write `sel_local.h`**

File: `GPTP/hooks/selection_ext/sel_local.h`
```cpp
//Local selection: what the local player sees (circles, the console). None of
//it is synced, and none of it may feed synced state.
#pragma once
#include <SCBW/api.h>

namespace sellocal {

//Makes list the drawn selection, replacing subunits by their parents in
//list too (0x49AE40).
void buildActive(CUnit** list, u32 count);
//Drops a dying unit from the drawn selection (0x49F7A0).
void localRemove(CUnit* unit);
//Draws every circle again after a save stripped them (0x499A60).
void redrawCircles();
//Copies the drawn selection to the console and picks the portrait (0x4C38B0).
void clientCopy();
//Drops one unit from the drawn selection and sends the rest (0x4C3B40).
void deselectAndSend(CUnit* unit);
//Selects the saved local selection when a game starts (0x4D0820).
void reselectAtStart();
//At the start of 0x4EED10: clears every selection array.
void gameStartClear();
//At 0x4EEDC6: keeps the local player's selection for reselectAtStart and
//clears the synced ones, as vanilla does with its 12.
void gameStartKeepLocal();
//The egg's second Zergling or Scourge joins the selection (0x45D040).
void addTwin(CUnit* twin);
//Tells the console to rebuild the selection, buttons, portrait and wireframes.
void requestRefresh();

} //sellocal
```

- [ ] **Step 2: Write `sel_local.cpp`**

File: `GPTP/hooks/selection_ext/sel_local.cpp`
```cpp
#include "sel_local.h"
#include "sel_exe.h"
#include "sel_send.h"
#include <SCBW/selection_ext.h>
#include <cstring>

using namespace selext;

namespace {

u8* const	REBUILD_CLIENT_SELECTION	= (u8*)		0x0059723C;
u32* const	REFRESH_BUTTON_SET			= (u32*)	0x0068C1B0;
u8* const	REFRESH_PORTRAIT			= (u8*)		0x0068AC74;
u8* const	REFRESH_STAT_DATA			= (u8*)		0x0068C1F8;
u32* const	HOVER_DIALOG				= (u32*)	0x0068C1E8;
u32* const	HOVER_DIALOG_USER			= (u32*)	0x0068C1EC;
const u32* const PLACING_BUILDING		= (u32*)	0x00640880;
const u8* const	TARGETING				= (u8*)		0x00641694;
const u8* const	LOCAL_VISIBILITY		= (u8*)		0x0057F0B0;
const u8* const	ALT_HELD				= (u8*)		0x00596A2A;
const u8* const	IS_MULTIPLAYER			= (u8*)		0x0057F0B4;
const u8* const	IS_TEAM_GAME			= (u8*)		0x00596875;
const u8* const	PLAYER_FORCES			= (u8*)		0x0057EEEA;	//+ player * 0x24

//CSprite::selectionIndex is a byte.
u32 circleSlot(u32 slot) {
	return slot < 255 ? slot : 255;
}

CUnit* parentOf(CUnit* unit) {
	if (units_dat::BaseProperty[unit->id] & UnitProperty::Subunit)
		return unit->subunit;
	return unit;
}

bool isDying(const CUnit* unit) {
	return unit->mainOrderId == OrderId::Die && unit->mainOrderState == 1;
}

} //unnamed namespace

namespace sellocal {

void requestRefresh() {
	*HOVER_DIALOG = 0;
	*HOVER_DIALOG_USER = 0;
	*REBUILD_CLIENT_SELECTION = 1;
	*REFRESH_BUTTON_SET = 1;
	*REFRESH_PORTRAIT = 1;
	*REFRESH_STAT_DATA = 1;
}

void buildActive(CUnit** list, u32 count) {
	for (u32 i = 0; i < SEL_MAX && activeSel[i] != NULL; i++) {
		CUnit* unit = activeSel[i];
		activeSel[i] = NULL;
		selexe::removeSelectionCircle(parentOf(unit));
	}
	if (count > SEL_MAX)
		count = SEL_MAX;
	for (u32 i = 0; i < count; i++) {
		CUnit* unit = parentOf(list[i]);
		activeSel[i] = unit;
		selexe::createSelectionCircle(unit, circleSlot(i));
		list[i] = unit;
	}
	mirrorActive();
}

void localRemove(CUnit* unit) {
	if (!(unit->sprite->flags & CSprite_Flags::Selected))
		return;
	CUnit* list[SEL_MAX];
	u32 n = listCount(activeSel, SEL_MAX);
	memcpy(list, activeSel, n * sizeof(CUnit*));
	if (listFind(list, n, unit) < 0)
		return;
	n = listRemove(list, n, unit);
	buildActive(list, n);
	requestRefresh();
	if (n == 0) {
		if (*PLACING_BUILDING != 0)
			selexe::cancelPlacement();
		if (*TARGETING != 0)
			selexe::cancelTargetOrder();
	}
}

void redrawCircles() {
	for (u32 i = 0; i < SEL_MAX && activeSel[i] != NULL; i++) {
		CUnit* unit = parentOf(activeSel[i]);
		activeSel[i] = unit;
		selexe::createSelectionCircle(unit, circleSlot(i));
	}
	mirrorActive();
	const u32 local = (u32)*LOCAL_HUMAN_ID;
	if (*IS_MULTIPLAYER == 0 || *IS_TEAM_GAME == 0 || local >= PLAYERS)
		return;
	for (int p = PLAYERS - 1; p >= 0; p--) {
		if ((u32)p == local || PLAYER_FORCES[p * 0x24] != PLAYER_FORCES[local * 0x24])
			continue;
		for (u32 j = 0; j < SEL_MAX && playersSel[p][j] != NULL; j++)
			selexe::addDashedCircle(playersSel[p][j]);
	}
}

void clientCopy() {
	memcpy(clientSel, activeSel, sizeof(clientSel));
	clientCount = 0;
	*activePortraitUnit = NULL;
	CUnit* best = NULL;
	for (u32 i = 0; i < SEL_MAX; i++) {
		CUnit* unit = clientSel[i];
		if (unit == NULL)
			continue;
		if (selexe::outranks(unit, best))
			best = unit;
		clientCount++;
	}
	*activePortraitUnit = best;
	if (clientCount == 1) {
		listClear(clientSel, SEL_MAX);
		clientSel[0] = best;
	}
	mirrorClient();
}

void deselectAndSend(CUnit* unit) {
	CUnit* list[SEL_MAX];
	u32 n = 0;
	for (u32 i = 0; i < SEL_MAX && activeSel[i] != NULL; i++)
		if (activeSel[i] != unit)
			list[n++] = activeSel[i];
	buildActive(list, n);
	selsend::cmdactSelect(n, list);
	requestRefresh();
}

void reselectAtStart() {
	CUnit* list[SEL_MAX];
	u32 n = 0;
	for (u32 i = 0; i < SEL_MAX && reselect[i] != NULL; i++) {
		CUnit* unit = reselect[i];
		if (unit->sprite == NULL || isDying(unit))
			continue;
		if (!(unit->sprite->visibilityFlags & *LOCAL_VISIBILITY))
			continue;
		list[n++] = unit;
	}
	if (n != 0) {
		bool done = false;
		if (n == 1 && *ALT_HELD != 0)
			done = selexe::selectRecentGroupOf(tagOf(list[0]));
		if (!done) {
			buildActive(list, n);
			selsend::cmdactSelect(n, list);
			*REBUILD_CLIENT_SELECTION = 1;
		}
	}
	*REFRESH_BUTTON_SET = 1;
	*REFRESH_PORTRAIT = 1;
	*REFRESH_STAT_DATA = 1;
	*HOVER_DIALOG = 0;
	*HOVER_DIALOG_USER = 0;
}

void gameStartClear() {
	clearAll();
}

void gameStartKeepLocal() {
	listClear(reselect, SEL_MAX);
	const u32 local = (u32)*LOCAL_HUMAN_ID;
	if (local < PLAYERS)
		memcpy(reselect, playersSel[local], sizeof(reselect));
	for (u32 p = 0; p < PLAYERS; p++) {
		listClear(playersSel[p], SEL_MAX);
		mirrorPlayer(p);
	}
	memset(pending, 0, sizeof(pending));
}

void addTwin(CUnit* twin) {
	CUnit* list[SEL_MAX];
	const u32 n = listCount(activeSel, SEL_MAX);
	if (n >= SEL_MAX)
		return;
	memcpy(list, activeSel, n * sizeof(CUnit*));
	list[n] = twin;
	buildActive(list, n + 1);
	selsend::cmdactSelect(n + 1, list);
	requestRefresh();
}

} //sellocal
```

- [ ] **Step 3:** Build after Task 5 (it needs `sel_send.h`). Commit with Task 5.

### Task 5: sending (`CMDACT_Select`, `CMDACT_HotkeyUnit`)

**Files:**
- Create: `GPTP/hooks/selection_ext/sel_send.h`, `GPTP/hooks/selection_ext/sel_send.cpp`

**Interfaces:**
- Consumes: `selext::*`, `selexe::queueCommand`, `selexe::centerViewOnGroup`.
- Produces: `selsend::cmdactSelect(u32 count, CUnit** list)`, `selsend::cmdactHotkey(u32 group, u32 action, CUnit** list, u32 count)`.

- [ ] **Step 1: Write `sel_send.h`**

File: `GPTP/hooks/selection_ext/sel_send.h`
```cpp
//Sending selection commands. A packet of 12 or fewer units goes out as
//vanilla 0x09/0x0A/0x0B; a larger one as select chunks (0x3C).
#pragma once
#include <SCBW/api.h>

namespace selsend {

//Sends the local player's new selection as the difference from the last one
//sent (0x4C0860).
void cmdactSelect(u32 count, CUnit** list);
//Sends a control-group command; a recall also becomes the last selection
//sent (0x4C07B0).
void cmdactHotkey(u32 group, u32 action, CUnit** list, u32 count);

} //selsend
```

- [ ] **Step 2: Write `sel_send.cpp`**

File: `GPTP/hooks/selection_ext/sel_send.cpp`
```cpp
#include "sel_send.h"
#include "sel_exe.h"
#include <SCBW/selection_ext.h>
#include <cstring>

using namespace selext;

namespace {

u8* const	LAST_HOTKEY			= (u8*)			0x00597280;
u32* const	LAST_HOTKEY_TICK	= (u32*)		0x0059727C;
const u8* const LOCAL_VISIBILITY	= (u8*)		0x0057F0B0;
const u8* const REPLAY_VISIBILITY	= (u8*)		0x006D0F18;
const u32 DOUBLE_TAP_MS = 500;

//Sends one packet: vanilla ids for 12 or fewer units, chunks for more.
void sendPacket(ChunkMode mode, const u16* tags, u32 count) {
	if (count <= VANILLA_MAX) {
		u8 command[2 + 2 * VANILLA_MAX];
		command[0] = mode == CHUNK_REPLACE ? 0x09 : mode == CHUNK_ADD ? 0x0A : 0x0B;
		command[1] = (u8)count;
		memcpy(&command[2], tags, count * sizeof(u16));
		selexe::queueCommand(command, 2 + 2 * count);
		return;
	}
	//The viewer's selection in a replay is local only, and the executor would
	//skip these anyway.
	if (*IS_IN_REPLAY)
		return;
	u8 chunk[CHUNK_MAX_BYTES];
	for (u32 c = 0; c < chunkCountFor(count); c++)
		selexe::queueCommand(chunk, chunkBuild(chunk, mode, tags, count, c));
}

} //unnamed namespace

namespace selsend {

void cmdactSelect(u32 count, CUnit** list) {
	static u16 added[SEL_MAX];
	static u16 removed[SEL_MAX];
	static u16 all[SEL_MAX];

	*LAST_HOTKEY = 0xFF;
	if (count > SEL_MAX)
		count = SEL_MAX;
	const u8 visible = *IS_IN_REPLAY ? *REPLAY_VISIBILITY : *LOCAL_VISIBILITY;
	const u32 lastCount = listCount(lastSent, SEL_MAX);

	u32 addedCount = 0;
	for (u32 k = 0; k < count; k++) {
		CUnit* unit = list[k];
		if (listFind(lastSent, lastCount, unit) >= 0)
			continue;
		if (unit->sprite->visibilityFlags & visible)
			added[addedCount++] = tagOf(unit);
	}
	u32 removedCount = 0;
	for (u32 s = 0; s < lastCount; s++)
		if (listFind(list, count, lastSent[s]) < 0)
			removed[removedCount++] = tagOf(lastSent[s]);

	listClear(lastSent, SEL_MAX);
	memcpy(lastSent, list, count * sizeof(CUnit*));
	mirrorLastSent();

	if (addedCount + removedCount >= count) {
		//A whole new selection, in reverse order as vanilla sends it.
		for (u32 k = 0; k < count; k++)
			all[count - 1 - k] = tagOf(list[k]);
		sendPacket(CHUNK_REPLACE, all, count);
	}
	else {
		if (removedCount != 0)
			sendPacket(CHUNK_REMOVE, removed, removedCount);
		if (addedCount != 0)
			sendPacket(CHUNK_ADD, added, addedCount);
	}
}

void cmdactHotkey(u32 group, u32 action, CUnit** list, u32 count) {
	const u8 command[3] = { 0x13, (u8)action, (u8)group };
	selexe::queueCommand(command, sizeof(command));
	if (action != 1) {
		*LAST_HOTKEY = 0xFF;
		return;
	}
	if (count > SEL_MAX)
		count = SEL_MAX;
	listClear(lastSent, SEL_MAX);
	memcpy(lastSent, list, count * sizeof(CUnit*));
	mirrorLastSent();
	const u32 now = GetTickCount();
	if (group == *LAST_HOTKEY && now - *LAST_HOTKEY_TICK < DOUBLE_TAP_MS) {
		selexe::centerViewOnGroup(group);
		*LAST_HOTKEY = 0xFF;
	}
	else {
		*LAST_HOTKEY = (u8)group;
		*LAST_HOTKEY_TICK = now;
	}
}

} //selsend
```
(`GetTickCount` comes from `<windows.h>`, which `SCBW/api.h` pulls in through `hook_tools.h`/`types.h`; if the build says it is undeclared, add `#include <windows.h>` at the top.)

- [ ] **Step 3: Build.** Expected: success.
- [ ] **Step 4: Commit** `git commit -m "feat: local selection and sending for the extended selection"` (with `sel_local.*`, `sel_send.*`).

### Task 6: save-file chunk

**Files:**
- Create: `GPTP/hooks/selection_ext/sel_save.h`, `GPTP/hooks/selection_ext/sel_save.cpp`

**Interfaces:**
- Produces: `size_t __cdecl selsave::writeLastAndExtension(const void*, size_t, size_t, FILE*)` (replaces the `fwrite` call at 0x4C2E0A), `size_t __cdecl selsave::readLastAndExtension(void*, size_t, size_t, FILE*)` (replaces the `fread` call at 0x4D0225).

- [ ] **Step 1: Write `sel_save.h`**

File: `GPTP/hooks/selection_ext/sel_save.h`
```cpp
//The save-file chunk of the extended selection. It follows the last vanilla
//write of a save (screenY) and is read after the last vanilla read.
#pragma once
#include <cstdio>

namespace selsave {

//Replaces the fwrite of screenY at 0x4C2E0A: writes it, then the chunk.
//Returns 0 on failure, which takes vanilla's failure path.
size_t __cdecl writeLastAndExtension(const void* data, size_t size, size_t count, FILE* file);
//Replaces the fread of screenY at 0x4D0225: reads it, then the chunk. An old
//save without the chunk loads with the vanilla 12; a damaged chunk returns 0,
//which ends the load with vanilla's error.
size_t __cdecl readLastAndExtension(void* data, size_t size, size_t count, FILE* file);

} //selsave
```

- [ ] **Step 2: Write `sel_save.cpp`**

File: `GPTP/hooks/selection_ext/sel_save.cpp`
```cpp
#include "sel_save.h"
#include "sel_exe.h"
#include <SCBW/selection_ext.h>

using namespace selext;

namespace {

const u32 MAGIC = 0x584C4553;	//"SELX"
const u16 VERSION = 1;

#pragma pack(push, 1)
struct Header {
	u32 magic;
	u16 version;
	u16 limit;			//the SEL_MAX of the build that saved
	u32 payloadBytes;	//playersSel as u16 tags, [PLAYERS][limit]
};
#pragma pack(pop)

CUnit* const* const VANILLA_PLAYERS_SEL = (CUnit* const*)0x006284E8;	//[8][12]

u16 tags[PLAYERS][SEL_MAX];

//An old save: the selections are vanilla's 12.
void fillFromVanilla() {
	for (u32 p = 0; p < PLAYERS; p++) {
		listClear(playersSel[p], SEL_MAX);
		u32 n = 0;
		for (u32 i = 0; i < VANILLA_MAX; i++)
			if (VANILLA_PLAYERS_SEL[p * VANILLA_MAX + i] != NULL)
				playersSel[p][n++] = VANILLA_PLAYERS_SEL[p * VANILLA_MAX + i];
	}
}

} //unnamed namespace

namespace selsave {

size_t __cdecl writeLastAndExtension(const void* data, size_t size, size_t count, FILE* file) {
	if (selexe::fwriteExe(data, size, count, file) != count)
		return 0;
	for (u32 p = 0; p < PLAYERS; p++)
		for (u32 i = 0; i < SEL_MAX; i++)
			tags[p][i] = tagOf(playersSel[p][i]);
	const Header header = { MAGIC, VERSION, (u16)SEL_MAX, sizeof(tags) };
	if (selexe::fwriteExe(&header, sizeof(header), 1, file) != 1)
		return 0;
	if (!selexe::writeCompressed(file, tags, sizeof(tags)))
		return 0;
	return count;
}

size_t __cdecl readLastAndExtension(void* data, size_t size, size_t count, FILE* file) {
	if (selexe::freadExe(data, size, count, file) != count)
		return 0;
	Header header;
	if (selexe::freadExe(&header, sizeof(header), 1, file) != 1
		|| header.magic != MAGIC || header.version != VERSION)
	{
		fillFromVanilla();
		return count;
	}
	const u32 limit = header.limit;
	if (limit == 0 || header.payloadBytes != PLAYERS * limit * sizeof(u16))
		return 0;
	u16* saved = new u16[PLAYERS * limit];
	const bool ok = selexe::readCompressed(file, saved, header.payloadBytes);
	if (ok)
		for (u32 p = 0; p < PLAYERS; p++) {
			listClear(playersSel[p], SEL_MAX);
			u32 n = 0;
			for (u32 i = 0; i < limit && n < SEL_MAX; i++) {
				CUnit* unit = unitOfTag(saved[p * limit + i]);
				if (unit != NULL)
					playersSel[p][n++] = unit;
			}
			mirrorPlayer(p);
		}
	delete[] saved;
	return ok ? count : 0;
}

} //selsave
```

- [ ] **Step 3: Build.** Expected: success.
- [ ] **Step 4: Commit** `git commit -m "feat: save-file chunk for the extended selection"`.

### Task 7: fixed-size consumers (archon merge, receive_command, smart-cast)

**Files:**
- Modify: `GPTP/hooks/recv_commands/CMDRECV_MergeArchon.cpp` (both functions, lines ~20–38 and ~153–171)
- Modify: `GPTP/hooks/recv_commands/receive_command.cpp:54-65`
- Modify: `GPTP/hooks/recv_commands/smart_cast.cpp:56,84-86`

- [ ] **Step 1: `CMDRECV_MergeArchon.cpp`.** Add `#include <SCBW/selection_ext.h>` after the first include. In **both** `CMDRECV_MergeDarkArchon()` and `CMDRECV_MergeArchon()`:
  - `CUnit* templars_stored[SELECTION_ARRAY_LENGTH];` → `CUnit* templars_stored[selext::SEL_MAX];`
  - `for(int i = 0;i < SELECTION_ARRAY_LENGTH;i++)` → `for(u32 i = 0;i < selext::SEL_MAX;i++)`
  - the store inside the `while` becomes
    ```cpp
    			if(current_unit->id == UnitId::ProtossDarkTemplar && templars_stored_count < (int)selext::SEL_MAX) {
    ```
    (`ProtossHighTemplar` in `CMDRECV_MergeArchon`).
- [ ] **Step 2: `receive_command.cpp`.** Add `#include <SCBW/selection_ext.h>`. Replace the two arrays and their bounds:
    ```cpp
    				static CUnit* selection[selext::SEL_MAX];
    				static CUnit* candidates[selext::SEL_MAX];
    				u32 selectionCount = 0, candidateCount = 0;

    				*selectionIndexStart = 0;
    				for(CUnit* unit = getActivePlayerNextSelection(); unit != NULL; unit = getActivePlayerNextSelection()) {
    					if(selectionCount < selext::SEL_MAX)
    						selection[selectionCount++] = unit;
    					if(candidateCount < selext::SEL_MAX && takesOrder(unit, unitParam, bActionOrder))
    						candidates[candidateCount++] = unit;
    				}
    ```
- [ ] **Step 3: `smart_cast.cpp`.** Add `#include <SCBW/selection_ext.h>`. `UnitRef roundSelection[PLAYER_COUNT][SELECTION_ARRAY_LENGTH];` → `UnitRef roundSelection[PLAYER_COUNT][selext::SEL_MAX];`, and in `followSelection` replace both `SELECTION_ARRAY_LENGTH` with `selext::SEL_MAX`.
- [ ] **Step 4: Build.** Expected: success; `grep -n SELECTION_ARRAY_LENGTH` in these three files prints nothing.
- [ ] **Step 5: Commit** `git commit -m "fix: iterator consumers take the whole selection"`.

### Task 8: stage 1 hooks

**Files:**
- Create: `GPTP/hooks/selection_ext/sel_inject.cpp`
- Modify: `GPTP/initialize.cpp` (include + one line under "ENABLED HOOKS")

**Interfaces:**
- Consumes: `selsync::*`, `sellocal::*`, `selsend::*`, `selsave::*`.
- Produces: `hooks::injectSelectionExtHooks()`.

- [ ] **Step 1: Write `sel_inject.cpp`** (stage 1 part; Task 11 adds the stage 2 part to the same file)

File: `GPTP/hooks/selection_ext/sel_inject.cpp`
```cpp
//Wrappers from the exe's calling conventions to the extended selection, and
//the functions that install them. Addresses and conventions: see the spec.
#include "selection_ext_hooks.h"
#include "sel_synced.h"
#include "sel_local.h"
#include "sel_send.h"
#include "sel_save.h"
#include <SCBW/selection_ext.h>
#include <hook_tools.h>

namespace {

//-------- Stage 1 --------//

//0x49A850: no arguments, the unit in EAX; every other register kept.
void __declspec(naked) nextSelectedWrapper() {
	static CUnit* unit;
	__asm PUSHAD
	unit = selsync::nextSelected();
	__asm {
		POPAD
		MOV EAX, unit
		RETN
	}
}

//0x49A7F0: EDI = unit.
void __declspec(naked) removeFromAllSelectionsWrapper() {
	static CUnit* unit;
	__asm {
		MOV unit, EDI
		PUSHAD
	}
	selsync::removeFromAllSelections(unit);
	__asm {
		POPAD
		RETN
	}
}

//0x49A740: EAX = player.
void __declspec(naked) clearSelectionWrapper() {
	static u32 player;
	__asm {
		MOV player, EAX
		PUSHAD
	}
	selsync::clearSelection(player);
	__asm {
		POPAD
		RETN
	}
}

//0x4C2750, 0x4C2560, 0x4BFB40: stdcall(packet).
void __declspec(naked) recvSelectWrapper() {
	static const u8* packet;
	__asm {
		MOV EAX, [ESP+4]
		MOV packet, EAX
		PUSHAD
	}
	selsync::recvSelect(packet);
	__asm {
		POPAD
		RETN 4
	}
}

void __declspec(naked) recvShiftSelectWrapper() {
	static const u8* packet;
	__asm {
		MOV EAX, [ESP+4]
		MOV packet, EAX
		PUSHAD
	}
	selsync::recvShiftSelect(packet);
	__asm {
		POPAD
		RETN 4
	}
}

void __declspec(naked) recvShiftDeselectWrapper() {
	static const u8* packet;
	__asm {
		MOV EAX, [ESP+4]
		MOV packet, EAX
		PUSHAD
	}
	selsync::recvShiftDeselect(packet);
	__asm {
		POPAD
		RETN 4
	}
}

//0x4C2870: ECX = packet.
void __declspec(naked) recvHotkeyWrapper() {
	static const u8* packet;
	__asm {
		MOV packet, ECX
		PUSHAD
	}
	selsync::recvHotkey(packet);
	__asm {
		POPAD
		RETN
	}
}

//0x49AE40: EAX = list, stdcall(count).
void __declspec(naked) buildActiveWrapper() {
	static CUnit** list;
	static u32 count;
	__asm {
		MOV list, EAX
		MOV EAX, [ESP+4]
		MOV count, EAX
		PUSHAD
	}
	sellocal::buildActive(list, count);
	__asm {
		POPAD
		RETN 4
	}
}

//0x49F7A0: EAX = unit.
void __declspec(naked) localRemoveWrapper() {
	static CUnit* unit;
	__asm {
		MOV unit, EAX
		PUSHAD
	}
	sellocal::localRemove(unit);
	__asm {
		POPAD
		RETN
	}
}

//0x499A60: no arguments.
void __declspec(naked) redrawCirclesWrapper() {
	__asm PUSHAD
	sellocal::redrawCircles();
	__asm {
		POPAD
		RETN
	}
}

//0x4C38B0: no arguments; ends with a tail jump to 0x458DE0 (button set).
const u32 Func_UpdateButtonSet = 0x00458DE0;
void __declspec(naked) clientCopyWrapper() {
	__asm PUSHAD
	sellocal::clientCopy();
	__asm {
		POPAD
		JMP Func_UpdateButtonSet
	}
}

//0x4C3B40: EDX = unit.
void __declspec(naked) deselectAndSendWrapper() {
	static CUnit* unit;
	__asm {
		MOV unit, EDX
		PUSHAD
	}
	sellocal::deselectAndSend(unit);
	__asm {
		POPAD
		RETN
	}
}

//0x4D0820: no arguments.
void __declspec(naked) reselectAtStartWrapper() {
	__asm PUSHAD
	sellocal::reselectAtStart();
	__asm {
		POPAD
		RETN
	}
}

//0x4EED10 entry (8 bytes: push edi; xor eax, eax; mov ecx, 0xC).
const u32 Back_GameStartEntry = 0x004EED18;
void __declspec(naked) gameStartEntryWrapper() {
	__asm PUSHAD
	sellocal::gameStartClear();
	__asm {
		POPAD
		PUSH EDI
		XOR EAX, EAX
		MOV ECX, 0x0C
		JMP Back_GameStartEntry
	}
}

//0x4EEDC6 (5 bytes: mov eax, [0x512688]), where vanilla keeps the local
//player's 12 and wipes the synced selections.
const u32 Back_GameStartKeepLocal = 0x004EEDCB;
void __declspec(naked) gameStartKeepLocalWrapper() {
	__asm PUSHAD
	sellocal::gameStartKeepLocal();
	__asm {
		POPAD
		MOV EAX, 0x00512688
		MOV EAX, [EAX]
		JMP Back_GameStartKeepLocal
	}
}

//0x4C0860: stdcall(count, list).
void __declspec(naked) cmdactSelectWrapper() {
	static u32 count;
	static CUnit** list;
	__asm {
		MOV EAX, [ESP+4]
		MOV count, EAX
		MOV EAX, [ESP+8]
		MOV list, EAX
		PUSHAD
	}
	selsend::cmdactSelect(count, list);
	__asm {
		POPAD
		RETN 8
	}
}

//0x4C07B0: BL = group, stdcall(u8 action, CUnit** list, u8 count).
void __declspec(naked) cmdactHotkeyWrapper() {
	static u32 group, action, count;
	static CUnit** list;
	__asm {
		MOV group, EBX
		MOV EAX, [ESP+4]
		MOV action, EAX
		MOV EAX, [ESP+8]
		MOV list, EAX
		MOV EAX, [ESP+12]
		MOV count, EAX
		PUSHAD
	}
	selsend::cmdactHotkey(group & 0xFF, action & 0xFF, list, count & 0xFF);
	__asm {
		POPAD
		RETN 12
	}
}

} //unnamed namespace

namespace hooks {

void injectSelectionExtHooks() {
	jmpPatch(nextSelectedWrapper,				0x0049A850, 2);
	jmpPatch(removeFromAllSelectionsWrapper,	0x0049A7F0, 1);
	jmpPatch(clearSelectionWrapper,				0x0049A740, 1);
	jmpPatch(recvSelectWrapper,					0x004C2750, 1);
	jmpPatch(recvShiftSelectWrapper,			0x004C2560, 1);
	jmpPatch(recvShiftDeselectWrapper,			0x004BFB40, 1);
	jmpPatch(recvHotkeyWrapper,					0x004C2870, 0);
	jmpPatch(buildActiveWrapper,				0x0049AE40, 0);
	jmpPatch(localRemoveWrapper,				0x0049F7A0, 1);
	jmpPatch(redrawCirclesWrapper,				0x00499A60, 1);
	jmpPatch(clientCopyWrapper,					0x004C38B0, 0);
	jmpPatch(deselectAndSendWrapper,			0x004C3B40, 1);
	jmpPatch(reselectAtStartWrapper,			0x004D0820, 1);
	jmpPatch(gameStartEntryWrapper,				0x004EED10, 3);
	jmpPatch(gameStartKeepLocalWrapper,			0x004EEDC6, 0);
	jmpPatch(cmdactSelectWrapper,				0x004C0860, 1);
	jmpPatch(cmdactHotkeyWrapper,				0x004C07B0, 0);
	callPatch(selsave::writeLastAndExtension,	0x004C2E0A, 0);
	callPatch(selsave::readLastAndExtension,	0x004D0225, 0);
}

} //hooks
```
(Before writing the `nops` counts, re-check each entry's instruction boundaries with `python disat.py <addr> 4` from `D:\SC Modding\resexp-analysis`; the counts above only pad the rest of the cut instruction and are cosmetic for whole-function replacements. For 0x4EED10 and 0x4EEDC6 they must be exactly 3 and 0.)

- [ ] **Step 2: Register in `initialize.cpp`.** Add `#include "hooks/selection_ext/selection_ext_hooks.h"` with the other hook includes, and under "ENABLED HOOKS", after `hooks::injectSelectLarvaHooks();`:
```cpp
	//Selections above 12 units (hooks/selection_ext, SCBW/selection_ext.h).
	hooks::injectSelectionExtHooks();
```
- [ ] **Step 3: Build.** Expected: success.
- [ ] **Step 4: Commit** `git commit -m "feat: install the extended selection hooks"`.

### Task 9: stage 1 in-game test round

- [ ] **Step 1:** Touch `game_hooks.cpp`, build, report the build stamp to the user.
- [ ] **Step 2:** Give the user this round (hero units per memory; any map with Zerglings/Marines/Templar works because nothing in unit data is changed):

1.1 Start a game. Expected: the build stamp and "extended selection: self-test passed".
1.2 Drag-select 12 units, right-click to move, then attack-move. Expected: all 12 move/attack; 12 circles.
1.3 Shift-click a selected unit, shift-click an unselected one. Expected: removed/added, circles follow, wireframes update.
1.4 Ctrl+1 on 12 units, click away, press 1, press 1 twice fast. Expected: the 12 return; the screen centres on the double tap.
1.5 Alt-click one unit of a recent selection. Expected: that recent selection returns.
1.6 Let a selected unit die, and load one into a Dropship/Bunker. Expected: it leaves the selection and the wireframes; no crash.
1.7 Select 4 High Templar, press Archon Warp twice. Expected: two Archons, one pair per press.
1.8 Select 2 Ghosts, cast Lockdown twice. Expected: each Ghost casts once.
1.9 With 12 selected, save the game (menu), keep playing. Expected: circles remain on all 12 after the save.
1.10 Load that save, then a save made with an older build. Expected: both load; the first restores the 12 selected, the second too.
1.11 Mind-control a selected enemy unit (or have one of yours mind-controlled). Expected: no crash; it leaves the old owner's selection.

- [ ] **Step 3:** Fix what fails (crash reports: `D:\Games\Starcraft 1.16.1\Errors\*.ERR`, last entry), rebuild, repeat.

---

# Stage 2

### Task 10: client list builders to `SEL_MAX`

**Files:**
- Modify: `GPTP/hooks/interface/selection.cpp` (lines 203, 303–304, 361, 386–407, 454–455, 468–469, 550–551, 586–611, 620–641)

- [ ] **Step 1:** Add `#include <SCBW/selection_ext.h>` after `#include "resolution.h"`.
- [ ] **Step 2:** `SortAllUnits` line 203: `SELECTION_ARRAY_LENGTH` → `selext::SEL_MAX`.
- [ ] **Step 3:** `combineSelectionsLists` lines 303–304 and 361: `SELECTION_ARRAY_LENGTH` → `selext::SEL_MAX`.
- [ ] **Step 4:** `getSelectedUnitsInBox`: arrays become `static CUnit* local_array_1[selext::SEL_MAX + 1];` and `local_array_2[selext::SEL_MAX + 1]` (one extra null: `combineSelectionsLists` reads until a null); the clear loop runs to `selext::SEL_MAX + 1`; the copy of the current selection reads `selext::activeSel[i]` for `i < selext::SEL_MAX` and sets `local_array_2[selext::SEL_MAX] = NULL`.
- [ ] **Step 5:** `getSelectedUnitsAtPoint`: the two arrays become `static CUnit* local_temp_array_1[selext::SEL_MAX + 1]` / `local_temp_array_2[...]`; every `SELECTION_ARRAY_LENGTH` in the function → `selext::SEL_MAX`; every `activePlayerSelection->unit[...]` → `selext::activeSel[...]`; the loops that clear/copy also clear index `selext::SEL_MAX`.
- [ ] **Step 6:** Replace the shift-click removal (the block from `u32 memcpy_size;` through `local_temp_array_1[arrayIndex] = NULL;`, including vanilla's `arrayIndex--;`) with a search, since `CSprite::selectionIndex` is capped at 255:
```cpp
						//unit already selected, remove it from selection
						bool bUpdateSelection = true;
						arrayIndex = (int)selext::listRemove(local_temp_array_1, (u32)arrayIndex, clicked_unit);
```
  and change the following `if(arrayIndex == 1)` logic to use the new `arrayIndex` (it already does: `arrayIndex` is now the count after removal, as vanilla's decremented index was).
- [ ] **Step 7:** Build; `grep -n "SELECTION_ARRAY_LENGTH\|activePlayerSelection" GPTP/hooks/interface/selection.cpp` must only show `selectUnitGroup` (control groups, stage 4).
- [ ] **Step 8: Commit** `git commit -m "feat: drag box and clicks select up to the limit"`.

### Task 11: the select-chunk command (receive, lengths, twin)

**Files:**
- Modify: `GPTP/hooks/selection_ext/sel_inject.cpp` (add the stage 2 part)
- Modify: `GPTP/initialize.cpp` (one line)

- [ ] **Step 1:** Re-check the patch windows (from `D:\SC Modding\resexp-analysis`): `python disat.py 486619 8` (must end with `2bc2 sub eax,edx` at 0x486632), `python disat.py 4CE082 8` (ends at `85db test ebx,ebx` 0x4CE09B), `python disat.py 4CDD00 10` (`8d741601 lea esi,[esi+edx+1]` at 0x4CDD1E), and the dword at 0x486ED0 is 0x00486DA3.
- [ ] **Step 2:** Add to `sel_inject.cpp`, inside the unnamed namespace after the stage 1 wrappers:
```cpp
//-------- Stage 2 --------//

u32 __stdcall commandLengthOf(const u8* command) {
	return selext::commandLength(command);
}

void __cdecl recvSelectChunkC(const u8* packet) {
	selsync::recvSelectChunk(packet);
}

//Dispatch slot 50 of the executor 0x4865D0 (ids 0x3C-0x44, 0x48, 0x5B). ESI =
//command, EAX = bytes left, [EBP+8] = bytes left, [EBP-4] = command size.
const u32 Back_CommandDone = 0x00486D7A;	//records the command and advances
const u32 Back_CommandBad = 0x00486DA3;		//vanilla's exit for these ids
void __declspec(naked) selectChunkDispatch() {
	__asm {
		CMP BYTE PTR [ESI], 0x3C
		JNE notOurs
		CMP EAX, 3
		JL notOurs
		MOVZX ECX, BYTE PTR [ESI+2]
		LEA ECX, [ECX*2+3]
		SUB EAX, ECX
		MOV [EBP+8], EAX
		JS notOurs
		MOV [EBP-4], ECX
		PUSHAD
		PUSH ESI
		CALL recvSelectChunkC
		ADD ESP, 4
		POPAD
		JMP Back_CommandDone
	notOurs:
		JMP Back_CommandBad
	}
}

//0x486619 (executor, replay viewer's skip path): ECX = id, ESI = command;
//out EDX = length, then 0x486632.
const u32 Back_ExecutorLength = 0x00486632;
void __declspec(naked) executorLengthStub() {
	__asm {
		PUSH EAX
		PUSH ECX
		PUSH ESI
		CALL commandLengthOf
		MOV EDX, EAX
		POP ECX
		POP EAX
		JMP Back_ExecutorLength
	}
}

//0x4CE082 (replay playback 0x4CDFF0): EAX = id, ECX = command; out EBX =
//length, then 0x4CE09B. ECX and EDX are used afterwards.
const u32 Back_PlaybackLength = 0x004CE09B;
void __declspec(naked) playbackLengthStub() {
	__asm {
		PUSH ECX
		PUSH EDX
		PUSH ECX
		CALL commandLengthOf
		MOV EBX, EAX
		POP EDX
		POP ECX
		JMP Back_PlaybackLength
	}
}

//0x4CDD04 (replay save walk 0x4CDCE0): EAX + 1 = command; does vanilla's
//INC EAX, out EDX = length, then 0x4CDD1E.
const u32 Back_SaveWalkLength = 0x004CDD1E;
void __declspec(naked) saveWalkLengthStub() {
	__asm {
		INC EAX
		PUSH EAX
		PUSH ECX
		PUSH EAX
		CALL commandLengthOf
		MOV EDX, EAX
		POP ECX
		POP EAX
		JMP Back_SaveWalkLength
	}
}

//0x45D040: stdcall(twin).
void __declspec(naked) addTwinWrapper() {
	static CUnit* twin;
	__asm {
		MOV EAX, [ESP+4]
		MOV twin, EAX
		PUSHAD
	}
	sellocal::addTwin(twin);
	__asm {
		POPAD
		RETN 4
	}
}
```
and inside `namespace hooks`:
```cpp
void injectSelectChunkHooks() {
	memoryPatch(0x00486ED0, (u32)&selectChunkDispatch);
	jmpPatch(executorLengthStub,	0x00486619, 20);
	jmpPatch(playbackLengthStub,	0x004CE082, 20);
	jmpPatch(saveWalkLengthStub,	0x004CDD04, 21);
	jmpPatch(addTwinWrapper,		0x0045D040, 1);
}
```
- [ ] **Step 3:** In `initialize.cpp`, right after `hooks::injectSelectionExtHooks();`:
```cpp
	hooks::injectSelectChunkHooks();
```
- [ ] **Step 4:** Build. Expected: success.
- [ ] **Step 5: Commit** `git commit -m "feat: select chunks for selections above 12"`.

### Task 12: stage 2 in-game test round

- [ ] **Step 1:** Touch `game_hooks.cpp`, build, report the stamp.
- [ ] **Step 2:** A test map with many hero units (e.g. 400 Devouring One, the Zergling hero, created by a trigger at start) is needed; ask the user to make or pick one. Round:

2.1 Start. Expected: self-test passed.
2.2 Drag-select 400 heroes. Expected: circles on all; move/attack/patrol affect all (watch the far edge of the group).
2.3 Shift-drag another group while 400 are selected. Expected: refused (nothing added), as vanilla refuses past 12.
2.4 Shift-click to remove single units from a 300-unit selection; then drag-deselect is not a vanilla feature, so select 200 of them anew. Expected: the selection follows each click.
2.5 Ctrl-click one unit type on screen with more than 12 on screen. Expected: all of that type on screen selected.
2.6 Make the game lag (drag the window or alt-tab while moving 400) and reselect rapidly. Expected: no crash; the selection settles on the last one.
2.7 Select Zergling eggs (hero Zerglings don't come from eggs: use normal Larva → Zergling eggs for this one) with more than 12 units selected. Expected: both twins join.
2.8 Save the replay; watch it. Expected: the large selections play back identically.
2.9 Save with 200 selected, load. Expected: the 200 are selected again.
2.10 Cast Psi Storm with 30 High Templar (heroes: Tassadar/Zeratul is not one; use Hero_Tassadar or regular HT in a test map). Expected: smart-cast cycles through all 30.

- [ ] **Step 3:** Fix what fails, rebuild, repeat.

### Task 13: documentation

- [ ] Update the spec's status line to "stage 1+2 built (date), tested in game"; list any deviation found in testing under "Known issues and follow-ups".
- [ ] Update `C:\Users\vnghi\.claude\projects\D--SC-Modding-GPTP\memory\selection-400-work.md` with what is built.
- [ ] Commit `git commit -m "docs: extended selection stages 1+2 built"`.
