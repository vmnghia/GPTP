//Checks of the pure selection logic (SCBW/selection_ext_core.cpp). They run
//once at the start of a game, which prints the result, and in the host test
//tests/selection_ext_test.bat.
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
CUnit* fake(u32 n) {
	return (CUnit*)(0x1000 + 0x10 * n);
}

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

	listClear(swap, 4);
	CHECK(listCount(swap, 4) == 0);
}

void chunkRoundTrip() {
	static u16 tags[SEL_MAX];
	for (u32 i = 0; i < SEL_MAX; i++)
		tags[i] = (u16)(i + 1);
	const u32 count = 300;
	CHECK(chunkCountFor(0) == 1);
	CHECK(chunkCountFor(125) == 1);
	CHECK(chunkCountFor(126) == 2);
	CHECK(chunkCountFor(count) == 3);

	static PendingPacket p;
	memset(&p, 0, sizeof(p));
	u8 buf[CHUNK_MAX_BYTES];
	bool done = false;
	for (u32 c = 0; c < chunkCountFor(count); c++) {
		const u32 len = chunkBuild(buf, CHUNK_ADD, tags, count, c);
		CHECK(len <= CHUNK_MAX_BYTES && len == variableCommandLength(buf));
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
	static PendingPacket p;
	memset(&p, 0, sizeof(p));
	u8 first[CHUNK_MAX_BYTES], second[CHUNK_MAX_BYTES], other[CHUNK_MAX_BYTES];
	chunkBuild(first, CHUNK_REPLACE, tags, 200, 0);
	chunkBuild(second, CHUNK_REPLACE, tags, 200, 1);
	chunkBuild(other, CHUNK_REMOVE, tags, 200, 1);

	//A later chunk with no first one is dropped.
	CHECK(!chunkFeed(p, second) && !p.open);
	//A chunk of another mode in between drops the packet.
	chunkFeed(p, first);
	CHECK(!chunkFeed(p, other) && !p.open);
	CHECK(!chunkFeed(p, second));
	//A new first chunk restarts the packet.
	chunkFeed(p, first);
	CHECK(chunkFeed(p, second) && p.count == 200);
	//A count above the chunk limit is dropped.
	first[2] = (u8)(CHUNK_MAX_UNITS + 1);
	CHECK(!chunkFeed(p, first) && !p.open);
}

//A lost middle chunk (the sender's queue can drop one under lag) must drop
//the whole packet, not commit the chunks that did arrive.
void chunkMissingMiddle() {
	static u16 tags[SEL_MAX];
	for (u32 i = 0; i < SEL_MAX; i++)
		tags[i] = (u16)(i + 1);
	static PendingPacket p;
	memset(&p, 0, sizeof(p));
	u8 c0[CHUNK_MAX_BYTES], c1[CHUNK_MAX_BYTES], c2[CHUNK_MAX_BYTES];
	chunkBuild(c0, CHUNK_REPLACE, tags, 300, 0);
	chunkBuild(c1, CHUNK_REPLACE, tags, 300, 1);
	chunkBuild(c2, CHUNK_REPLACE, tags, 300, 2);

	chunkFeed(p, c0);
	CHECK(!chunkFeed(p, c2) && !p.open);	//c1 never arrived
	//The same chunk twice is a gap too.
	chunkFeed(p, c0);
	chunkFeed(p, c1);
	CHECK(!chunkFeed(p, c1) && !p.open);
	//In order, the packet completes.
	chunkFeed(p, c0);
	chunkFeed(p, c1);
	CHECK(chunkFeed(p, c2) && p.count == 300);
}

void lengths() {
	const u8 select[] = { 0x09, 3, 0, 0, 0, 0, 0, 0 };
	CHECK(variableCommandLength(select) == 8);
	const u8 chunk[] = { CMD_SELECT_CHUNK, CHUNK_FIRST | CHUNK_LAST, 2, 0, 0, 0, 0 };
	CHECK(variableCommandLength(chunk) == 7);
	const u8 hotkey[] = { 0x13, 0, 0 };
	CHECK(variableCommandLength(hotkey) == 0);
	const u8 queued[] = { CMD_QUEUED_BUILD, 0x1E, 0, 0, 0, 0, 0, 0 };
	CHECK(variableCommandLength(queued) == 8);
}

void ring() {
	const u16 stamps[8] = { 5, 3, 9, 3, 7, 8, 6, 4 };
	CHECK(oldestRingSlot(stamps) == 3);	//ties go to the higher slot
	const u16 zeros[8] = { 0 };
	CHECK(oldestRingSlot(zeros) == 7);
	u16 full[8];
	for (u32 i = 0; i < 8; i++)
		full[i] = 0xFFFF;
	CHECK(oldestRingSlot(full) == 0xFF);
}

//The selection circle and health bar pools are free lists of 0x40-byte images.
struct Node {
	Node* prev;
	Node* next;
	u8 rest[0x38];
};

void freeLists() {
	static Node vanilla[2];
	static Node extra[3];
	memset(vanilla, 0, sizeof(vanilla));
	vanilla[0].next = &vanilla[1];
	vanilla[1].prev = &vanilla[0];
	void* head = &vanilla[0];
	void* tail = &vanilla[1];
	freeListAppend(&head, &tail, extra, 3, sizeof(Node));
	CHECK(head == &vanilla[0] && tail == &extra[2]);
	CHECK(vanilla[1].next == &extra[0] && extra[0].prev == &vanilla[1]);
	CHECK(extra[0].next == &extra[1] && extra[2].prev == &extra[1] && extra[2].next == NULL);

	//An empty list (every vanilla image in use) gets a new head.
	void* emptyHead = NULL;
	void* emptyTail = NULL;
	freeListAppend(&emptyHead, &emptyTail, extra, 3, sizeof(Node));
	CHECK(emptyHead == &extra[0] && emptyTail == &extra[2] && extra[0].prev == NULL);
}

void pages() {
	CHECK(pageSizeFor(270, 12) == 12);		//vanilla
	CHECK(pageSizeFor(270, WIREFRAME_MAX) == 12);
	CHECK(pageSizeFor(910, WIREFRAME_MAX) == 46);	//23 columns
	CHECK(pageSizeFor(1678, WIREFRAME_MAX) == 90);	//2048 wide
	CHECK(pageSizeFor(4000, WIREFRAME_MAX) == WIREFRAME_MAX);
	CHECK(pageSizeFor(910, 12) == 12);		//statdata.bin not repacked
	CHECK(pageSizeFor(60, 12) == 2);		//never below one column
	CHECK(pageCountFor(0, 12) == 1 && pageCountFor(12, 12) == 1);
	CHECK(pageCountFor(13, 12) == 2 && pageCountFor(400, 46) == 9);
	CHECK(clampPage(5, 13, 12) == 1 && clampPage(1, 13, 12) == 1);
	CHECK(clampPage(3, 0, 12) == 0);
}

void pageKeys() {
	const u16 up = 0x21, down = 0x22, other = 0x41;
	CHECK(pageAfterKey(down, true, false, 0, 3) == 1);
	CHECK(pageAfterKey(up, true, false, 2, 3) == 1);
	CHECK(pageAfterKey(down, true, false, 2, 3) == 2);	//stops at the last page
	CHECK(pageAfterKey(up, true, false, 0, 3) == 0);	//and at the first
	CHECK(pageAfterKey(down, false, false, 0, 3) == 0);	//plain PgDn scrolls the map
	CHECK(pageAfterKey(down, true, true, 0, 3) == 0);	//chat is open
	CHECK(pageAfterKey(other, true, false, 0, 3) == 0);
}

void ringSearch() {
	static u16 ring[8][SEL_MAX];
	memset(ring, 0, sizeof(ring));
	const u16 stamps[8] = { 5, 9, 9, 1, 0, 0, 0, 0 };
	ring[0][0] = 7;
	ring[1][3] = 7;		//no tag before it: slot 3 is past the group's end
	ring[2][0] = 7;
	for (u32 i = 0; i < SEL_MAX; i++)
		ring[3][i] = (u16)(1000 + i);	//a full group
	CHECK(newestRingGroupWith(ring, stamps, 7) == 2);	//stamps 9 and 9: the higher group
	CHECK(newestRingGroupWith(ring, stamps, (u16)(1000 + SEL_MAX - 1)) == 3);	//its last slot
	CHECK(newestRingGroupWith(ring, stamps, 0x7FFF) == -1);
}

void subgroupKeys() {
	CHECK(subgroupType(30) == 5 && subgroupType(5) == 5);
	CHECK(subgroupType(25) == 23 && subgroupType(23) == 23);
	CHECK(subgroupType(37) == 37);
	CHECK(defaultPriority(true, 0) > defaultPriority(false, 0xFFFF));	//heroes first
	CHECK(defaultPriority(false, 1200) > defaultPriority(false, 50));
	const u16 bc = defaultPriority(false, 1200), marine = defaultPriority(false, 50);
	CHECK(subgroupKey(bc, 12, false) < subgroupKey(marine, 0, false));	//priority first
	CHECK(subgroupKey(marine, 0, false) < subgroupKey(marine, 32, false));	//then lower type
	CHECK(subgroupKey(marine, 0, false) < subgroupKey(marine, 0, true));	//real before fake
	CHECK(subgroupKey(marine, 0, true) < subgroupKey(marine, 1, false));	//fakes right after
}

void subgroupSort() {
	CUnit* units[5] = { fake(1), fake(2), fake(3), fake(4), fake(5) };
	u32 keys[5] = { 7, 3, 7, 3, 1 };
	sortBySubgroup(units, keys, 5);
	CHECK(keys[0] == 1 && keys[1] == 3 && keys[2] == 3 && keys[3] == 7 && keys[4] == 7);
	CHECK(units[0] == fake(5) && units[1] == fake(2) && units[2] == fake(4));	//stable
	CHECK(units[3] == fake(1) && units[4] == fake(3));
}

void subgroupActive() {
	CUnit* before[3] = { fake(1), fake(2), fake(3) };
	CUnit* added[4] = { fake(3), fake(1), fake(9), fake(2) };
	CUnit* removed[2] = { fake(3), fake(1) };
	CUnit* fresh[2] = { fake(1), fake(9) };
	CHECK(keepsActive(false, before, 3, added, 4));
	CHECK(keepsActive(false, before, 3, removed, 2));
	CHECK(keepsActive(false, before, 3, before, 3));
	CHECK(!keepsActive(false, before, 3, fresh, 2));
	//A fresh box over the whole army (or a recall) holds the old units but
	//still starts at the top subgroup.
	CHECK(!keepsActive(true, before, 3, added, 4));
	CHECK(!keepsActive(true, before, 3, removed, 2));
	const u32 keys[5] = { 2, 2, 5, 9, 9 };
	CHECK(activeKeyAfter(keys, 5, 5, true) == 5);
	CHECK(activeKeyAfter(keys, 5, 5, false) == 2);
	//Gone (killed, shift-removed): the top subgroup, as in SC2.
	CHECK(activeKeyAfter(keys, 5, 4, true) == 2);
	CHECK(activeKeyAfter(keys, 5, 10, true) == 2);
	CHECK(keyAfterTab(keys, 5, 2, false) == 5);
	CHECK(keyAfterTab(keys, 5, 9, false) == 2);	//wraps
	CHECK(keyAfterTab(keys, 5, 2, true) == 9);
	CHECK(keyAfterTab(keys, 5, 5, true) == 2);
	const u32 one[2] = { 4, 4 };
	CHECK(keyAfterTab(one, 2, 4, false) == 4 && keyAfterTab(one, 2, 4, true) == 4);
}

void buttonStates() {
	CHECK(betterButtonState(0, -1) == -1 && betterButtonState(-1, 0) == -1);
	CHECK(betterButtonState(-1, 1) == 1 && betterButtonState(1, 0) == 1);
	CHECK(betterButtonState(0, 0) == 0);
}

void dimColours() {
	static u8 pal[256 * 4];
	static bool none[256];
	memset(pal, 0, sizeof(pal));
	memset(none, 0, sizeof(none));
	//1: bright green, 2: dark green (40%), 3: mid green, 4: dark red
	pal[4 * 1 + 1] = 250;
	pal[4 * 2 + 1] = 100;
	pal[4 * 3 + 1] = 170;
	pal[4 * 4 + 0] = 100;
	CHECK(dimIndex(pal, none, 1, 40) == 2);
	CHECK(dimIndex(pal, none, 3, 40) == 2);	//68 green: 100 (distance 32) beats black (68)
	CHECK(dimIndex(pal, none, 0, 40) == 0);	//black stays black
	pal[4 * 6 + 1] = 100;	//the same dark green at a higher index: the lower wins
	CHECK(dimIndex(pal, none, 1, 40) == 2);
	//A cycling entry is never picked: with 2 skipped, the copy at 6 is.
	static bool skip[256];
	memset(skip, 0, sizeof(skip));
	skip[2] = true;
	CHECK(dimIndex(pal, skip, 1, 40) == 6);
}

void paletteCycling() {
	u8 records[3 * 16];
	memset(records, 0, sizeof(records));
	records[0 * 16 + 1] = 8;	//active, entries 1-6
	records[0 * 16 + 3] = 1;
	records[0 * 16 + 5] = 6;
	records[1 * 16 + 3] = 20;	//inactive (byte 1 clear): not marked
	records[1 * 16 + 5] = 30;
	records[2 * 16 + 1] = 8;	//active, entries 7-13
	records[2 * 16 + 3] = 7;
	records[2 * 16 + 5] = 13;
	static bool cycling[256];
	memset(cycling, 0, sizeof(cycling));
	cyclingEntries(records, 3, cycling);
	CHECK(!cycling[0] && cycling[1] && cycling[6] && cycling[7] && cycling[13]);
	CHECK(!cycling[14] && !cycling[20] && !cycling[30]);

	static u8 a[256 * 4], b[256 * 4];
	memset(a, 0, sizeof(a));
	memset(b, 0, sizeof(b));
	b[4 * 3] = 99;		//a cycling entry moved: still the same palette
	CHECK(samePalette(a, b, cycling));
	b[4 * 200 + 2] = 1;	//a fade step: changed
	CHECK(!samePalette(a, b, cycling));
}

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

void buildReview() {
	const s32 xs[3] = { 10, 50, 90 }, ys[3] = { 0, 0, 0 };
	const bool able[3] = { true, true, true };
	const bool notConstructing[3] = { false, true, true };
	CHECK(nearestPreferring(xs, ys, able, notConstructing, 3, 0, 0) == 1);	//0 nearer but constructing
	const bool allConstructing[3] = { false, false, false };
	CHECK(nearestPreferring(xs, ys, able, allConstructing, 3, 0, 0) == 0);	//none free: nearest
	const bool none[3] = { false, false, false };
	CHECK(nearestPreferring(xs, ys, none, notConstructing, 3, 0, 0) == -1);
	CHECK(holdsBuild(0x19) && holdsBuild(0x1E) && holdsBuild(0x1F) && holdsBuild(0x21));
	//A Drone's DroneStartBuild becomes DroneLand (0x46) then DroneBuild (0x1A)
	//at once: a walking Drone holds its building through those.
	CHECK(holdsBuild(0x1A) && holdsBuild(0x46));
	CHECK(!holdsBuild(0x06) && !holdsBuild(0x55));
	CHECK(showCantReach(true, true) && showCantReach(false, false) && showCantReach(true, false));
	CHECK(!showCantReach(false, true));	//money failure: its own error
}

void prepaid() {
	u32 paidM = 250, paidG = 100, refundM, refundG;
	reconcilePaid(&paidM, &paidG, 150, 100, &refundM, &refundG);	//a depot dropped
	CHECK(refundM == 100 && refundG == 0 && paidM == 150 && paidG == 100);
	reconcilePaid(&paidM, &paidG, 150, 100, &refundM, &refundG);	//nothing changed
	CHECK(refundM == 0 && refundG == 0 && paidM == 150 && paidG == 100);
	reconcilePaid(&paidM, &paidG, 0, 0, &refundM, &refundG);		//queue cleared
	CHECK(refundM == 150 && refundG == 100 && paidM == 0 && paidG == 0);
	paidM = 50;
	reconcilePaid(&paidM, &paidG, 80, 0, &refundM, &refundG);		//never more than paid
	CHECK(refundM == 0 && paidM == 50);
	CHECK(canAfford(150, 0, 150, 0) && canAfford(400, 100, 150, 100));
	CHECK(!canAfford(149, 0, 150, 0) && !canAfford(400, 99, 150, 100));
	CHECK(canAfford(0, 0, 0, 0));	//cost 0 is always affordable
}

void shiftPlacing() {
	CHECK(shiftQueues(true, 0x19) && shiftQueues(true, 0x1E) && shiftQueues(true, 0x1F));
	CHECK(!shiftQueues(false, 0x1E));	//plain placement
	CHECK(!shiftQueues(true, 0x24) && !shiftQueues(true, 0x47) && !shiftQueues(true, 0x2E));	//addon, landing, Nydus exit
	CHECK(placingHolds(true, false, 1));	//the only SCV started building: Shift keeps it
	CHECK(placingHolds(false, true, 3));
	CHECK(!placingHolds(false, false, 3));	//no Shift, nobody can: vanilla cancels
	CHECK(!placingHolds(true, true, 0));	//nothing selected any more
}

void minimapKeys() {
	const u16 T = 0x54, TAB = 0x09;
	CHECK(minimapToggleFor(T, false, false, true) == MINIMAP_TERRAIN);		//Alt+T
	CHECK(minimapToggleFor(T, true, true, false) == MINIMAP_ALLY_COLOURS);	//Ctrl+Shift+T
	CHECK(minimapToggleFor(TAB, false, false, false) == MINIMAP_NONE);	//Tab: subgroups only
	CHECK(minimapToggleFor(TAB, true, false, false) == MINIMAP_NONE);	//Shift+Tab too
	CHECK(minimapToggleFor(T, false, false, false) == MINIMAP_NONE);	//T: the card's
	CHECK(minimapToggleFor(T, true, false, true) == MINIMAP_NONE);		//Alt+Shift+T
	CHECK(minimapToggleFor(T, false, true, false) == MINIMAP_NONE);		//Ctrl+T
	CHECK(minimapToggleFor(T, true, true, true) == MINIMAP_NONE);		//Ctrl+Alt+Shift+T
	CHECK(minimapToggleFor(0x41, false, false, true) == MINIMAP_NONE);	//Alt+A
}

} //unnamed namespace

u32 selfTest(u32* firstFailedLine) {
	failures = 0;
	firstLine = 0;
	lists();
	chunkRoundTrip();
	chunkFeedRejects();
	chunkMissingMiddle();
	lengths();
	ring();
	freeLists();
	pages();
	pageKeys();
	ringSearch();
	subgroupKeys();
	subgroupSort();
	subgroupActive();
	buttonStates();
	dimColours();
	paletteCycling();
	minimapKeys();
	buildPicks();
	buildReview();
	prepaid();
	shiftPlacing();
	*firstFailedLine = firstLine;
	return failures;
}

} //selext
