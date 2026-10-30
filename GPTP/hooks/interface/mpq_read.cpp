#include "mpq_read.h"
#include <windows.h>

namespace {

//storm.dll's file API, by ordinal as BWAPI's storm.h has it (checked in game 2026-10-07).
typedef BOOL (__stdcall* SFileOpenFileExFn)(HANDLE mpq, const char* name, DWORD scope, HANDLE* file);
typedef DWORD (__stdcall* SFileGetFileSizeFn)(HANDLE file, DWORD* high);
typedef BOOL (__stdcall* SFileReadFileFn)(HANDLE file, void* buffer, DWORD toRead, DWORD* read, LPOVERLAPPED overlapped);
typedef BOOL (__stdcall* SFileCloseFileFn)(HANDLE file);
const WORD ORDINAL_CLOSE_FILE = 253;
const WORD ORDINAL_GET_FILE_SIZE = 265;
const WORD ORDINAL_OPEN_FILE_EX = 268;
const WORD ORDINAL_READ_FILE = 269;

} //unnamed namespace

namespace mpqread {

Result read(const char* name, std::vector<u8>& out) {
	const HMODULE storm = GetModuleHandleA("storm.dll");
	const SFileOpenFileExFn openFileEx = storm == NULL ? NULL :
		(SFileOpenFileExFn)GetProcAddress(storm, MAKEINTRESOURCEA(ORDINAL_OPEN_FILE_EX));
	const SFileGetFileSizeFn getFileSize = storm == NULL ? NULL :
		(SFileGetFileSizeFn)GetProcAddress(storm, MAKEINTRESOURCEA(ORDINAL_GET_FILE_SIZE));
	const SFileReadFileFn readBytes = storm == NULL ? NULL :
		(SFileReadFileFn)GetProcAddress(storm, MAKEINTRESOURCEA(ORDINAL_READ_FILE));
	const SFileCloseFileFn closeFile = storm == NULL ? NULL :
		(SFileCloseFileFn)GetProcAddress(storm, MAKEINTRESOURCEA(ORDINAL_CLOSE_FILE));
	if (openFileEx == NULL || getFileSize == NULL || readBytes == NULL || closeFile == NULL)
		return NO_STORM;
	HANDLE file = NULL;
	if (!openFileEx(NULL, name, 0, &file))
		return MISSING;
	const DWORD size = getFileSize(file, NULL);
	bool ok = size != 0xFFFFFFFF;
	if (ok) {
		out.resize(size);
		DWORD read = 0;
		ok = size == 0 || (readBytes(file, out.data(), size, &read, NULL) && read == size);
	}
	closeFile(file);
	return ok ? OK : FAILED;
}

} //mpqread
