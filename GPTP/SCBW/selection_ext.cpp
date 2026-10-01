//The storage of the extended selection and everything that touches the
//game's memory: the mirrors, unit tags, the command-length table.
#include "selection_ext.h"
#include <SCBW/api.h>
#include <cstring>

namespace selext {

CUnit* playersSel[PLAYERS][SEL_MAX];
CUnit* activeSel[SEL_MAX];
CUnit* clientSel[SEL_MAX];
u32 clientCount;
CUnit* lastSent[SEL_MAX];
CUnit* reselect[SEL_MAX];
PendingPacket pending[PLAYERS];
u32 selectionPage;
u16 groupsExt[PLAYERS][GROUP_COUNT][SEL_MAX];

namespace {

CUnit** const VANILLA_PLAYERS_SEL	= (CUnit**)	0x006284E8;	//[8][12], synced
CUnit** const VANILLA_ACTIVE_SEL	= (CUnit**)	0x006284B8;
CUnit** const VANILLA_CLIENT_SEL	= (CUnit**)	0x00597208;
u8* const VANILLA_CLIENT_COUNT		= (u8*)		0x0059723D;
CUnit** const VANILLA_LAST_SENT		= (CUnit**)	0x0059724C;
u32* const VANILLA_GROUPS			= (u32*)	0x0057FE60;	//[8][18][12] tags, in CGame
const u32* const COMMAND_LENGTHS	= (u32*)	0x005005F8;

void copyFirst12(CUnit** dest, CUnit* const* src) {
	for (u32 i = 0; i < VANILLA_MAX; i++)
		dest[i] = src[i];
}

} //unnamed namespace

void mirrorPlayer(u32 player) {
	if (player < PLAYERS)
		copyFirst12(&VANILLA_PLAYERS_SEL[player * VANILLA_MAX], playersSel[player]);
}

void mirrorActive() {
	copyFirst12(VANILLA_ACTIVE_SEL, activeSel);
}

void mirrorClient() {
	copyFirst12(VANILLA_CLIENT_SEL, clientSel);
	*VANILLA_CLIENT_COUNT = (u8)(clientCount < VANILLA_MAX ? clientCount : VANILLA_MAX);
}

void mirrorLastSent() {
	copyFirst12(VANILLA_LAST_SENT, lastSent);
}

void mirrorGroup(u32 player, u32 group) {
	if (player >= PLAYERS || group >= GROUP_COUNT)
		return;
	u32* const vanilla = &VANILLA_GROUPS[(player * GROUP_COUNT + group) * VANILLA_MAX];
	for (u32 i = 0; i < VANILLA_MAX; i++)
		vanilla[i] = groupsExt[player][group][i];
}

u16 tagOf(const CUnit* unit) {
	if (unit == NULL)
		return 0;
	const u32 index = unit->getIndex();
	if (index < 1 || index > UNIT_ARRAY_LENGTH)
		return 0;
	return (u16)(index | (unit->targetOrderSpecial << 11));
}

CUnit* unitOfTag(u32 tag) {
	if (tag == 0)
		return NULL;
	CUnit* unit = CUnit::getFromIndex((u16)(tag & 0x7FF));
	if (unit == NULL || unit->sprite == NULL)
		return NULL;
	if (unit->mainOrderId == OrderId::Die && unit->mainOrderState == 1)
		return NULL;
	if (unit->targetOrderSpecial != (tag >> 11))
		return NULL;
	return unit;
}

u32 commandLength(const u8* cmd) {
	const u32 variable = variableCommandLength(cmd);
	return variable != 0 ? variable : COMMAND_LENGTHS[cmd[0]];
}

void clearAll() {
	for (u32 p = 0; p < PLAYERS; p++) {
		listClear(playersSel[p], SEL_MAX);
		mirrorPlayer(p);
	}
	listClear(activeSel, SEL_MAX);
	mirrorActive();
	listClear(clientSel, SEL_MAX);
	clientCount = 0;
	mirrorClient();
	listClear(lastSent, SEL_MAX);
	mirrorLastSent();
	listClear(reselect, SEL_MAX);
	memset(pending, 0, sizeof(pending));
	selectionPage = 0;
	//The vanilla groups are left alone: on a load they already hold the saved
	//ones, which the save chunk's reader turns back into groupsExt.
	memset(groupsExt, 0, sizeof(groupsExt));
}

} //selext
