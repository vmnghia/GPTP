#include "buttonsets_loader.h"
#include "mpq_read.h"
#include <SCBW/buttonsets_file.h>
#include <SCBW/api.h>
#include <SCBW/scbwdata.h>
#include <windows.h>
#include <cstdio>
#include <vector>

namespace {

const char* const FILE_NAME = "Manifold\\buttonsets.bin";

//The applied sets: the table points into this buffer until the next apply.
std::vector<u8> applied;
bool reported;

void report(const char* reason) {
	if (reported)
		return;
	reported = true;
	char line[128];
	sprintf_s(line, sizeof(line), "buttonsets.bin: %s", reason);
	scbw::printText(line, GameTextColor::Yellow);
}

//Fills out with the file. False when it isn't there (no report) or can't be read.
bool readFile(std::vector<u8>& out) {
	const mpqread::Result result = mpqread::read(FILE_NAME, out);
	if (result == mpqread::NO_STORM)
		report("storm.dll's file functions were not found");
	else if (result == mpqread::FAILED)
		report("could not be read");
	return result == mpqread::OK;
}

//StarCraft.exe's first code section, from its PE headers in memory.
void codeRange(u32* start, u32* end) {
	const u8* const base = (const u8*)GetModuleHandleA(NULL);
	const IMAGE_NT_HEADERS32* const nt =
		(const IMAGE_NT_HEADERS32*)(base + ((const IMAGE_DOS_HEADER*)base)->e_lfanew);
	const IMAGE_SECTION_HEADER* section = IMAGE_FIRST_SECTION(nt);
	for (u32 i = 0; i < nt->FileHeader.NumberOfSections; i++, section++)
		if (section->Characteristics & IMAGE_SCN_CNT_CODE) {
			const u32 size = section->Misc.VirtualSize > section->SizeOfRawData
				? section->Misc.VirtualSize : section->SizeOfRawData;
			*start = (u32)base + section->VirtualAddress;
			*end = *start + size;
			return;
		}
	*start = 0;
	*end = 0;
}

} //unnamed namespace

namespace bsloader {

void applyAtGameStart() {
	std::vector<u8> file;
	if (!readFile(file))
		return;
	static bsfile::SetEntry entries[bsfile::SET_COUNT];
	u32 codeStart, codeEnd;
	codeRange(&codeStart, &codeEnd);
	const char* const error = bsfile::parse(file.data(), (u32)file.size(), codeStart, codeEnd, entries);
	if (error != NULL) {
		report(error);
		return;
	}
	//Every entry is repointed before the old buffer goes, at the end of this function.
	applied.swap(file);
	for (u32 s = 0; s < bsfile::SET_COUNT; s++) {
		buttonSetTable[s].buttonsInSet = entries[s].buttonCount;
		buttonSetTable[s].firstButton = (BUTTON*)(applied.data() + entries[s].firstButton);
		buttonSetTable[s].connectedUnit = entries[s].connectedUnit;
	}
}

} //bsloader
