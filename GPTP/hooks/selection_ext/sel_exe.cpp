#include "sel_exe.h"

namespace selexe {

namespace {

const u32 Func_CreateSelectionCircle	= 0x004E6180;
const u32 Func_RemoveSelectionCircle	= 0x004E6290;
const u32 Func_IsTeamAlly				= 0x0049A110;
const u32 Func_RemoveDashedCircle		= 0x00497590;
const u32 Func_AddDashedCircle			= 0x004E65C0;
const u32 Func_FreeImage				= 0x004D4FA0;
const u32 Func_InitHealthBarImage		= 0x004D68C0;
const u32 Func_ShowControl				= 0x004186A0;
const u32 Func_HideControl				= 0x00418700;
const u32 Func_InvalidateControl		= 0x0041C400;
const u32 Func_CanMultiSelect			= 0x0047B770;
const u32 Func_Outranks					= 0x0049A350;
const u32 Func_SelectRecentGroupOf		= 0x00496D30;
const u32 Func_CenterViewOnGroup		= 0x004967E0;
const u32 Func_QueueCommand				= 0x00485BD0;
const u32 Func_MoveScreen				= 0x0049C440;
const u32 Func_RefreshLayer3And4		= 0x0048D9A0;
const u32 Func_Sub48E310				= 0x0048E310;
const u32 Func_CancelTargetOrder		= 0x0048CA10;
const u32 Func_WriteCompressed			= 0x004C3450;

typedef u32 (__stdcall *ReadCompressedFn)(void* data, u32 size, FILE* file);
const ReadCompressedFn readCompressedFn = (ReadCompressedFn)0x004C3280;
typedef size_t (__cdecl *FwriteFn)(const void* data, size_t size, size_t count, FILE* file);
const FwriteFn fwriteFn = (FwriteFn)0x00411931;
typedef size_t (__cdecl *FreadFn)(void* data, size_t size, size_t count, FILE* file);
const FreadFn freadFn = (FreadFn)0x004117DE;

} //unnamed namespace

void createSelectionCircle(CUnit* unit, u32 slot) {
	__asm {
		PUSHAD
		PUSH slot
		MOV EAX, unit
		CALL Func_CreateSelectionCircle
		POPAD
	}
}

void removeSelectionCircle(CUnit* unit) {
	__asm {
		PUSHAD
		MOV EAX, unit
		CALL Func_RemoveSelectionCircle
		POPAD
	}
}

bool isTeamAlly(u32 player) {
	u32 result;
	__asm {
		PUSHAD
		MOV EAX, player
		CALL Func_IsTeamAlly
		MOV result, EAX
		POPAD
	}
	return result != 0;
}

void removeDashedCircle(CSprite* sprite) {
	__asm {
		PUSHAD
		MOV EAX, sprite
		CALL Func_RemoveDashedCircle
		POPAD
	}
}

void addDashedCircle(CUnit* unit) {
	__asm {
		PUSHAD
		MOV EAX, unit
		CALL Func_AddDashedCircle
		POPAD
	}
}

void freeImage(CImage* image) {
	__asm {
		PUSHAD
		MOV ESI, image
		CALL Func_FreeImage
		POPAD
	}
}

void initHealthBarImage(CImage* image) {
	__asm {
		PUSHAD
		MOV ECX, image
		XOR EAX, EAX
		CALL Func_InitHealthBarImage
		POPAD
	}
}

void showControl(BinDlg* control) {
	__asm {
		PUSHAD
		MOV ESI, control
		CALL Func_ShowControl
		POPAD
	}
}

void hideControl(BinDlg* control) {
	__asm {
		PUSHAD
		MOV ESI, control
		CALL Func_HideControl
		POPAD
	}
}

void invalidateControl(BinDlg* control) {
	__asm {
		PUSHAD
		MOV EAX, control
		CALL Func_InvalidateControl
		POPAD
	}
}

bool canMultiSelect(CUnit* unit) {
	u32 result;
	__asm {
		PUSHAD
		MOV ECX, unit
		CALL Func_CanMultiSelect
		MOV result, EAX
		POPAD
	}
	return result != 0;
}

bool outranks(CUnit* unit, CUnit* best) {
	u32 result;
	__asm {
		PUSHAD
		MOV EDI, unit
		MOV ESI, best
		CALL Func_Outranks
		MOV result, EAX
		POPAD
	}
	return result != 0;
}

bool selectRecentGroupOf(u32 tag) {
	u32 result;
	__asm {
		PUSHAD
		PUSH tag
		CALL Func_SelectRecentGroupOf
		MOV result, EAX
		POPAD
	}
	return result != 0;
}

void centerViewOnGroup(u32 group) {
	__asm {
		PUSHAD
		MOV ECX, group
		CALL Func_CenterViewOnGroup
		POPAD
	}
}

void queueCommand(const void* data, u32 bytes) {
	__asm {
		PUSHAD
		MOV ECX, data
		MOV EDX, bytes
		CALL Func_QueueCommand
		POPAD
	}
}

void moveScreen(s32 x, s32 y) {
	__asm {
		PUSHAD
		MOV EAX, x
		MOV ECX, y
		CALL Func_MoveScreen
		POPAD
	}
}

void cancelPlacement() {
	__asm {
		PUSHAD
		CALL Func_RefreshLayer3And4
		CALL Func_Sub48E310
		POPAD
	}
}

//Enters the minimap key handler's toggle code at EAX, with EDI = the minimap
//dialog, as if from its own entry (push esi, push edi): the code ends
//pop edi / mov eax, 1 / pop esi / ret, which returns to the caller.
void __declspec(naked) minimapToggleThunk() {
	__asm {
		PUSH ESI
		PUSH EDI
		JMP EAX
	}
}

void minimapToggle(bool allyColours) {
	BinDlg* const dialog = *(BinDlg**)0x0059CB5C;	//the minimap dialog
	if (dialog == NULL)
		return;
	const u32 entry = allyColours ? 0x004A5948 : 0x004A597B;
	__asm {
		PUSHAD
		MOV EDI, dialog
		MOV EAX, entry
		CALL minimapToggleThunk
		POPAD
	}
}

void cancelTargetOrder() {
	__asm {
		PUSHAD
		CALL Func_CancelTargetOrder
		POPAD
	}
}

bool writeCompressed(FILE* file, const void* data, u32 bytes) {
	u32 result;
	__asm {
		PUSHAD
		PUSH file
		PUSH bytes
		MOV EAX, data
		CALL Func_WriteCompressed
		MOV result, EAX
		POPAD
	}
	return result != 0;
}

bool readCompressed(FILE* file, void* data, u32 size) {
	return readCompressedFn(data, size, file) != 0;
}

size_t fwriteExe(const void* data, size_t size, size_t count, FILE* file) {
	return fwriteFn(data, size, count, file);
}

size_t freadExe(void* data, size_t size, size_t count, FILE* file) {
	return freadFn(data, size, count, file);
}

//-------- Smart-build: the build receive path --------//

bool placeBuildingAllowed(CUnit* unit, u8 order, u16 unitType) {
	static u32 result;
	const u32 Func = 0x0048DBD0;
	__asm {
		PUSHAD
		MOV AX, unitType
		MOV DL, order
		MOV ECX, unit
		CALL Func
		MOV result, EAX
		POPAD
	}
	return result != 0;
}

void placeBuilding(u8 order, u16 unitType, u32 tiles) {
	const u32 Func = 0x0048E190;
	__asm {
		PUSHAD
		PUSH tiles
		MOV AX, unitType
		MOV CL, order
		CALL Func
		POPAD
	}
}

u32 placementCheck(CUnit* builder, u8 player, s32 tileX, s32 tileY, u16 type) {
	static u32 result;
	const u32 Func = 0x00473FB0;
	const u32 player32 = player, type32 = type;
	__asm {
		PUSHAD
		PUSH 0
		PUSH 0
		PUSH 0
		PUSH 1
		PUSH type32
		PUSH tileY
		PUSH tileX
		PUSH player32
		PUSH builder
		CALL Func
		MOV result, EAX
		POPAD
	}
	return result;
}

bool placementMessage(u32 code) {
	static u32 result;
	const u32 Func = 0x0048D930;
	__asm {
		PUSHAD
		MOV EAX, code
		CALL Func
		MOV result, EAX
		POPAD
	}
	return result != 0;
}

bool hasSupplies(u16 type, u8 player) {
	static u32 result;
	const u32 Func = 0x0042CF70;
	const u32 player32 = player, type32 = type;
	__asm {
		PUSHAD
		PUSH 1
		PUSH type32
		PUSH player32
		CALL Func
		MOV result, EAX
		POPAD
	}
	return result != 0;
}

void refundQueueSlots(CUnit* unit) {
	const u32 Func = 0x00466E80;
	__asm {
		PUSHAD
		MOV EAX, unit
		CALL Func
		POPAD
	}
}

bool fillBuildSlot(CUnit* unit, u16 type) {
	static u32 result;
	const u32 Func = 0x00467250;
	const u32 type32 = type;
	__asm {
		PUSHAD
		PUSH type32
		MOV EDI, unit
		CALL Func
		MOV result, EAX
		POPAD
	}
	return result != 0;
}

bool withinReach(CUnit* unit, s32 x, s32 y, u32 distance) {
	static u32 result;
	const u32 Func = 0x00401240;
	__asm {
		PUSHAD
		PUSH x
		PUSH distance
		MOV EAX, y
		MOV ECX, unit
		CALL Func
		MOV result, EAX
		POPAD
	}
	return result != 0;
}

void showStatTextTo(u32 stringId, u8 player) {
	const char* const text = statTxtTbl->getString((u16)stringId);
	const u32 player32 = player;
	const u32 Func = 0x0048CF00;
	__asm {
		PUSHAD
		MOV ECX, player32
		MOV EAX, text
		CALL Func
		POPAD
	}
}

} //selexe
