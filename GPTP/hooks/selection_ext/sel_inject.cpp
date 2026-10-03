//Wrappers from the exe's calling conventions to the extended selection, and
//the functions that install them. Addresses and conventions: see the spec.
#include "selection_ext_hooks.h"
#include "sel_synced.h"
#include "sel_local.h"
#include "sel_send.h"
#include "sel_save.h"
#include "sel_panel.h"
#include "sel_groups.h"
#include "sel_subgroups.h"
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
	//Every exe caller (drag, click, recall, Alt-click) makes a new selection.
	sellocal::buildActiveNewSelection(list, count);
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

//The calls to 0x4D6930 (builds the image pools; no arguments) at 0x4EEE5F and
//0x4EEFED, the new-game and load paths of game init.
const u32 Func_InitImages = 0x004D6930;
void __declspec(naked) initImagesWrapper() {
	__asm {
		CALL Func_InitImages
		PUSHAD
	}
	sellocal::growImagePools();
	__asm {
		POPAD
		RETN
	}
}

//-------- Stage 3 --------//

//0x425960: EAX = dialog.
void __declspec(naked) panelFillWrapper() {
	static BinDlg* dialog;
	__asm {
		MOV dialog, EAX
		PUSHAD
	}
	selpanel::fill(dialog);
	__asm {
		POPAD
		RETN
	}
}

//0x424660: no arguments, BOOL in EAX.
void __declspec(naked) panelChangedWrapper() {
	static u32 result;
	__asm PUSHAD
	//No temporaries here: in a naked function they land in the caller's frame
	//(tests/check_naked_wrappers.py).
	result = selpanel::changed();
	__asm {
		POPAD
		MOV EAX, result
		RETN
	}
}

//0x458220: EDX = the clicked wireframe.
void __declspec(naked) panelClickWrapper() {
	static BinDlg* control;
	__asm {
		MOV control, EDX
		PUSHAD
	}
	selpanel::click(control);
	__asm {
		POPAD
		RETN
	}
}

//0x484350, the in-game KEYDOWN proc: ECX = the event.
void __declspec(naked) keyDownWrapper() {
	static const u8* event;
	__asm {
		MOV event, ECX
		PUSHAD
	}
	selpanel::keyDown(event);
	__asm {
		POPAD
		RETN
	}
}

//-------- Stage 4 --------//

//0x496D30 (Alt-click): stdcall(tag), BOOL in EAX.
void __declspec(naked) selectRecentGroupWrapper() {
	static u32 tag;
	static u32 result;
	__asm {
		MOV EAX, [ESP+4]
		MOV tag, EAX
		PUSHAD
	}
	result = selgroups::selectRecentGroupOf(tag);
	__asm {
		POPAD
		MOV EAX, result
		RETN 4
	}
}

//0x4967E0 (double-tap centring): CL = group.
void __declspec(naked) centerViewOnGroupWrapper() {
	static u32 group;
	__asm {
		MOVZX EAX, CL
		MOV group, EAX
		PUSHAD
	}
	selgroups::centerViewOnGroup(group);
	__asm {
		POPAD
		RETN
	}
}

//The keyboard scroll (0x47EF80) reads PgDn's and PgUp's held state at
//0x47EFEA and 0x47F03A (6 bytes each: mov dl, [key]; then test dl, dl). With
//Ctrl held they read as up, so Ctrl+PgUp/PgDn turn pages without scrolling.
void __declspec(naked) pageDownHeldStub() {
	__asm {
		PUSH EAX
		MOV EAX, 0x00596A3A
		MOV DL, [EAX]
		MOV EAX, 0x00596A29
		CMP BYTE PTR [EAX], 0
		POP EAX
		JE keep
		XOR DL, DL
	keep:
		RETN
	}
}

void __declspec(naked) pageUpHeldStub() {
	__asm {
		PUSH EAX
		MOV EAX, 0x00596A39
		MOV DL, [EAX]
		MOV EAX, 0x00596A29
		CMP BYTE PTR [EAX], 0
		POP EAX
		JE keep
		XOR DL, DL
	keep:
		RETN
	}
}

//-------- Stage 5 --------//

//0x45990F (button click, 24 bytes to the handler's return): ESI = the dialog.
//Runs the button's action with the client mirror holding the active
//subgroup's best units, then returns from the handler as vanilla does.
void __declspec(naked) buttonActionStub() {
	__asm {
		MOV ESI, [ESI+0x26]
		PUSHAD
	}
	selsub::viewBegin();
	__asm {
		POPAD
		PUSH EAX
		MOV EAX, 0x00596A28
		MOV DL, [EAX]
		POP EAX
		MOV CX, [ESI+0x0E]
		CALL DWORD PTR [ESI+8]
		PUSHAD
	}
	selsub::viewEnd();
	__asm {
		POPAD
		POP EDI
		MOV EAX, 1
		POP ESI
		RETN
	}
}

//Calls of 0x46F5B0 (target-order send check): stdcall, 4 arguments. Each
//PUSH [ESP+0x10] copies the next argument down (they sit at ESP+4..+0x10
//after the call here, and every push moves ESP by 4).
const u32 Func_TargetOrderCheck = 0x0046F5B0;
void __declspec(naked) targetOrderCheckStub() {
	static u32 result;
	__asm PUSHAD
	selsub::viewBegin();
	__asm {
		POPAD
		PUSH DWORD PTR [ESP+0x10]
		PUSH DWORD PTR [ESP+0x10]
		PUSH DWORD PTR [ESP+0x10]
		PUSH DWORD PTR [ESP+0x10]
		CALL Func_TargetOrderCheck
		MOV result, EAX
		PUSHAD
	}
	selsub::viewEnd();
	__asm {
		POPAD
		MOV EAX, result
		RETN 0x10
	}
}

//0x456FB6 (wireframe draw proc, after its colour fill, before the frame is
//drawn): ESI = the unit. Dims the colours, then the replaced
//mov eax, [0x68C1FC], and back.
const u32 WireframeDrawBack = 0x00456FBB;
void __declspec(naked) wireframeDimStub() {
	static CUnit* unit;
	__asm {
		MOV unit, ESI
		PUSHAD
	}
	selsub::dimWireframe(unit);
	__asm {
		POPAD
		MOV EAX, 0x0068C1FC
		MOV EAX, [EAX]
		JMP WireframeDrawBack
	}
}

//0x4A5938 in the minimap key handler 0x4A5900: vanilla's
//cmp word [esi+8], 9 / jne, on a typed character (event 0xF, from WM_CHAR):
//Tab, or Shift+Tab. Tab now cycles subgroups, so no character is the
//minimap's; its toggles moved to Alt+T / Ctrl+Shift+T (selpanel::keyDown).
const u32 MinimapNotMine = 0x004A5921;
void __declspec(naked) minimapKeyStub() {
	__asm JMP MinimapNotMine
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
	callPatch(initImagesWrapper,				0x004EEE5F, 0);
	callPatch(initImagesWrapper,				0x004EEFED, 0);
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

void injectSelectionPanelHooks() {
	//StatData's interact table, which 0x4584C0 passes to 0x418100 at
	//USER_CREATE: vanilla's 44 entries, then the wireframe handler.
	memoryPatch(0x004584C3, selpanel::interactTableBytes());
	memoryPatch(0x004584C8, (u32)selpanel::interactTable());
	//Tooltips for every wireframe id (a sign-extended imm8, so at most 0x7F).
	memoryPatch(0x00457D7E, (u8)(selext::WIREFRAME_FIRST_ID + selext::WIREFRAME_MAX - 1));
	jmpPatch(panelFillWrapper,		0x00425960, 6);
	jmpPatch(panelChangedWrapper,	0x00424660, 2);
	jmpPatch(panelClickWrapper,		0x00458220, 1);
	jmpPatch(keyDownWrapper,		0x00484350, 0);
	callPatch(pageDownHeldStub,		0x0047EFEA, 1);
	callPatch(pageUpHeldStub,		0x0047F03A, 1);
}

void injectControlGroupHooks() {
	jmpPatch(selectRecentGroupWrapper,	0x00496D30, 1);
	jmpPatch(centerViewOnGroupWrapper,	0x004967E0, 1);
}

void injectCommandCardHooks() {
	jmpPatch(buttonActionStub,		0x0045990F, 19);
	callPatch(targetOrderCheckStub,	0x004A5631, 0);
	callPatch(targetOrderCheckStub,	0x004BD54B, 0);
	callPatch(targetOrderCheckStub,	0x004BD564, 0);
	jmpPatch(wireframeDimStub,		0x00456FB6, 0);
	//The minimap's Tab toggles move to Alt+T / Ctrl+Shift+T (selpanel::keyDown).
	jmpPatch(minimapKeyStub,		0x004A5938, 2);
}

} //hooks
