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
//Writes list (n entries, at most 12 used) into the client mirror, with count
//(capped at 12) as its count, for a check that reads it; mirrorClient() puts
//the real one back. The count stays the real selection's, so conditions that
//need one unit selected (the Build menus) still see the whole selection.
void mirrorClientView(CUnit* const* list, u32 n, u32 count);

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

//-------- Selection panel pages (stage 3) --------//

//Wireframe controls of statdata.bin: ids 0x21 onward, 2 rows, columns 36 px
//apart from x 30 (make_statdata_wide.py writes WIREFRAME_MAX of them).
const u32 WIREFRAME_FIRST_ID = 0x21;
const u32 WIREFRAME_MAX = 90;
//Wireframes per page: the columns that fit a StatData dialog this wide (the
//vanilla 270 fits 6), two rows, at most the controls present.
u32 pageSizeFor(u32 dialogWidth, u32 wireframeControls);
//Pages for count units (at least 1).
u32 pageCountFor(u32 count, u32 pageSize);
//page, moved back to the last page if it is past it.
u32 clampPage(u32 page, u32 count, u32 pageSize);
//Local: the page of the selection the panel shows.
extern u32 selectionPage;
//The page after a key press: Ctrl+PgUp/PgDn (virtual keys 0x21/0x22) turn it,
//stopping at the ends; nothing else does, nor anything while chat is open.
//(Plain PgUp/PgDn scroll the map diagonally in vanilla.)
u32 pageAfterKey(u16 key, bool ctrlHeld, bool chatOpen, u32 page, u32 pages);

//-------- Control groups (stage 4) --------//

const u32 GROUP_COUNT = 18;			//Ctrl+0-9, then the 8 recent selections
const u32 FIRST_RING_GROUP = 10;
//Synced: each player's control groups, as unit tags, compact.
extern u16 groupsExt[PLAYERS][GROUP_COUNT][SEL_MAX];
//Copies a group's first 12 into vanilla's u32[8][18][12] at 0x57FE60.
void mirrorGroup(u32 player, u32 group);
//Of the 8 recent-selection groups (ring[0..7]), the one holding tag with the
//newest stamp, ties going to the higher group, or -1 (vanilla 0x496D30).
int newestRingGroupWith(const u16 (*ring)[SEL_MAX], const u16* stamps, u16 tag);

//-------- Subgroups (stage 5) --------//

const u32 UNIT_TYPES = 228;
//The type a unit counts as in a subgroup: siege mode counts as tank mode
//(Siege Tank 30 -> 5, Edmund Duke 25 -> 23).
u16 subgroupType(u16 unitId);
//Default priority: heroes above everything, then higher build score.
u16 defaultPriority(bool hero, u16 buildScore);
//Sort key: ascending is panel order (higher priority, then lower type, real
//units before hallucinations). type must be below 512.
u32 subgroupKey(u16 priority, u16 type, bool hallucination);
//Sorts units by keys (parallel arrays), keeping the order of equal keys.
void sortBySubgroup(CUnit** units, u32* keys, u32 n);
//Whether a selection change keeps the active subgroup. A fresh selection (a
//click, box, recall or Alt-click without Shift) never does; otherwise it
//does if the new selection holds every old unit (an add) or the old one
//holds every new unit (a removal, a death).
bool keepsActive(bool fresh, CUnit* const* before, u32 nb, CUnit* const* after, u32 na);
//The active key after a change. keys: the new selection's keys, sorted, n > 0.
//keep and oldKey present: oldKey. Otherwise (a fresh selection, or the active
//subgroup killed or removed): the first, as in SC2.
u32 activeKeyAfter(const u32* keys, u32 n, u32 oldKey, bool keep);
//The next (back: previous) distinct key after active, wrapping. n > 0.
u32 keyAfterTab(const u32* keys, u32 n, u32 active, bool back);
//Of two button condition results, the better: Enabled 1 > Disabled -1 > Invisible 0.
s32 betterButtonState(s32 a, s32 b);
//The palette entry (rgbx, 4 bytes each, 256 of them) nearest by squared RGB
//distance to entry index scaled by percent, leaving out the entries marked
//in skip (256 flags); ties to the lower index.
u8 dimIndex(const u8* palette, const bool* skip, u8 index, u32 percent);
//Marks in cycling (256 flags) the palette entries the tileset's colour
//cycling rotates: records are count 16-byte records (0x6CE2A0); one with
//byte 1 set cycles entries byte 3 to byte 5.
void cyclingEntries(const u8* records, u32 count, bool* cycling);
//Whether two palettes (rgbx x 256) are equal outside the skipped entries.
bool samePalette(const u8* a, const u8* b, const bool* skip);

//Tab cycles subgroups, so the minimap's vanilla Tab toggles (0x4A5938) move:
//Alt+T hides/shows terrain, Ctrl+Shift+T cycles the ally colours (user,
//2026-10-03). Which toggle a key press is, if any.
//-------- Smart-build --------//

//Queued build: [0x3D][order][u16 x tile][u16 y tile][u16 type][u8 Shift
//sequence], 0x0C plus the sequence (1-63) it was placed in.
const u8 CMD_QUEUED_BUILD = 0x3D;
const u32 QUEUED_BUILD_BYTES = 9;
//A queued build entry's type field: the type (bits 0-8), its Shift sequence
//(bits 9-14) and the mark (bit 15).
const u16 QUEUED_TYPE_MASK = 0x01FF;
u16 packQueuedType(u16 type, u32 sequence);
u16 queuedTypeOf(u16 packed);
u32 queuedSequenceOf(u16 packed);
//The next Shift sequence number: 1-63, wrapping (0 is a plain placement).
u32 nextShiftSequence(u32 sequence);
//Whether a queued, unstarted building blocks a new placement over it: always
//for a plain placement (sequence 0), else unless it is of the same sequence.
bool queuedSiteBlocks(u32 newSequence, u32 queuedSequence);
//The placement grid cell (row, column) of a building whose top-left tile is
//(tileX, tileY): its centre in pixels (cells are 32x32 tiles).
void placementCellCentre(u32 tileX, u32 tileY, u32 row, u32 column, s32* x, s32* y);
//The placement grid's status byte of cell (row, column) of box (0: the
//building, 1: an addon) in BW's array at 0x6408F8: 48 bytes a box, 6 a row.
u32 placementCellIndex(u32 box, u32 row, u32 column);
//Whether two footprints (centres and sizes in pixels) overlap.
bool footprintsOverlap(s32 x1, s32 y1, s32 w1, s32 h1, s32 x2, s32 y2, s32 w2, s32 h2);
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
//The nearest able entry among the preferred ones, else among all able ones;
//or -1 (a plain placement prefers workers that aren't constructing).
int nearestPreferring(const s32* xs, const s32* ys, const bool* able, const bool* preferred,
                      u32 n, s32 x, s32 y);
//Whether a worker's order holds a building: the three build orders, an SCV
//constructing (0x21), and the Drone's DroneLand (0x46) / DroneBuild (0x1A),
//which its DroneStartBuild becomes at once. A Shift-placement queues behind
//it (a Drone's is recycled instead).
bool holdsBuild(u32 order);
//A build order gave up: "Couldn't reach the building site." when the
//worker got stuck (Unmovable) or stopped out of reach; not after a money
//failure (within reach, its own error shown).
bool showCantReach(bool stuck, bool withinReach);
//Prepaid Shift-placements: a unit has paid paidM/paidG for the builds it
//holds; it now holds builds worth heldM/heldG. What it no longer holds is
//refunded (out), and paid becomes held.
void reconcilePaid(u32* paidM, u32* paidG, u32 heldM, u32 heldG, u32* refundM, u32* refundG);
//Whether a placement click with Shift held queues (0x3D) and keeps placing:
//only a worker's build order (Drone, Terran, Protoss), never an addon or a
//landing.
bool shiftQueues(bool shiftHeld, u32 order);
//Whether placing goes on when the game asks (0x48DDA0, e.g. as a building
//starts): with members selected, while Shift-queuing (as SC2: the cursor
//stays while Shift is held), else if any member can still place it.
bool placingHolds(bool shiftQueuing, bool anyMemberCan, u32 members);
//Whether a worker can take a Shift-queued build: free to take it now
//(0x48DBD0), or already building and its type can make it (it queues).
bool ableToQueue(bool allowedNow, bool holdingBuild, bool canMake);
//Whether a queued build must first take out the queue's last order: the
//unit's own return-to-idle order, which vanilla queues when an SCV starts
//constructing with an empty queue (0x467FD0); a build after it never runs.
bool dropsTrailingIdle(u32 lastQueuedOrder, u32 unitIdleOrder);
//Whether a player with these resources can pay this cost now.
bool canAfford(s32 minerals, s32 gas, u32 costM, u32 costG);

enum MinimapToggle { MINIMAP_NONE = 0, MINIMAP_TERRAIN = 1, MINIMAP_ALLY_COLOURS = 2 };
MinimapToggle minimapToggleFor(u16 key, bool shift, bool ctrl, bool alt);

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
