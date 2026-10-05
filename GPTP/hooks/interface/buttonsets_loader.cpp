#include "buttonsets_loader.h"
#include <SCBW/buttonsets_file.h>
#include <SCBW/api.h>
#include <SCBW/scbwdata.h>
#include <windows.h>
#include <cstdio>
#include <vector>

namespace {

//storm.dll's file API, by ordinal as BWAPI's storm.h has it [VERIFY].
typedef BOOL (__stdcall* SFileOpenFileExFn)(HANDLE mpq, const char* name, DWORD scope, HANDLE* file);
typedef DWORD (__stdcall* SFileGetFileSizeFn)(HANDLE file, DWORD* high);
typedef BOOL (__stdcall* SFileReadFileFn)(HANDLE file, void* buffer, DWORD toRead, DWORD* read, LPOVERLAPPED overlapped);
typedef BOOL (__stdcall* SFileCloseFileFn)(HANDLE file);
const WORD ORDINAL_CLOSE_FILE = 253;
const WORD ORDINAL_GET_FILE_SIZE = 265;
const WORD ORDINAL_OPEN_FILE_EX = 268;
const WORD ORDINAL_READ_FILE = 269;

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
	const HMODULE storm = GetModuleHandleA("storm.dll");
	const SFileOpenFileExFn openFileEx = storm == NULL ? NULL :
		(SFileOpenFileExFn)GetProcAddress(storm, MAKEINTRESOURCEA(ORDINAL_OPEN_FILE_EX));
	const SFileGetFileSizeFn getFileSize = storm == NULL ? NULL :
		(SFileGetFileSizeFn)GetProcAddress(storm, MAKEINTRESOURCEA(ORDINAL_GET_FILE_SIZE));
	const SFileReadFileFn readBytes = storm == NULL ? NULL :
		(SFileReadFileFn)GetProcAddress(storm, MAKEINTRESOURCEA(ORDINAL_READ_FILE));
	const SFileCloseFileFn closeFile = storm == NULL ? NULL :
		(SFileCloseFileFn)GetProcAddress(storm, MAKEINTRESOURCEA(ORDINAL_CLOSE_FILE));
	if (openFileEx == NULL || getFileSize == NULL || readBytes == NULL || closeFile == NULL) {
		report("storm.dll's file functions were not found");
		return false;
	}
	HANDLE file = NULL;
	if (!openFileEx(NULL, FILE_NAME, 0, &file))
		return false;
	const DWORD size = getFileSize(file, NULL);
	bool ok = size != 0xFFFFFFFF;
	if (ok) {
		out.resize(size);
		DWORD read = 0;
		ok = size == 0 || (readBytes(file, out.data(), size, &read, NULL) && read == size);
	}
	closeFile(file);
	if (!ok)
		report("could not be read");
	return ok;
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
