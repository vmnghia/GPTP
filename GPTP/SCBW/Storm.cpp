#include "Storm.h"
#include <Windows.h>

namespace {

typedef u32 (__stdcall *SMemFree_type)(void* ptr, const char* filename, u32 line, int idk);	//ordinal 403
SMemFree_type SMemFree_ptr = NULL;

//The name FastFileRead passes Storm for its allocation bookkeeping. A static
//string: the branch kept a stack array's address in a static, which dangled.
const char* const STORM_DEBUG_FILE = "Storm.cpp";

} //unnamed namespace

u32 SMemFree(void* ptr, const char* filename, u32 line, int idk) {
	if (SMemFree_ptr == NULL)
		SMemFree_ptr = (SMemFree_type)GetProcAddress(GetModuleHandle("storm.dll"), MAKEINTRESOURCE(403));
	return SMemFree_ptr(ptr, filename, line, idk);
}

const u32 Func_FastFileRead = 0x004D2D10;
void* FastFileRead(const char* filename, void* buffer, bool returnIfNotFound, u32 scope, u32* filesize) {
	static u32 returnIfNotFound_int;
	static void* result;
	static const char* debugFile;
	returnIfNotFound_int = returnIfNotFound;
	debugFile = STORM_DEBUG_FILE;

	__asm {
		PUSHAD
		PUSH __LINE__
		PUSH debugFile
		PUSH returnIfNotFound_int
		PUSH buffer
		PUSH filename
		MOV ECX, scope
		MOV EAX, filesize
		CALL Func_FastFileRead
		MOV result, EAX
		POPAD
	}
	return result;
}
