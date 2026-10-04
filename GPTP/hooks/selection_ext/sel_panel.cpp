#include "sel_panel.h"
#include "sel_exe.h"
#include "sel_local.h"
#include "sel_send.h"
#include "sel_subgroups.h"
#include <SCBW/selection_ext.h>
#include <definitions.h>
#include <graphics/Font.h>
#include <graphics/Bitmap.h>
#include <cstdio>

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

u32 interact[PANEL_LAST_ID];
bool interactBuilt;

u32 pageSize = VANILLA_MAX;		//of the last fill
bool outdatedWarned;			//this game
u32 shownStamp;					//selsub::activeStamp() at the last fill
u32 shownStart;
u32 shownCount;
s32 shownHitPoints[WIREFRAME_MAX];
u16 shownId[WIREFRAME_MAX];

//The page controls as last drawn, to redraw them when these change.
u32 drawnPage = 0xFFFFFFFF;
u32 drawnPages;
PageControls drawnMode;

//The current-page tab (0xA5 brighter, the fill lighter) and greyed arrows.
u8 litRemap[256];
u8 greyRemap[256];
bool remapsBuilt;

//Frame 0x0D of the GRP at [0x68C1C0], decoded once per GRP.
const u8* const* const CMDBTNS_GRP = (const u8**)0x0068C1C0;
const u32 WIREFRAME_FRAME = 0x0D;
const u8* decodedFrom;
u8 frame[FRAME_WIDTH * FRAME_HEIGHT];
bool frameOk;

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

//The dialog's panel controls by id; returns how many wireframes there are
//(the first gap ends them). Missing tabs and arrows are NULL.
u32 collectControls(BinDlg* dialog, BinDlg** wireframes, BinDlg** tabs, BinDlg** arrows) {
	for (u32 k = 0; k < WIREFRAME_MAX; k++)
		wireframes[k] = NULL;
	for (u32 t = 0; t < PAGE_TABS; t++)
		tabs[t] = NULL;
	for (u32 a = 0; a < 3; a++)
		arrows[a] = NULL;
	for (BinDlg* control = firstChild(dialog); control != NULL; control = control->next) {
		const s32 id = control->index;
		if (id >= (s32)WIREFRAME_FIRST_ID && id < (s32)PAGE_TAB_FIRST_ID)
			wireframes[id - WIREFRAME_FIRST_ID] = control;
		else if (id >= (s32)PAGE_TAB_FIRST_ID && id < (s32)PAGE_UP_ID)
			tabs[id - PAGE_TAB_FIRST_ID] = control;
		else if (id >= (s32)PAGE_UP_ID && id <= (s32)PAGE_DOWN_ID)
			arrows[id - PAGE_UP_ID] = control;
	}
	u32 n = 0;
	while (n < WIREFRAME_MAX && wireframes[n] != NULL)
		n++;
	return n;
}

//Shows the tabs or the arrows for the selection's pages, and redraws them
//when the page, the page count or the kind of control changes.
void updatePageControls(BinDlg** tabs, BinDlg** arrows) {
	const u32 pages = pageCountFor(clientCount, pageSize);
	const PageControls mode = pageControlsFor(pages);
	const bool redraw = pages != drawnPages || selectionPage != drawnPage || mode != drawnMode;
	for (u32 t = 0; t < PAGE_TABS; t++) {
		if (tabs[t] == NULL)
			continue;
		if (mode == PAGE_CONTROLS_TABS && t < pages) {
			selexe::showControl(tabs[t]);
			if (redraw)
				selexe::invalidateControl(tabs[t]);
		}
		else
			selexe::hideControl(tabs[t]);
	}
	for (u32 a = 0; a < 3; a++) {
		if (arrows[a] == NULL)
			continue;
		if (mode == PAGE_CONTROLS_ARROWS) {
			selexe::showControl(arrows[a]);
			if (redraw)
				selexe::invalidateControl(arrows[a]);
		}
		else
			selexe::hideControl(arrows[a]);
	}
	drawnPages = pages;
	drawnPage = selectionPage;
	drawnMode = mode;
}

struct Surface {
	u16 width;
	u16 height;
	u8* data;
};
//The bitmap a dialog draw proc draws on (0x41C1DF sets it).
Surface* const* const DRAW_SURFACE = (Surface**)0x006CF4A8;

void buildRemaps() {
	for (u32 i = 0; i < 256; i++)
		litRemap[i] = greyRemap[i] = (u8)i;
	litRemap[0xA5] = 0x7E;
	litRemap[0x29] = 0xA0;
	litRemap[0x2A] = 0xA0;
	greyRemap[0xA5] = 0x91;
	greyRemap[0xA0] = 0x43;
	remapsBuilt = true;
}

bool frameReady() {
	const u8* const grp = *CMDBTNS_GRP;
	if (grp == NULL)
		return false;
	if (grp != decodedFrom) {
		frameOk = decodeGrpFrame(grp, WIREFRAME_FRAME, frame);
		decodedFrom = grp;
	}
	return frameOk;
}

//A triangle 11 px wide, 6 tall, pointing up or down, centred on cx.
void drawTriangle(Surface* surface, s32 cx, s32 top, bool up, u8 colour) {
	for (s32 i = 0; i < 6; i++) {
		const s32 y = top + (up ? i : 5 - i);
		if (y < 0 || y >= surface->height)
			continue;
		for (s32 x = cx - i; x <= cx + i; x++)
			if (x >= 0 && x < surface->width)
				surface->data[y * surface->width + x] = colour;
	}
}

void drawCentredText(Surface* surface, const BinDlg* control, const char* text) {
	const s32 width = graphics::Font::getTextWidth(text, 0);
	const s32 height = graphics::Font::getTextHeight(text, 0);
	const s32 x = control->bounds.left + (control->bounds.right - control->bounds.left + 1 - width) / 2;
	const s32 y = control->bounds.top + (control->bounds.bottom - control->bounds.top + 1 - height) / 2;
	((graphics::Bitmap*)surface)->blitString(text, x, y, 0);
}

//The draw proc of the tabs and arrows (called like 0x456F50: ecx the
//control, two stack arguments, ret 8).
void __fastcall pageButtonDraw(BinDlg* control, u32, u32, void*) {
	Surface* const surface = *DRAW_SURFACE;
	if (surface == NULL || !frameReady())
		return;
	if (!remapsBuilt)
		buildRemaps();
	const s32 id = control->index;
	const s32 left = control->bounds.left, top = control->bounds.top;
	const u32 width = control->bounds.right - control->bounds.left + 1;
	const u32 height = control->bounds.bottom - control->bounds.top + 1;
	const u32 pages = pageCountFor(clientCount, pageSize);
	if (id == (s32)PAGE_LABEL_ID) {
		static char text[16];
		sprintf_s(text, sizeof(text), "\x04%u/%u", selectionPage + 1, pages);
		drawCentredText(surface, control, text);
		return;
	}
	if (id == (s32)PAGE_UP_ID || id == (s32)PAGE_DOWN_ID) {
		const bool up = id == (s32)PAGE_UP_ID;
		const bool atEnd = up ? selectionPage == 0 : selectionPage + 1 >= pages;
		drawNineSlice(frame, surface->data, surface->width, surface->width, surface->height,
		              left, top, width, height, atEnd ? greyRemap : NULL);
		drawTriangle(surface, left + (s32)width / 2, top + 4, up, atEnd ? 0x4A : 0x54);
		return;
	}
	const u32 tab = id - PAGE_TAB_FIRST_ID;
	const bool lit = tab == selectionPage;
	drawNineSlice(frame, surface->data, surface->width, surface->width, surface->height,
	              left, top, width, height, lit ? litRemap : NULL);
	static char text[8];
	sprintf_s(text, sizeof(text), lit ? "\x07%u" : "\x04%u", tab + 1);
	drawCentredText(surface, control, text);
}

void pageButtonClicked(s32 id) {
	const u32 pages = pageCountFor(clientCount, pageSize);
	u32 page = selectionPage;
	if (id >= (s32)PAGE_TAB_FIRST_ID && id < (s32)PAGE_UP_ID)
		page = pageAfterTab(id - PAGE_TAB_FIRST_ID, selectionPage, pages);
	else if (id == (s32)PAGE_UP_ID || id == (s32)PAGE_DOWN_ID)
		page = pageAfterArrow(selectionPage, pages, id == (s32)PAGE_UP_ID);
	if (page == selectionPage)
		return;
	selectionPage = page;
	*REFRESH_STAT_DATA = 1;
}

//The interact proc of the tabs and arrows, called like 0x4583E0: ecx the
//control, edx the event (+0x0C its number, 0x0E a user event; +0 the user
//event's kind: 0 create, 2 activate). Anything else goes to the default
//handler of the control's type, as 0x4583E0 does.
const u16 EVENT_USER = 0x0E;
const u32 USER_CREATE = 0;
const u32 USER_ACTIVATE = 2;
typedef u32 (__fastcall* DialogHandler)(BinDlg* control, u8* event);
const DialogHandler* const DEFAULT_HANDLERS = (const DialogHandler*)0x005014AC;

u32 __fastcall pageButtonInteract(BinDlg* control, u8* event) {
	if (*(const u16*)(event + 0x0C) == EVENT_USER) {
		const u32 kind = *(const u32*)event;
		if (kind == USER_CREATE)
			control->fxnUpdate = (void*)pageButtonDraw;
		else if (kind == USER_ACTIVATE) {
			pageButtonClicked(control->index);
			return 1;
		}
	}
	return DEFAULT_HANDLERS[control->controlType](control, event);
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
	static BinDlg* tabs[PAGE_TABS];
	static BinDlg* arrows[3];
	const u32 controls = collectControls(dialog, wireframes, tabs, arrows);
	pageSize = pageSizeFor(dialog->bounds.width, controls);
	if (!outdatedWarned && panelFileOutdated(dialog->bounds.width, controls, arrows[2] != NULL)) {
		static char text[112];
		sprintf_s(text, sizeof(text), PLUGIN_NAME ": statdata.bin not repacked or out of date: %u wireframes%s",
		          controls, arrows[2] != NULL ? "" : ", no page buttons");
		scbw::printText(text, GameTextColor::Yellow);
		outdatedWarned = true;
	}
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
	updatePageControls(tabs, arrows);
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
	//The minimap's vanilla Tab toggles, moved off Tab: Alt+T and Ctrl+Shift+T
	//come here as key-downs (the minimap only sees typed characters, and
	//those keys type none it knows).
	const MinimapToggle toggle = minimapToggleFor(key, *SHIFT_HELD != 0, *CTRL_HELD != 0, *ALT_HELD != 0);
	if (toggle != MINIMAP_NONE) {
		if (!chatOpen)
			selexe::minimapToggle(toggle == MINIMAP_ALLY_COLOURS);
		return;
	}
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

void reset() {
	outdatedWarned = false;
}

const u32* interactTable() {
	if (!interactBuilt) {
		for (u32 i = 0; i < VANILLA_CONTROLS; i++)
			interact[i] = VANILLA_INTERACT[i];
		for (u32 id = VANILLA_CONTROLS + 1; id < PAGE_TAB_FIRST_ID; id++)
			interact[id - 1] = WIREFRAME_INTERACT;
		for (u32 id = PAGE_TAB_FIRST_ID; id <= PANEL_LAST_ID; id++)
			interact[id - 1] = (u32)pageButtonInteract;
		interactBuilt = true;
	}
	return interact;
}

u32 interactTableBytes() {
	return sizeof(interact);
}

} //selpanel
