#include "requirements_loader.h"
#include "mpq_read.h"
#include <SCBW/fg_requirements.h>
#include <SCBW/api.h>
#include <hook_tools.h>
#include <cstdio>
#include <string>
#include <vector>

namespace {

const char* const DATALIST = "Firegraft\\datalist.txt";

//Where the exe holds each table's address (or the address + 2, the first
//entry's id), found by scanning StarCraft.exe 1.16.1 for the five addresses:
//all 39 are in code, none in data (2026-10-29). FireGraft's runtime finds the
//same ones by scanning 0x400000-0x500000 (0x10006E52 in its loader DLL).
const u32 UNIT_SITES[] = {
	0x0046E103, 0x0046E138, 0x0046E142, 0x0046E19E, 0x0046E1AA, 0x0046E2D8,
	0x0046E324, 0x0046E386, 0x0046E4DF, 0x0046E51F, 0x0046E564, 0x0046E570,
	0x0046E593, 0x0048E24B, 0x0048E257 };
const u32 UPGRADE_SITES[] = { 0x0046D5A3, 0x0046D5D6, 0x0046D5E0, 0x0046E0C7 };
const u32 RESEARCH_SITES[] = { 0x0046D533, 0x0046D568, 0x0046D572, 0x0046DF96 };
const u32 USE_SITES[] = { 0x0046D4C3, 0x0046D4F8, 0x0046D502, 0x0046DE76 };
const u32 ORDER_SITES[] = {
	0x00429620, 0x0046D453, 0x0046D488, 0x0046D492, 0x0046DD6D, 0x004C11AC,
	0x004C139C, 0x004C158C, 0x004C1798, 0x004C1E13, 0x004C22C5, 0x004CC592 };

//Each table's offsets, one u16 per id, come from its .dat file (0xFFFF: not
//known yet) and are filled from the table on first use (0x46E100 for units,
//0x46D450-0x46D5A0 for the rest). Set back to 0xFFFF, they are filled from
//the new table.
struct Table {
	u32 vanilla;
	const u32* sites;
	u32 siteCount;
	u32 cache;
	u32 cacheCount;
};

const Table TABLES[fgreq::TABLE_COUNT] = {
	{ 0x00514178, UNIT_SITES, _countof(UNIT_SITES), 0x00660A70, 228 },
	{ 0x005145C0, UPGRADE_SITES, _countof(UPGRADE_SITES), 0x006558C0, 61 },
	{ 0x00514908, RESEARCH_SITES, _countof(RESEARCH_SITES), 0x00656198, 44 },
	{ 0x00514A48, USE_SITES, _countof(USE_SITES), 0x006562F8, 44 },
	{ 0x00514CF8, ORDER_SITES, _countof(ORDER_SITES), 0x00665580, 189 },
};

//The applied tables: the exe points into these for the rest of the run.
std::vector<u16> applied[fgreq::TABLE_COUNT];
bool tried;

void report(const char* what, const char* reason) {
	char line[160];
	sprintf_s(line, sizeof(line), "FireGraft requirements: %s%s", what, reason);
	scbw::printText(line, GameTextColor::Yellow);
}

bool readOrReport(const char* name, std::vector<u8>& out) {
	const mpqread::Result result = mpqread::read(name, out);
	if (result == mpqread::OK)
		return true;
	report(name, result == mpqread::MISSING ? " is not in the exe" :
		result == mpqread::FAILED ? " could not be read" : ": storm.dll's file functions were not found");
	return false;
}

} //unnamed namespace

namespace reqloader {

void applyOnce() {
	if (tried)
		return;
	tried = true;

	std::vector<u8> list;
	if (!readOrReport(DATALIST, list))
		return;
	const std::string name = fgreq::fgpName((const char*)list.data(), (u32)list.size());
	if (name.empty()) {
		report(DATALIST, " names no .fgp");
		return;
	}
	std::vector<u8> fgp;
	if (!readOrReport(name.c_str(), fgp))
		return;

	//Everything is built and checked before the exe is touched.
	std::vector<u16> built[fgreq::TABLE_COUNT];
	bool present[fgreq::TABLE_COUNT];
	u32 blanks[fgreq::TABLE_COUNT] = {};
	for (u32 t = 0; t < fgreq::TABLE_COUNT; t++) {
		u32 length = 0;
		const char* error = NULL;
		const u8* const section = fgreq::findSection(fgp.data(), (u32)fgp.size(), fgreq::SECTION_TAGS[t], &length, &error);
		if (error != NULL) {
			report(name.c_str(), ": not a FireGraft project");
			return;
		}
		present[t] = section != NULL;
		if (!present[t])
			continue;
		error = fgreq::buildTable(section, length, built[t], &blanks[t]);
		if (error != NULL) {
			char reason[80];
			sprintf_s(reason, sizeof(reason), "section %s %s", fgreq::SECTION_TAGS[t], error);
			report("", reason);
			return;
		}
		const Table& table = TABLES[t];
		for (u32 s = 0; s < table.siteCount; s++) {
			const u32 held = *(const u32*)table.sites[s];
			if (held != table.vanilla && held != table.vanilla + 2) {
				char reason[80];
				sprintf_s(reason, sizeof(reason), "0x%06X holds 0x%08X, not the table's address", table.sites[s], held);
				report("", reason);
				return;
			}
		}
	}

	u32 count = 0;
	for (u32 t = 0; t < fgreq::TABLE_COUNT; t++) {
		if (!present[t])
			continue;
		applied[t].swap(built[t]);
		const Table& table = TABLES[t];
		const u32 base = (u32)applied[t].data();
		for (u32 s = 0; s < table.siteCount; s++) {
			const u32 held = *(const u32*)table.sites[s];
			memoryPatch(table.sites[s], base + (held - table.vanilla));
		}
		u16* const cache = (u16*)table.cache;
		for (u32 i = 0; i < table.cacheCount; i++)
			cache[i] = 0xFFFF;
		count++;
	}
	char line[160];
	sprintf_s(line, sizeof(line), "FireGraft requirements: %u tables from %s", count, name.c_str());
	scbw::printText(line);
	//Blank items (fgreq::BLANK) were left out; name where, so they can be
	//cleaned up in FireGraft.
	for (u32 t = 0; t < fgreq::TABLE_COUNT; t++)
		if (blanks[t] != 0) {
			char reason[80];
			sprintf_s(reason, sizeof(reason), "%u blank item(s) in %s left out", blanks[t], fgreq::SECTION_TAGS[t]);
			report("", reason);
		}
}

} //reqloader
