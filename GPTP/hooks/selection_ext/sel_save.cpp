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
