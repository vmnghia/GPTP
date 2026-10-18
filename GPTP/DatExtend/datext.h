#pragma once
#include <SCBW/api.h>

//The .dat extender: images.dat, sprites.dat and flingy.dat with more entries
//than vanilla's 999 / 517 / 209. Ported from saintofidiocy/GPTP@DatExtender
//(0a4516b, 2022-08-28), cut to those three tables (the Mecha Viking's
//images, 2026-10-18; plan in sc2grp docs/2026-10-18-dat-extension-plan.md).
//
//An extended file is vanilla's layout with more entries (PyMS's "expanded"
//.dat, AbstractDAT.expand_entries); a vanilla-size file loads exactly as
//before. The other .dat files must stay vanilla-size: an extended one is
//refused with the game's invalid-file warning rather than half-patched.

//Marks the byte after the end of an array, in the reference lists.
#define ARRAY_END          (s32)0x80000000

//Makes a scbwdata.cpp variable (one the extender re-points) visible here.
#define DATEXT_VAR_REF(type, name)  extern type name;

namespace DatExt {

	//The engine's .dat load tables (what LoadGameData, 0x4D2E80, is passed).
	namespace LoadTable {
		enum {
			Units_Dat    = 0x00513C30,
			Weapons_Dat  = 0x00513868,
			Flingy_Dat   = 0x00515A38,
			Sprites_Dat  = 0x00513FB8,
			Images_Dat   = 0x00514010,
			Upgrades_Dat = 0x005136E0,
			Techdata_Dat = 0x005137D8,
			Orders_Dat   = 0x00513EC8,
			Sfxdata_Dat  = 0x00515498,
			Portdata_Dat = 0x00513780,
			Mapdata_Dat  = 0x00512BA0
		};
	};

	//How many entries each table has now (vanilla's until an extended file loads).
	extern u32 imageCount, spriteCount, flingyCount;

	//Writes a `size`-byte value of `val*factor+add` to each address of the
	//zero-terminated list in `ptr` (constReplace's lists).
	typedef struct {
		s32 factor;
		s32 add;
		u32 size; // 1, 2, 4
		u32 ptr[1]; // to make it an array -- zero-terminated list
	} constList;

	//Called by the load hook with the file's size: false for a size this
	//extender can't load (the caller then warns, as the game does).
	bool setDatSize(DatLoad* datTablePtr, u32 size);

	//General patching functions.
	void datTablePatch(DatLoad* datTablePtr, s32* list);
	void constReplace(s32* list, u32 val);
	u32 simpleReplace(u32* list, u32 val);

	//One per table, in its own .cpp.
	void flingy_dat_doPatch(u32 newCount);
	void sprites_dat_doPatch(u32 newCount);
	void images_dat_doPatch(u32 newCount);

}
