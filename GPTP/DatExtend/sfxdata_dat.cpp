//Ported from saintofidiocy/GPTP@DatExtender (0a4516b): the engine's
//references to sfxdata.dat's arrays, which setDatSize re-points (datext.h),
//and the sound count the engine compares against (sfx_consts), unchanged;
//plus what that list leaves out (below the line: the loaded-sound table and
//four more counts, 2026-10-28). For SC2 sounds of the Mecha Viking's torpedo
//(sc2grp, 2026-10-27).
#include <SCBW/api.h>
#include <hook_tools.h>
#include <cstdlib>
#include <cstring>
#include "datext.h"


namespace {

  // List of address reference replacements.
s32 sfxdata_r[] = {/* sound */ 0,   0x004BC5E9 +3, 0x004BC68D +3, 0x004BCA04 +1, 0x004BCA39 +3, 0x004DB0E5 +1,  0,
                               60,  0x004BCB29 +1,  0,
                               84,  0x0046116C +1,  0,
                               88,  0x0046102C +1,  0,
                               0,
                   /* unk1 */  0,   0x004BBEC1 +2, 0x004BC0D1 +2, 0x004BC811 +2,  0, 0,
                   /* unk2 */  0,   0x004BB970 +2, 0x004BBC52 +2, 0x004BBC65 +2, 0x004BBF15 +2, 0x004BBF1E +2, 0x004BBFF1 +2, 0x004BC06A +2, 0x004BC0BE +2, 0x004BC7AC +2, 0x004BC808 +2, 0x004BC844 +2, 0x004BCAD4 +2, 0x004BCAF1 +2, 0x004BCB09 +2, 0x004BCD42 +2,  0, 0,
                   /* unk3 */  0,   0x0048F123 +4, 0x0048F200 +4, 0x0048F2E7 +4, 0x0048F3CE +4, 0x0048F49C +4, 0x0048F56E +4, 0x0048F603 +4, 0x0048F693 +4, 0x0048F72E +4, 0x0048F81E +4, 0x0048FAE7 +4,  0,
                               260, 0x0048F8E0 +3,  0,
                               270, 0x0048F373 +3, 0x0048F418 +3,  0,
                               0,
                   /* unk4 */  0,   0x00455BD9 +3, 0x0048ECEA +3, 0x0048EDD4 +3, 0x0048EE59 +3, 0x0048EFA2 +3, 0x0048F025 +3, 0x0048F0C7 +3, 0x0048F1A4 +3, 0x004E3EB2 +3, 0x004E3F52 +3,  0,
                               2,   0x0048D169 +3,  0,
                               23,  0x0048D0ED +3, 0x0048D406 +3,  0,
                               0,0};


// Sfxdata.dat tables
            // factor, add, size,       ptr, ptr, ptr, ...,  0,  ...
s32 sfx_consts[] = {1,  0, sizeof(int), 0x004BBEB5 +2, 0x004BCA96 +2, 0x004BCD28 +2,  0,
                    1, -1, sizeof(int), 0x004BBC2A +3,  0,
                    0,0,0,0};


// --- Beyond upstream (sc2grp, 2026-10-28): what its list leaves out. Found when
// the Viking's sound 1144 crashed at 0x4BBB2B: `ecx = id*16 + 0x6D1270` ran past
// the end of the loaded-sound table into the globals after it. Every reference
// below comes from sc2grp `tools/sc_disasm.py refs 6D1270 6D59EF` and `imm 1144
// 1143` on StarCraft.exe 1.16.1; upstream's own five arrays were checked the same
// way (all 53 of its operands found, none missing).

// The engine's table of loaded sounds: 16 bytes a sound (+0xC the DirectSound
// buffer, +8 its size), 1144 of them, not a .dat array. Each entry: the address
// of the 4-byte operand, and the value it holds (the table, a field in it, or a
// fixed sound's field: 0x6D1A98 is sound 130's +8, 0x6D1AE8 sound 135's).
// 0x6D59F0, just past the end, is a variable of its own: not moved.
const u32 SOUND_TABLE = 0x006D1270, SOUND_ENTRY = 16, SOUND_VANILLA = 1144;
const u32 soundTableRefs[][2] = {
	{ 0x0045EF64 +2, 0x006D1278 },  // mul    dword ptr [edx + 0x6d1278]
	{ 0x0048EA08 +2, 0x006D1278 },  // mul    dword ptr [ecx + 0x6d1278]
	{ 0x0048F11B +2, 0x006D1278 },  // mul    dword ptr [ecx + 0x6d1278]
	{ 0x0048F1F8 +2, 0x006D1278 },  // mul    dword ptr [ecx + 0x6d1278]
	{ 0x0048F2DC +2, 0x006D1278 },  // mul    dword ptr [ecx + 0x6d1278]
	{ 0x0048F36D +2, 0x006D1AE8 },  // mul    dword ptr [0x6d1ae8]
	{ 0x0048F3C8 +2, 0x006D1278 },  // mul    dword ptr [edx + 0x6d1278]
	{ 0x0048F410 +2, 0x006D1AE8 },  // mul    dword ptr [0x6d1ae8]
	{ 0x0048F496 +2, 0x006D1278 },  // mul    dword ptr [edx + 0x6d1278]
	{ 0x0048F568 +2, 0x006D1278 },  // mul    dword ptr [edx + 0x6d1278]
	{ 0x0048F5FD +2, 0x006D1278 },  // mul    dword ptr [edx + 0x6d1278]
	{ 0x0048F68D +2, 0x006D1278 },  // mul    dword ptr [edx + 0x6d1278]
	{ 0x0048F728 +2, 0x006D1278 },  // mul    dword ptr [edx + 0x6d1278]
	{ 0x0048F818 +2, 0x006D1278 },  // mul    dword ptr [edx + 0x6d1278]
	{ 0x0048F8D8 +2, 0x006D1A98 },  // mul    dword ptr [0x6d1a98]
	{ 0x0048FAE1 +2, 0x006D1278 },  // mul    dword ptr [edx + 0x6d1278]
	{ 0x004BB723 +2, 0x006D1278 },  // mov    eax, dword ptr [eax + 0x6d1278]
	{ 0x004BB8E6 +2, 0x006D1270 },  // add    esi, 0x6d1270
	{ 0x004BBC45 +2, 0x006D1270 },  // add    esi, 0x6d1270
	{ 0x004BC075 +2, 0x006D1270 },  // add    ecx, 0x6d1270
	{ 0x004BC672 +2, 0x006D1270 },  // add    ebx, 0x6d1270
	{ 0x004BCAA4 +2, 0x006D1270 },  // add    esi, 0x6d1270
};

// The sound count where upstream's sfx_consts doesn't reach: a bounds check
// (0x45F041), the walk over the sound-file array after sfxdata.dat loads (both
// load paths: 0x4BCA18, 0x4DB0F9), and the loaded-sound cursor's wrap (0x4BBCA4,
// count - 1).
s32 sfx_consts_more[] = {1,  0, sizeof(int), 0x0045F041 +1, 0x004BCA18 +1, 0x004DB0F9 +1,  0,
                         1, -1, sizeof(int), 0x004BBCA4 +1,  0,
                         0,0,0,0};

void moveSoundTable(u32 newCount) {
	u8* const table = (u8*)calloc(newCount, SOUND_ENTRY);
	if (table == NULL)
		return;
	memcpy(table, (void*)SOUND_TABLE, SOUND_VANILLA * SOUND_ENTRY);
	for (u32 i = 0; i < sizeof(soundTableRefs) / sizeof(soundTableRefs[0]); i++) {
		const u32 value = (u32)table + (soundTableRefs[i][1] - SOUND_TABLE);
		memoryPatch(soundTableRefs[i][0], value);
	}
}
};


namespace DatExt {

  void sfxdata_dat_doPatch(u32 newCount) {
    datTablePatch((DatLoad*)LoadTable::Sfxdata_Dat, sfxdata_r);
    constReplace(sfx_consts, newCount);
    constReplace(sfx_consts_more, newCount);
    moveSoundTable(newCount);
  }


  //void sfxdata_dat_unpatch() {
  //}

};