#include "sel_panel.h"
#include "sel_exe.h"
#include "sel_local.h"
#include "sel_send.h"
#include "sel_subgroups.h"
#include <SCBW/selection_ext.h>

using namespace selext;

namespace {

u8* const	LAYOUT				= (u8*)	0x0068C1E5;	//1: the multi-selection layout is up
u8* const	REFRESH_STAT_DATA	= (u8*)	0x0068C1F8;
const u8* const	SHIFT_HELD		= (u8*)	0x00596A28;
const u8* const	CTRL_HELD		= (u8*)	0x00596A29;
const u8* const	ALT_HELD		= (u8*)	0x00596A2A;
const u32* const VANILLA_INTERACT = (u32*)0x00504AF0;	//44 entries
const u32 VANILLA_CONTROLS = 44;
const u32 WIREFRAME_INTERACT = 0x004583E0;
//The console's TextBox dialog (rez\?textbox.bin), made at console setup
//(0x4F38D1). It is visible all game (its .bin flags it so); the chat input is
//its edit control, id 6, which shows while the player types.
BinDlg* const* const TEXT_BOX = (BinDlg**)0x0068C140;
const s16 CHAT_EDIT_ID = 6;

//Made by the wireframe handler at USER_CREATE; its draw proc reads it.
struct WireframeUser {
	CUnit* unit;
	u16 unitId;
	u16 pad;
};

u32 interact[WIREFRAME_FIRST_ID - 1 + WIREFRAME_MAX];
bool interactBuilt;

u32 pageSize = VANILLA_MAX;		//of the last fill
u32 shownStamp;					//selsub::activeStamp() at the last fill
u32 shownStart;
u32 shownCount;
s32 shownHitPoints[WIREFRAME_MAX];
u16 shownId[WIREFRAME_MAX];

BinDlg* firstChild(BinDlg* dialog) {
	return *(BinDlg**)((u8*)dialog + 0x42);
}

WireframeUser* userOf(BinDlg* control) {
	return (WireframeUser*)control->user;
}

bool isVisible(const BinDlg* dialog) {
	return (dialog->flags & BinDlgFlags::Visible) != 0;
}

//Whether the player is typing a chat message.
bool isChatOpen() {
	BinDlg* const textBox = *TEXT_BOX;
	if (textBox == NULL || !isVisible(textBox))
		return false;
	for (BinDlg* control = firstChild(textBox); control != NULL; control = control->next)
		if (control->index == CHAT_EDIT_ID)
			return isVisible(control);
	return false;
}

//The dialog's wireframe controls by id; returns how many there are.
u32 collectWireframes(BinDlg* dialog, BinDlg** wireframes) {
	for (u32 k = 0; k < WIREFRAME_MAX; k++)
		wireframes[k] = NULL;
	for (BinDlg* control = firstChild(dialog); control != NULL; control = control->next) {
		const s32 k = control->index - (s32)WIREFRAME_FIRST_ID;
		if (k >= 0 && k < (s32)WIREFRAME_MAX)
			wireframes[k] = control;
	}
	u32 n = 0;
	while (n < WIREFRAME_MAX && wireframes[n] != NULL)
		n++;
	return n;
}

} //unnamed namespace

namespace selpanel {

void fill(BinDlg* dialog) {
	if (*LAYOUT != 1) {
		for (BinDlg* control = dialog->controlType ? dialog : firstChild(dialog);
			 control != NULL; control = control->next)
			selexe::hideControl(control);
		*LAYOUT = 1;
	}
	if (dialog->controlType)
		dialog = dialog->parent;

	static BinDlg* wireframes[WIREFRAME_MAX];
	const u32 controls = collectWireframes(dialog, wireframes);
	pageSize = pageSizeFor(dialog->bounds.width, controls);
	selectionPage = clampPage(selectionPage, clientCount, pageSize);
	shownStart = selectionPage * pageSize;

	u32 k = 0;
	for (u32 i = shownStart; i < SEL_MAX && k < pageSize; i++) {
		CUnit* unit = clientSel[i];
		if (unit == NULL)
			break;
		BinDlg* control = wireframes[k];
		userOf(control)->unit = unit;
		userOf(control)->unitId = unit->id;
		selexe::showControl(control);
		if (!(control->flags & 1)) {
			control->flags |= 1;
			selexe::invalidateControl(control);
		}
		shownHitPoints[k] = unit->hitPoints;
		shownId[k] = unit->id;
		k++;
	}
	shownCount = k;
	//Stage 5 highlight: a new active subgroup changes which wireframes are
	//dimmed without changing the units, so draw them all again.
	if (selsub::activeStamp() != shownStamp) {
		shownStamp = selsub::activeStamp();
		for (u32 s = 0; s < shownCount; s++)
			selexe::invalidateControl(wireframes[s]);
	}
	for (; k < controls; k++)
		selexe::hideControl(wireframes[k]);
}

bool changed() {
	//A new active subgroup or palette changes the dimmed wireframes.
	if (selsub::activeStamp() != shownStamp)
		return true;
	if (shownCount == 0 && clientCount > shownStart)
		return true;
	if (clientCount != 0 && shownStart >= clientCount)
		return true;
	for (u32 k = 0; k < pageSize && shownStart + k < SEL_MAX; k++) {
		CUnit* unit = clientSel[shownStart + k];
		if (unit == NULL)
			continue;
		if (k >= shownCount || unit->hitPoints != shownHitPoints[k] || unit->id != shownId[k])
			return true;
	}
	return false;
}

void click(BinDlg* control) {
	static CUnit* list[SEL_MAX];
	CUnit* const clickedUnit = userOf(control)->unit;
	const bool shift = *SHIFT_HELD != 0;
	const bool ctrl = !shift && *CTRL_HELD != 0;
	u32 n = 0;
	if (shift) {
		for (u32 i = 0; i < SEL_MAX && clientSel[i] != NULL; i++)
			if (clientSel[i] != clickedUnit)
				list[n++] = clientSel[i];
	}
	else
	if (ctrl) {
		//The clicked unit's subgroup (stage 5): every state of its type.
		const u32 key = selsub::keyOf(clickedUnit);
		for (u32 i = 0; i < SEL_MAX && clientSel[i] != NULL; i++)
			if (selsub::keyOf(clientSel[i]) == key)
				list[n++] = clientSel[i];
	}
	else {
		if (*ALT_HELD && selexe::selectRecentGroupOf(tagOf(clickedUnit)))
			return;
		list[n++] = clickedUnit;
	}
	if ((shift || ctrl) && n == 1 && *ALT_HELD && selexe::selectRecentGroupOf(tagOf(list[0])))
		return;
	//Removing a unit keeps the page; a new selection starts at page 0.
	if (!shift)
		selectionPage = 0;
	sellocal::buildActive(list, n);
	selsend::cmdactSelect(n, list);
	sellocal::requestRefresh();
}

void keyDown(const u8* event) {
	const u16 key = *(const u16*)(event + 8);
	//The chat box lets PgUp/PgDn through to here while it is open.
	const bool chatOpen = isChatOpen();
	//Stage 5: Tab / Shift+Tab activate the next / previous subgroup, and the
	//panel turns to the page of its first unit.
	const u16 VK_TAB_KEY = 0x09;
	if (key == VK_TAB_KEY) {
		if (!chatOpen && selsub::cycle(*SHIFT_HELD != 0)) {
			if (pageSize != 0)
				selectionPage = selsub::activeFirstIndex() / pageSize;
			sellocal::requestRefresh();
		}
		return;
	}
	const u32 page = pageAfterKey(key, *CTRL_HELD != 0, chatOpen, selectionPage,
	                              pageCountFor(clientCount, pageSize));
	if (page == selectionPage)
		return;
	selectionPage = page;
	*REFRESH_STAT_DATA = 1;
}

const u32* interactTable() {
	if (!interactBuilt) {
		for (u32 i = 0; i < VANILLA_CONTROLS; i++)
			interact[i] = VANILLA_INTERACT[i];
		for (u32 id = VANILLA_CONTROLS + 1; id < WIREFRAME_FIRST_ID + WIREFRAME_MAX; id++)
			interact[id - 1] = WIREFRAME_INTERACT;
		interactBuilt = true;
	}
	return interact;
}

u32 interactTableBytes() {
	return sizeof(interact);
}

} //selpanel
