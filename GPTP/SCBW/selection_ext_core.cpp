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
	if (id == CMD_QUEUED_BUILD)
		return 8;
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

u16 subgroupType(u16 unitId) {
	if (unitId == 30)	//Siege Tank, siege mode
		return 5;
	if (unitId == 25)	//Edmund Duke, siege mode
		return 23;
	return unitId;
}

u16 defaultPriority(bool hero, u16 buildScore) {
	const u16 score = buildScore < 0x7FFF ? buildScore : 0x7FFF;
	return hero ? (u16)(0x8000 | score) : score;
}

u32 subgroupKey(u16 priority, u16 type, bool hallucination) {
	return ((u32)(0xFFFF - priority) << 10) | ((u32)(type & 0x1FF) << 1) | (hallucination ? 1 : 0);
}

void sortBySubgroup(CUnit** units, u32* keys, u32 n) {
	for (u32 i = 1; i < n; i++) {
		CUnit* const unit = units[i];
		const u32 key = keys[i];
		u32 j = i;
		for (; j > 0 && keys[j - 1] > key; j--) {
			units[j] = units[j - 1];
			keys[j] = keys[j - 1];
		}
		units[j] = unit;
		keys[j] = key;
	}
}

namespace {

bool holdsAll(CUnit* const* big, u32 nb, CUnit* const* small, u32 ns) {
	for (u32 i = 0; i < ns; i++) {
		bool found = false;
		for (u32 j = 0; j < nb && !found; j++)
			found = big[j] == small[i];
		if (!found)
			return false;
	}
	return true;
}

} //unnamed namespace

bool keepsActive(bool fresh, CUnit* const* before, u32 nb, CUnit* const* after, u32 na) {
	if (fresh)
		return false;
	return holdsAll(after, na, before, nb) || holdsAll(before, nb, after, na);
}

u32 activeKeyAfter(const u32* keys, u32 n, u32 oldKey, bool keep) {
	if (keep)
		for (u32 i = 0; i < n; i++)
			if (keys[i] == oldKey)
				return oldKey;
	return keys[0];
}

u32 keyAfterTab(const u32* keys, u32 n, u32 active, bool back) {
	if (!back) {
		for (u32 i = 0; i < n; i++)
			if (keys[i] > active)
				return keys[i];
		return keys[0];
	}
	for (u32 i = n; i > 0; i--)
		if (keys[i - 1] < active)
			return keys[i - 1];
	return keys[n - 1];
}

s32 betterButtonState(s32 a, s32 b) {
	//Enabled 1 > Disabled -1 > Invisible 0.
	const s32 rankA = a == 1 ? 2 : a == -1 ? 1 : 0;
	const s32 rankB = b == 1 ? 2 : b == -1 ? 1 : 0;
	return rankB > rankA ? b : a;
}

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
	if (n > SEL_MAX)
		n = SEL_MAX;
	u32 fewest = 0xFFFFFFFF;
	for (u32 i = 0; i < n; i++)
		if (able[i] && builds[i] < fewest)
			fewest = builds[i];
	static bool candidate[SEL_MAX];
	for (u32 i = 0; i < n; i++)
		candidate[i] = able[i] && builds[i] == fewest;
	return nearestIndex(fromX, fromY, candidate, n, x, y);
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

int nearestPreferring(const s32* xs, const s32* ys, const bool* able, const bool* preferred,
                      u32 n, s32 x, s32 y) {
	if (n > SEL_MAX)
		n = SEL_MAX;
	static bool both[SEL_MAX];
	for (u32 i = 0; i < n; i++)
		both[i] = able[i] && preferred[i];
	const int k = nearestIndex(xs, ys, both, n, x, y);
	return k >= 0 ? k : nearestIndex(xs, ys, able, n, x, y);
}

bool holdsBuild(u32 order) {
	//DroneStartBuild, DroneBuild, BuildTerran, BuildProtoss1,
	//ConstructingBuilding, DroneLand
	return order == 0x19 || order == 0x1A || order == 0x1E || order == 0x1F
		|| order == 0x21 || order == 0x46;
}

bool showCantReach(bool stuck, bool withinReach) {
	return stuck || !withinReach;
}

void reconcilePaid(u32* paidM, u32* paidG, u32 heldM, u32 heldG, u32* refundM, u32* refundG) {
	*refundM = heldM < *paidM ? *paidM - heldM : 0;
	*refundG = heldG < *paidG ? *paidG - heldG : 0;
	*paidM -= *refundM;
	*paidG -= *refundG;
}

bool shiftQueues(bool shiftHeld, u32 order) {
	//DroneStartBuild, BuildTerran, BuildProtoss1
	return shiftHeld && (order == 0x19 || order == 0x1E || order == 0x1F);
}

bool placingHolds(bool shiftQueuing, bool anyMemberCan, u32 members) {
	return members != 0 && (shiftQueuing || anyMemberCan);
}

bool ableToQueue(bool allowedNow, bool holdingBuild, bool canMake) {
	return allowedNow || (holdingBuild && canMake);
}

bool dropsTrailingIdle(u32 lastQueuedOrder, u32 unitIdleOrder) {
	return lastQueuedOrder == unitIdleOrder;
}

bool canAfford(s32 minerals, s32 gas, u32 costM, u32 costG) {
	return minerals >= (s32)costM && gas >= (s32)costG;
}

MinimapToggle minimapToggleFor(u16 key, bool shift, bool ctrl, bool alt) {
	const u16 VK_T = 0x54;
	if (key != VK_T)
		return MINIMAP_NONE;
	if (alt && !ctrl && !shift)
		return MINIMAP_TERRAIN;
	if (ctrl && shift && !alt)
		return MINIMAP_ALLY_COLOURS;
	return MINIMAP_NONE;
}

void cyclingEntries(const u8* records, u32 count, bool* cycling) {
	for (u32 k = 0; k < count; k++) {
		const u8* const record = records + 16 * k;
		if (record[1] == 0)
			continue;
		for (u32 i = record[3]; i <= record[5] && i < 256; i++)
			cycling[i] = true;
	}
}

bool samePalette(const u8* a, const u8* b, const bool* skip) {
	for (u32 i = 0; i < 256; i++)
		if (!skip[i] && (a[4 * i] != b[4 * i] || a[4 * i + 1] != b[4 * i + 1] || a[4 * i + 2] != b[4 * i + 2]))
			return false;
	return true;
}

u8 dimIndex(const u8* palette, const bool* skip, u8 index, u32 percent) {
	const s32 r = palette[4 * index] * (s32)percent / 100;
	const s32 g = palette[4 * index + 1] * (s32)percent / 100;
	const s32 b = palette[4 * index + 2] * (s32)percent / 100;
	u32 best = 0, bestDistance = 0xFFFFFFFF;
	for (u32 i = 0; i < 256; i++) {
		if (skip[i])
			continue;
		const s32 dr = palette[4 * i] - r, dg = palette[4 * i + 1] - g, db = palette[4 * i + 2] - b;
		const u32 distance = (u32)(dr * dr + dg * dg + db * db);
		if (distance < bestDistance) {
			best = i;
			bestDistance = distance;
		}
	}
	return (u8)best;
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
