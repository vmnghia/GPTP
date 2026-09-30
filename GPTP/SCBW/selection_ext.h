//Selections larger than vanilla's 12 units.
//Design: docs/superpowers/specs/2026-09-30-extended-selection-design.md
//
//The arrays here are the real selections. Vanilla's 12-slot arrays are kept
//as a mirror of the first 12 entries, for exe code that still reads them.
//Every list is compact: its units come first, then nulls.
//
//The pure logic (lists, chunks, the ring slot) is in selection_ext_core.cpp,
//which the host test in tests/ builds on its own; the rest, which touches
//the game's memory, is in selection_ext.cpp.
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
//Bits 4-7: the chunk's place in its packet, mod 16, so a lost chunk is seen.
const u8 CHUNK_INDEX_SHIFT = 4;

//How many chunks a packet of count tags takes (at least 1).
u32 chunkCountFor(u32 count);
//Writes chunk number index of a packet into out (CHUNK_MAX_BYTES at least)
//and returns its length.
u32 chunkBuild(u8* out, ChunkMode mode, const u16* tags, u32 count, u32 index);

//A packet being received, one per player. Synced.
struct PendingPacket {
	bool open;
	u8 mode;
	u8 next;	//index (mod 16) of the chunk expected next
	u32 count;
	u16 tags[SEL_MAX];
};
extern PendingPacket pending[PLAYERS];
//Feeds one whole chunk command. Returns true when it completes the packet,
//which is then in p (and p is closed). A chunk out of place (none before it,
//another mode, or one missing in between) drops the packet.
bool chunkFeed(PendingPacket& p, const u8* cmd);

//Length of a variable-length command (the select chunk, or 0x09-0x0B), or 0
//for any other id.
u32 variableCommandLength(const u8* cmd);
//Length of any command: variableCommandLength, else vanilla's table 0x5005F8.
u32 commandLength(const u8* cmd);

//-------- Recent-selection ring --------//

//The ring slot to reuse: the oldest of the 8 stamps, ties going to the
//higher slot; 0xFF when every stamp is 0xFFFF (vanilla 0x496560).
u8 oldestRingSlot(const u16* stamps);

//-------- Image pools --------//

//Appends count nodes (stride bytes apart, each starting with a prev and a next
//pointer, like CImage's link) to the end of a free list given by its head and
//tail pointers. The exe takes images from the head.
void freeListAppend(void** head, void** tail, void* nodes, u32 count, u32 stride);

//Clears every array, its mirror, and the pending chunks.
void clearAll();

//Runs the checks of the pure logic. Returns the number that failed, and the
//source line of the first failure.
u32 selfTest(u32* firstFailedLine);

} //selext
