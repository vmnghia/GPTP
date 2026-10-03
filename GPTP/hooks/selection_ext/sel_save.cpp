#include "sel_save.h"
#include "sel_exe.h"
#include "sel_build.h"
#include <SCBW/selection_ext.h>
#include <cstring>

using namespace selext;

namespace {

const u32 MAGIC = 0x584C4553;	//"SELX"
//1: selections (stages 1-3). 2: selections, then control groups (stage 4).
//3: as 2, then a second compressed block: smart-build's stamps.
const u16 VERSION = 3;

#pragma pack(push, 1)
struct Header {
	u32 magic;
	u16 version;
	u16 limit;			//the SEL_MAX of the build that saved
	u32 payloadBytes;	//u16 tags: playersSel [PLAYERS][limit], then (version 2)
						//groupsExt [PLAYERS][GROUP_COUNT][limit]
};
#pragma pack(pop)

CUnit* const* const VANILLA_PLAYERS_SEL = (CUnit* const*)0x006284E8;	//[8][12]
const u32* const VANILLA_GROUPS = (const u32*)0x0057FE60;				//[8][18][12], in CGame

const u32 LISTS_V1 = PLAYERS;
const u32 LISTS_V2 = PLAYERS * (1 + GROUP_COUNT);

u16 payload[LISTS_V2][SEL_MAX];

//Version 3's second block.
struct BuildStamps {
	u32 stamps[UNIT_ARRAY_LENGTH];
	u32 lastStamp[8];
};
BuildStamps buildStamps;

//An old save: the selections are vanilla's 12.
void selectionsFromVanilla() {
	for (u32 p = 0; p < PLAYERS; p++) {
		listClear(playersSel[p], SEL_MAX);
		u32 n = 0;
		for (u32 i = 0; i < VANILLA_MAX; i++)
			if (VANILLA_PLAYERS_SEL[p * VANILLA_MAX + i] != NULL)
				playersSel[p][n++] = VANILLA_PLAYERS_SEL[p * VANILLA_MAX + i];
	}
}

//A save without groups in its chunk: they are vanilla's 12, which CGame
//already brought back.
void groupsFromVanilla() {
	for (u32 p = 0; p < PLAYERS; p++)
		for (u32 g = 0; g < GROUP_COUNT; g++) {
			u16* const slots = groupsExt[p][g];
			memset(slots, 0, SEL_MAX * sizeof(u16));
			const u32* const vanilla = &VANILLA_GROUPS[(p * GROUP_COUNT + g) * VANILLA_MAX];
			for (u32 i = 0; i < VANILLA_MAX && vanilla[i] != 0; i++)
				slots[i] = (u16)vanilla[i];
		}
}

//Selections are kept as live units (dead tags dropped), as vanilla's load
//(0x4CEDA0) does; groups keep their tags as saved, as vanilla's CGame does,
//and recall drops the dead ones.
void decode(const u16* saved, u32 limit, bool withGroups) {
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
	if (!withGroups) {
		groupsFromVanilla();
		return;
	}
	const u16* const groups = &saved[PLAYERS * limit];
	for (u32 p = 0; p < PLAYERS; p++)
		for (u32 g = 0; g < GROUP_COUNT; g++) {
			u16* const slots = groupsExt[p][g];
			memset(slots, 0, SEL_MAX * sizeof(u16));
			const u16* const list = &groups[(p * GROUP_COUNT + g) * limit];
			for (u32 i = 0; i < limit && i < SEL_MAX && list[i] != 0; i++)
				slots[i] = list[i];
			mirrorGroup(p, g);
		}
}

} //unnamed namespace

namespace selsave {

size_t __cdecl writeLastAndExtension(const void* data, size_t size, size_t count, FILE* file) {
	if (selexe::fwriteExe(data, size, count, file) != count)
		return 0;
	for (u32 p = 0; p < PLAYERS; p++)
		for (u32 i = 0; i < SEL_MAX; i++)
			payload[p][i] = tagOf(playersSel[p][i]);
	for (u32 p = 0; p < PLAYERS; p++)
		for (u32 g = 0; g < GROUP_COUNT; g++)
			memcpy(payload[PLAYERS + p * GROUP_COUNT + g], groupsExt[p][g], SEL_MAX * sizeof(u16));
	const Header header = { MAGIC, VERSION, (u16)SEL_MAX, sizeof(payload) };
	if (selexe::fwriteExe(&header, sizeof(header), 1, file) != 1)
		return 0;
	if (!selexe::writeCompressed(file, payload, sizeof(payload)))
		return 0;
	memcpy(buildStamps.stamps, selbuild::stamps, sizeof(buildStamps.stamps));
	memcpy(buildStamps.lastStamp, selbuild::lastStamp, sizeof(buildStamps.lastStamp));
	if (!selexe::writeCompressed(file, &buildStamps, sizeof(buildStamps)))
		return 0;
	return count;
}

size_t __cdecl readLastAndExtension(void* data, size_t size, size_t count, FILE* file) {
	if (selexe::freadExe(data, size, count, file) != count)
		return 0;
	Header header;
	if (selexe::freadExe(&header, sizeof(header), 1, file) != 1
		|| header.magic != MAGIC || header.version < 1 || header.version > 3)
	{
		selectionsFromVanilla();
		groupsFromVanilla();
		return count;
	}
	const u32 limit = header.limit;
	const u32 lists = header.version >= 2 ? LISTS_V2 : LISTS_V1;
	if (limit == 0 || header.payloadBytes != lists * limit * sizeof(u16))
		return 0;
	u16* saved = new u16[lists * limit];
	bool ok = selexe::readCompressed(file, saved, header.payloadBytes);
	if (ok)
		decode(saved, limit, header.version >= 2);
	delete[] saved;
	//Smart-build stamps: version 3, else none (ties go to the lowest index).
	if (ok && header.version >= 3) {
		ok = selexe::readCompressed(file, &buildStamps, sizeof(buildStamps));
		if (ok) {
			memcpy(selbuild::stamps, buildStamps.stamps, sizeof(buildStamps.stamps));
			memcpy(selbuild::lastStamp, buildStamps.lastStamp, sizeof(buildStamps.lastStamp));
		}
	}
	return ok ? count : 0;
}

} //selsave
