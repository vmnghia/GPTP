#include "datext.h"
#include <hook_tools.h>
#include <cstdlib>

namespace DatExt {

u32 imageCount = 999, spriteCount = 517, flingyCount = 209, sfxCount = 1144;

namespace {

//Every .dat the game loads: its load table, vanilla size and the size of one
//expanded entry (PyMS's expanded_entry_size(), the branch's table), and
//whether this port can extend it.
struct DatFile {
	const char* name;
	u32 datTablePtr;
	u32 defaultSize;
	u32 entrySize;
	bool supported;
	bool loaded;
};

DatFile datFiles[] = {
	{ "units.dat",    LoadTable::Units_Dat,    19876, 97, false, false },
	{ "weapons.dat",  LoadTable::Weapons_Dat,   5460, 42, false, false },
	{ "flingy.dat",   LoadTable::Flingy_Dat,    3135, 15, true,  false },
	{ "sprites.dat",  LoadTable::Sprites_Dat,   3229,  7, true,  false },
	{ "images.dat",   LoadTable::Images_Dat,   37962, 38, true,  false },
	{ "upgrades.dat", LoadTable::Upgrades_Dat,  1281, 21, false, false },
	{ "techdata.dat", LoadTable::Techdata_Dat,   836, 19, false, false },
	{ "orders.dat",   LoadTable::Orders_Dat,    4158, 22, false, false },
	{ "sfxdata.dat",  LoadTable::Sfxdata_Dat,  10296,  9, true,  false },
	{ "portdata.dat", LoadTable::Portdata_Dat,  1320, 12, false, false },
	{ "mapdata.dat",  LoadTable::Mapdata_Dat,    260,  4, false, false },
};
const u32 DAT_FILE_COUNT = sizeof(datFiles) / sizeof(datFiles[0]);

} //unnamed namespace

bool setDatSize(DatLoad* datTablePtr, u32 size) {
	u32 i, j;

	for (i = 0; i < DAT_FILE_COUNT && (u32)datTablePtr != datFiles[i].datTablePtr; i++);

	//Not a .dat this knows: load as the game does.
	if (i == DAT_FILE_COUNT)
		return true;
	//Vanilla size: load as the game does.
	if (size == datFiles[i].defaultSize)
		return true;
	//Extended, but a table this port doesn't move: refuse it.
	if (!datFiles[i].supported)
		return false;
	//Not a whole number of entries: an invalid file.
	if ((size % datFiles[i].entrySize) != 0)
		return false;
	//Already moved (the game loads each .dat once per run).
	if (datFiles[i].loaded)
		return true;

	const u32 newCount = size / datFiles[i].entrySize;

	//New arrays for every property, which the loader then reads the file into.
	for (j = 0; datTablePtr[j].address != 0; j++) {
		void* const newMem = calloc(newCount, datTablePtr[j].length);
		if (newMem == NULL)
			return false;
		datTablePtr[j].entries = newCount;
		datTablePtr[j].address = (u32)newMem;
	}

	//Point the engine's references at them.
	switch (datTablePtr == (DatLoad*)LoadTable::Images_Dat ? 0 :
	        datTablePtr == (DatLoad*)LoadTable::Sprites_Dat ? 1 :
	        datTablePtr == (DatLoad*)LoadTable::Sfxdata_Dat ? 3 : 2) {
	case 0:
		images_dat_doPatch(newCount);
		imageCount = newCount;
		break;
	case 1:
		sprites_dat_doPatch(newCount);
		spriteCount = newCount;
		break;
	case 3:
		sfxdata_dat_doPatch(newCount);
		sfxCount = newCount;
		break;
	default:
		flingy_dat_doPatch(newCount);
		flingyCount = newCount;
		break;
	}

	datFiles[i].loaded = true;
	return true;
}

//Overwrites references to dat file arrays.
//A null-terminated list of null-terminated lists of addresses, one sub-list
//per property; each starts with the offset into the new array to write (or
//ARRAY_END for the byte past its end).
void datTablePatch(DatLoad* datTablePtr, s32* list) {
	u32 i, j;
	u32 offs;

	for (i = 0, j = 0; datTablePtr[i].entries != 0; i++, j++) {
		do {
			if (list[j] == ARRAY_END)
				offs = datTablePtr[i].address + datTablePtr[i].entries * datTablePtr[i].length;
			else
				offs = datTablePtr[i].address + list[j];

			for (j++; list[j] != 0; j++)
				memoryPatch(list[j], offs);
			j++;
		} while (list[j] != 0);
	}
}

//Writes `val * factor + add` to lists of addresses (constList structs, one
//after another, the last all zero).
void constReplace(s32* list, u32 val) {
	u32 i;
	s32 newVal;

	for (constList* cl = (constList*)list; cl->ptr[0] != 0; cl = (constList*)&cl->ptr[i + 1]) {
		newVal = val * cl->factor + cl->add;
		for (i = 0; cl->ptr[i] != 0; i++)
			memoryPatch(cl->ptr[i], (u8*)&newVal, cl->size);
	}
}

//Writes one value to a zero-terminated list of addresses; returns how many.
u32 simpleReplace(u32* list, u32 val) {
	u32 i;
	for (i = 0; list[i] != 0; i++)
		memoryPatch(list[i], val);
	return i;
}

} //DatExt
