//Wrappers from the exe's calling conventions to the extended selection, and
//the functions that install them. Addresses and conventions: see the spec.
#include "selection_ext_hooks.h"
#include "sel_synced.h"
#include "sel_local.h"
#include "sel_send.h"
#include "sel_save.h"
#include <SCBW/selection_ext.h>
#include <hook_tools.h>

namespace {

//-------- Stage 1 --------//

//0x49A850: no arguments, the unit in EAX; every other register kept.
void __declspec(naked) nextSelectedWrapper() {
	static CUnit* unit;
	__asm PUSHAD
	unit = selsync::nextSelected();
	__asm {
		POPAD
		MOV EAX, unit
		RETN
	}
}

//0x49A7F0: EDI = unit.
void __declspec(naked) removeFromAllSelectionsWrapper() {
	static CUnit* unit;
	__asm {
		MOV unit, EDI
		PUSHAD
	}
	selsync::removeFromAllSelections(unit);
	__asm {
		POPAD
		RETN
	}
}

//0x49A740: EAX = player.
void __declspec(naked) clearSelectionWrapper() {
	static u32 player;
	__asm {
		MOV player, EAX
		PUSHAD
	}
	selsync::clearSelection(player);
	__asm {
		POPAD
		RETN
	}
}

//0x4C2750, 0x4C2560, 0x4BFB40: stdcall(packet).
void __declspec(naked) recvSelectWrapper() {
	static const u8* packet;
	__asm {
		MOV EAX, [ESP+4]
		MOV packet, EAX
		PUSHAD
	}
	selsync::recvSelect(packet);
	__asm {
		POPAD
		RETN 4
	}
}

void __declspec(naked) recvShiftSelectWrapper() {
	static const u8* packet;
	__asm {
		MOV EAX, [ESP+4]
		MOV packet, EAX
		PUSHAD
	}
	selsync::recvShiftSelect(packet);
	__asm {
		POPAD
		RETN 4
	}
}

void __declspec(naked) recvShiftDeselectWrapper() {
	static const u8* packet;
	__asm {
		MOV EAX, [ESP+4]
		MOV packet, EAX
		PUSHAD
	}
	selsync::recvShiftDeselect(packet);
	__asm {
		POPAD
		RETN 4
	}
}

//0x4C2870: ECX = packet.
void __declspec(naked) recvHotkeyWrapper() {
	static const u8* packet;
	__asm {
		MOV packet, ECX
		PUSHAD
	}
	selsync::recvHotkey(packet);
	__asm {
		POPAD
		RETN
	}
}

//0x49AE40: EAX = list, stdcall(count).
void __declspec(naked) buildActiveWrapper() {
	static CUnit** list;
	static u32 count;
	__asm {
		MOV list, EAX
		MOV EAX, [ESP+4]
		MOV count, EAX
		PUSHAD
	}
	sellocal::buildActive(list, count);
	__asm {
		POPAD
		RETN 4
	}
}

//0x49F7A0: EAX = unit.
void __declspec(naked) localRemoveWrapper() {
	static CUnit* unit;
	__asm {
		MOV unit, EAX
		PUSHAD
	}
	sellocal::localRemove(unit);
	__asm {
		POPAD
		RETN
	}
}

//0x499A60: no arguments.
void __declspec(naked) redrawCirclesWrapper() {
	__asm PUSHAD
	sellocal::redrawCircles();
	__asm {
		POPAD
		RETN
	}
}

//0x4C38B0: no arguments; ends with a tail jump to 0x458DE0 (button set).
const u32 Func_UpdateButtonSet = 0x00458DE0;
void __declspec(naked) clientCopyWrapper() {
	__asm PUSHAD
	sellocal::clientCopy();
	__asm {
		POPAD
		JMP Func_UpdateButtonSet
	}
}

//0x4C3B40: EDX = unit.
void __declspec(naked) deselectAndSendWrapper() {
	static CUnit* unit;
	__asm {
		MOV unit, EDX
		PUSHAD
	}
	sellocal::deselectAndSend(unit);
	__asm {
		POPAD
		RETN
	}
}

//0x4D0820: no arguments.
void __declspec(naked) reselectAtStartWrapper() {
	__asm PUSHAD
	sellocal::reselectAtStart();
	__asm {
		POPAD
		RETN
	}
}

//0x4EED10 entry (8 bytes: push edi; xor eax, eax; mov ecx, 0xC).
const u32 Back_GameStartEntry = 0x004EED18;
void __declspec(naked) gameStartEntryWrapper() {
	__asm PUSHAD
	sellocal::gameStartClear();
	__asm {
		POPAD
		PUSH EDI
		XOR EAX, EAX
		MOV ECX, 0x0C
		JMP Back_GameStartEntry
	}
}

//0x4EEDC6 (5 bytes: mov eax, [0x512688]), where vanilla keeps the local
//player's 12 and wipes the synced selections.
const u32 Back_GameStartKeepLocal = 0x004EEDCB;
void __declspec(naked) gameStartKeepLocalWrapper() {
	__asm PUSHAD
	sellocal::gameStartKeepLocal();
	__asm {
		POPAD
		MOV EAX, 0x00512688
		MOV EAX, [EAX]
		JMP Back_GameStartKeepLocal
	}
}

//0x4C0860: stdcall(count, list).
void __declspec(naked) cmdactSelectWrapper() {
	static u32 count;
	static CUnit** list;
	__asm {
		MOV EAX, [ESP+4]
		MOV count, EAX
		MOV EAX, [ESP+8]
		MOV list, EAX
		PUSHAD
	}
	selsend::cmdactSelect(count, list);
	__asm {
		POPAD
		RETN 8
	}
}

//0x4C07B0: BL = group, stdcall(u8 action, CUnit** list, u8 count).
void __declspec(naked) cmdactHotkeyWrapper() {
	static u32 group, action, count;
	static CUnit** list;
	__asm {
		MOV group, EBX
		MOV EAX, [ESP+4]
		MOV action, EAX
		MOV EAX, [ESP+8]
		MOV list, EAX
		MOV EAX, [ESP+12]
		MOV count, EAX
		PUSHAD
	}
	selsend::cmdactHotkey(group & 0xFF, action & 0xFF, list, count & 0xFF);
	__asm {
		POPAD
		RETN 12
	}
}

//0x4D603C in 0x4D6010 (health bar setup; 7 bytes: mov edi, [ecx*4+0x6284B8]):
//ECX = the sprite's selectionIndex, [EBP+8] = the sprite; out EDI = its unit.
const u32 Back_HealthBarUnit = 0x004D6043;
void __declspec(naked) healthBarUnitStub() {
	static CSprite* sprite;
	static u32 slot;
	static CUnit* unit;
	__asm {
		MOV slot, ECX
		MOV EDI, [EBP+8]
		MOV sprite, EDI
		PUSHAD
	}
	unit = sellocal::unitForHealthBar(sprite, slot);
	__asm {
		POPAD
		MOV EDI, unit
		JMP Back_HealthBarUnit
	}
}

//-------- Stage 2 --------//

u32 __stdcall commandLengthOf(const u8* command) {
	return selext::commandLength(command);
}

void __cdecl recvSelectChunkC(const u8* packet) {
	selsync::recvSelectChunk(packet);
}

//Dispatch slot 50 of the executor 0x4865D0 (ids 0x3C-0x44, 0x48, 0x5B). ESI =
//command, EAX = bytes left, [EBP+8] = bytes left, [EBP-4] = command size.
const u32 Back_CommandDone = 0x00486D7A;	//records the command and advances
const u32 Back_CommandBad = 0x00486DA3;		//vanilla's exit for these ids
void __declspec(naked) selectChunkDispatch() {
	__asm {
		CMP BYTE PTR [ESI], 0x3C
		JNE notOurs
		CMP EAX, 3
		JL notOurs
		MOVZX ECX, BYTE PTR [ESI+2]
		LEA ECX, [ECX*2+3]
		SUB EAX, ECX
		MOV [EBP+8], EAX
		JS notOurs
		MOV [EBP-4], ECX
		PUSHAD
		PUSH ESI
		CALL recvSelectChunkC
		ADD ESP, 4
		POPAD
		JMP Back_CommandDone
	notOurs:
		JMP Back_CommandBad
	}
}

//0x486619 (executor, replay viewer's skip path): ECX = id, ESI = command;
//out EDX = length, then 0x486632.
const u32 Back_ExecutorLength = 0x00486632;
void __declspec(naked) executorLengthStub() {
	__asm {
		PUSH EAX
		PUSH ECX
		PUSH ESI
		CALL commandLengthOf
		MOV EDX, EAX
		POP ECX
		POP EAX
		JMP Back_ExecutorLength
	}
}

//0x4CE082 (replay playback 0x4CDFF0): EAX = id, ECX = command; out EBX =
//length, then 0x4CE09B. ECX and EDX are used afterwards.
const u32 Back_PlaybackLength = 0x004CE09B;
void __declspec(naked) playbackLengthStub() {
	__asm {
		PUSH ECX
		PUSH EDX
		PUSH ECX
		CALL commandLengthOf
		MOV EBX, EAX
		POP EDX
		POP ECX
		JMP Back_PlaybackLength
	}
}

//0x4CDD04 (replay save walk 0x4CDCE0): EAX + 1 = command; does vanilla's
//INC EAX, out EDX = length, then 0x4CDD1E.
const u32 Back_SaveWalkLength = 0x004CDD1E;
void __declspec(naked) saveWalkLengthStub() {
	__asm {
		INC EAX
		PUSH EAX
		PUSH ECX
		PUSH EAX
		CALL commandLengthOf
		MOV EDX, EAX
		POP ECX
		POP EAX
		JMP Back_SaveWalkLength
	}
}

//0x45D040: stdcall(twin).
void __declspec(naked) addTwinWrapper() {
	static CUnit* twin;
	__asm {
		MOV EAX, [ESP+4]
		MOV twin, EAX
		PUSHAD
	}
	sellocal::addTwin(twin);
	__asm {
		POPAD
		RETN 4
	}
}

} //unnamed namespace

namespace hooks {

void injectSelectionExtHooks() {
	jmpPatch(nextSelectedWrapper,				0x0049A850, 2);
	jmpPatch(removeFromAllSelectionsWrapper,	0x0049A7F0, 1);
	jmpPatch(clearSelectionWrapper,				0x0049A740, 1);
	jmpPatch(recvSelectWrapper,					0x004C2750, 1);
	jmpPatch(recvShiftSelectWrapper,			0x004C2560, 1);
	jmpPatch(recvShiftDeselectWrapper,			0x004BFB40, 1);
	jmpPatch(recvHotkeyWrapper,					0x004C2870, 0);
	jmpPatch(buildActiveWrapper,				0x0049AE40, 0);
	jmpPatch(localRemoveWrapper,				0x0049F7A0, 1);
	jmpPatch(redrawCirclesWrapper,				0x00499A60, 1);
	jmpPatch(healthBarUnitStub,					0x004D603C, 2);
	jmpPatch(clientCopyWrapper,					0x004C38B0, 3);
	jmpPatch(deselectAndSendWrapper,			0x004C3B40, 1);
	jmpPatch(reselectAtStartWrapper,			0x004D0820, 1);
	jmpPatch(gameStartEntryWrapper,				0x004EED10, 3);
	jmpPatch(gameStartKeepLocalWrapper,			0x004EEDC6, 0);
	jmpPatch(cmdactSelectWrapper,				0x004C0860, 1);
	jmpPatch(cmdactHotkeyWrapper,				0x004C07B0, 2);
	callPatch(selsave::writeLastAndExtension,	0x004C2E0A, 0);
	callPatch(selsave::readLastAndExtension,	0x004D0225, 0);
}

void injectSelectChunkHooks() {
	memoryPatch(0x00486ED0, (u32)&selectChunkDispatch);
	jmpPatch(executorLengthStub,	0x00486619, 20);
	jmpPatch(playbackLengthStub,	0x004CE082, 20);
	jmpPatch(saveWalkLengthStub,	0x004CDD04, 21);
	jmpPatch(addTwinWrapper,		0x0045D040, 1);
}

} //hooks
