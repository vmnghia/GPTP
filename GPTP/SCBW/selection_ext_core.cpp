//The pure logic of the extended selection: it touches no game memory, so the
//host test in tests/ builds it on its own.
#include "selection_ext.h"
#include <cstring>

namespace selext {

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

u32 chunkCountFor(u32 count) {
	return count == 0 ? 1 : (count + CHUNK_MAX_UNITS - 1) / CHUNK_MAX_UNITS;
}

u32 chunkBuild(u8* out, ChunkMode mode, const u16* tags, u32 count, u32 index) {
	const u32 chunks = chunkCountFor(count);
	const u32 start = index * CHUNK_MAX_UNITS;
	u32 n = count > start ? count - start : 0;
	if (n > CHUNK_MAX_UNITS)
		n = CHUNK_MAX_UNITS;
	u8 flags = (u8)(mode | ((index & 0x0F) << CHUNK_INDEX_SHIFT));
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
	const u8 index = flags >> CHUNK_INDEX_SHIFT;
	if (n > CHUNK_MAX_UNITS || mode > CHUNK_REMOVE) {
		p.open = false;
		return false;
	}
	if (flags & CHUNK_FIRST) {
		p.open = true;
		p.mode = mode;
		p.next = 0;
		p.count = 0;
	}
	else
	if (!p.open || p.mode != mode) {
		p.open = false;
		return false;
	}
	if (index != p.next) {
		p.open = false;
		return false;
	}
	p.next = (p.next + 1) & 0x0F;
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

u32 variableCommandLength(const u8* cmd) {
	const u8 id = cmd[0];
	if (id == CMD_SELECT_CHUNK)
		return 3 + 2 * cmd[2];
	if (id >= 0x09 && id <= 0x0B)
		return 2 + 2 * cmd[1];
	return 0;
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

u32 pageSizeFor(u32 dialogWidth, u32 wireframeControls) {
	const u32 columns = dialogWidth > 90 ? (dialogWidth - 90) / 36 + 1 : 1;
	const u32 size = 2 * columns;
	return size < wireframeControls ? size : wireframeControls;
}

u32 pageCountFor(u32 count, u32 pageSize) {
	if (count == 0 || pageSize == 0)
		return 1;
	return (count + pageSize - 1) / pageSize;
}

u32 clampPage(u32 page, u32 count, u32 pageSize) {
	const u32 last = pageCountFor(count, pageSize) - 1;
	return page < last ? page : last;
}

u32 pageAfterKey(u16 key, bool ctrlHeld, bool chatOpen, u32 page, u32 pages) {
	if (!ctrlHeld || chatOpen)
		return page;
	if (key == 0x21 && page > 0)
		return page - 1;
	if (key == 0x22 && page + 1 < pages)
		return page + 1;
	return page;
}

int newestRingGroupWith(const u16 (*ring)[SEL_MAX], const u16* stamps, u16 tag) {
	int best = -1;
	for (int k = 7; k >= 0; k--) {
		bool found = false;
		for (u32 i = 0; i < SEL_MAX && ring[k][i] != 0 && !found; i++)
			found = ring[k][i] == tag;
		if (found && (best < 0 || stamps[k] > stamps[best]))
			best = k;
	}
	return best;
}

void freeListAppend(void** head, void** tail, void* nodes, u32 count, u32 stride) {
	struct Link {
		Link* prev;
		Link* next;
	};
	for (u32 i = 0; i < count; i++) {
		Link* node = (Link*)((u8*)nodes + i * stride);
		Link* last = (Link*)*tail;
		node->prev = last;
		node->next = NULL;
		if (last != NULL)
			last->next = node;
		else
			*head = node;
		*tail = node;
	}
}

} //selext
