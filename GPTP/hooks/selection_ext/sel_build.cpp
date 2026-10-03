#include "sel_build.h"
#include "sel_exe.h"
#include "sel_subgroups.h"
#include <SCBW/selection_ext.h>
#include <cstring>

using namespace selext;

namespace selbuild {

u32 stamps[UNIT_ARRAY_LENGTH];
u32 lastStamp[8];
CUnit* chosenBuilder;

} //selbuild

namespace {

const u8 ORDER_DRONE_START_BUILD	= 0x19;
const u8 ORDER_DRONE_BUILD			= 0x1A;
const u8 ORDER_BUILD_TERRAN			= 0x1E;
const u8 ORDER_BUILD_PROTOSS1		= 0x1F;
const u8 ORDER_DRONE_LAND			= 0x46;
const u32 STR_WAYPOINTS_FULL		= 0x367;	//"Unit's waypoint list is full."
const u32 STR_LOW_ON_ORDERS			= 0x369;	//"Running low on orders, ..."
const u32 STR_CANT_REACH_SITE		= 0x35E;	//"Couldn't reach the building site."
const u32 STR_NOT_ENOUGH_MINERALS	= 0x352;
const u32 STR_NOT_ENOUGH_GAS		= 0x353;
const u32 MAX_QUEUED_ORDERS			= 8;
const u32 MAX_ORDERS_IN_USE			= 1800;
const u32* const ORDERS_IN_USE		= (const u32*)	0x00641698;
s32* const MINERAL_COST				= (s32*)		0x006CA51C;	//[8], 0x48DE70's
s32* const GAS_COST					= (s32*)		0x006CA4EC;
const u8* const ADVISOR_RACE		= (const u8*)	0x0057F1E2;

bool isBuildOrder(u32 order) {
	return order == ORDER_DRONE_START_BUILD || order == ORDER_BUILD_TERRAN || order == ORDER_BUILD_PROTOSS1;
}

//The site's centre in pixels, as 0x48E010 computes it.
void siteCentre(u16 tileX, u16 tileY, u16 type, s32* x, s32* y) {
	s32 w = (s16)units_dat::BuildingDimensions[type].x;
	s32 h = (s16)units_dat::BuildingDimensions[type].y;
	if (w < 0)
		w++;
	if (h < 0)
		h++;
	*x = tileX * 32 + w / 2;
	*y = tileY * 32 + h / 2;
}

//Build orders a unit holds (current and queued), and the site of the last.
u32 buildsOf(CUnit* unit, s32* lastX, s32* lastY) {
	u32 n = 0;
	*lastX = unit->position.x;
	*lastY = unit->position.y;
	if (isBuildOrder(unit->mainOrderId)) {
		n++;
		*lastX = unit->orderTarget.pt.x;
		*lastY = unit->orderTarget.pt.y;
	}
	for (COrder* order = unit->orderQueueHead; order != NULL; order = order->next)
		if (isBuildOrder(order->orderId)) {
			n++;
			*lastX = order->target.pt.x;
			*lastY = order->target.pt.y;
		}
	return n;
}

//0x48DE70's test for a Drone already landing or morphing.
bool droneCommitted(CUnit* unit) {
	return unit->mainOrderId == ORDER_DRONE_BUILD
		|| (unit->mainOrderId == ORDER_DRONE_LAND
			&& (unit->status & (UnitStatus::NoBrkCodeStart | UnitStatus::CanNotReceiveOrders)));
}

//The command's player's selection (synced: through the iterator).
u32 collectSelection(CUnit** units) {
	u32 n = 0;
	*selectionIndexStart = 0;
	for (CUnit* unit = getActivePlayerNextSelection(); unit != NULL && n < SEL_MAX;
		 unit = getActivePlayerNextSelection())
		units[n++] = unit;
	return n;
}

//Starts a build now with builder, through vanilla's path.
void buildNow(CUnit* builder, u8 order, u16 type, u32 tiles) {
	selbuild::chosenBuilder = builder;
	selexe::placeBuilding(order, type, tiles);
	selbuild::chosenBuilder = NULL;
}

//The command, its selection and which units can build it there (rule 1:
//vanilla's placement check per unit, reaching the site included).
struct BuildCommand {
	u8 order;
	u16 tileX, tileY, type;
	u32 tiles;
	s32 siteX, siteY;
	u32 n;
	u32 firstCode;	//a failing placement code to show if none can
	CUnit* units[SEL_MAX];
	bool able[SEL_MAX];
	s32 xs[SEL_MAX], ys[SEL_MAX];
};
BuildCommand cmd;

bool readCommand(const u8* packet) {
	cmd.order = packet[1];
	cmd.tileX = *(const u16*)(packet + 2);
	cmd.tileY = *(const u16*)(packet + 4);
	cmd.type = *(const u16*)(packet + 6);
	cmd.tiles = *(const u32*)(packet + 2);
	if (cmd.type >= UNIT_TYPES || cmd.tileX >= mapTileSize->width || cmd.tileY >= mapTileSize->height)
		return false;
	siteCentre(cmd.tileX, cmd.tileY, cmd.type, &cmd.siteX, &cmd.siteY);
	cmd.n = collectSelection(cmd.units);
	cmd.firstCode = 0;
	for (u32 i = 0; i < cmd.n; i++) {
		CUnit* const unit = cmd.units[i];
		cmd.xs[i] = unit->position.x;
		cmd.ys[i] = unit->position.y;
		cmd.able[i] = false;
		if (!selexe::placeBuildingAllowed(unit, cmd.order, cmd.type))
			continue;
		const u32 code = selexe::placementCheck(unit, unit->playerId, cmd.tileX, cmd.tileY, cmd.type);
		if (code != 0) {
			if (cmd.firstCode == 0)
				cmd.firstCode = code;
			continue;
		}
		cmd.able[i] = true;
	}
	return true;
}

void showNoneCould() {
	if (cmd.firstCode != 0)
		selexe::placementMessage(cmd.firstCode);
}

} //unnamed namespace

namespace selbuild {

void recvBuild(const u8* packet) {
	if (!readCommand(packet))
		return;
	const int k = nearestIndex(cmd.xs, cmd.ys, cmd.able, cmd.n, cmd.siteX, cmd.siteY);
	if (k < 0) {
		showNoneCould();
		return;
	}
	buildNow(cmd.units[k], cmd.order, cmd.type, cmd.tiles);
}

void recvQueuedBuild(const u8* packet) {
	if (!isBuildOrder(packet[1])) {
		recvBuild(packet);	//an addon or a landing: never queued
		return;
	}
	if (!readCommand(packet))
		return;
	static u32 builds[SEL_MAX];
	static s32 fromX[SEL_MAX], fromY[SEL_MAX];
	static bool isFree[SEL_MAX], recyclable[SEL_MAX];
	static u32 stampOf[SEL_MAX];
	for (u32 i = 0; i < cmd.n; i++) {
		CUnit* const unit = cmd.units[i];
		builds[i] = buildsOf(unit, &fromX[i], &fromY[i]);
		isFree[i] = cmd.able[i] && builds[i] == 0;
		recyclable[i] = cmd.able[i] && builds[i] > 0 && !droneCommitted(unit);
		stampOf[i] = stamps[unit->getIndex() - 1];
	}
	const bool drones = cmd.order == ORDER_DRONE_START_BUILD;
	const int k = drones
		? pickDrone(isFree, recyclable, stampOf, cmd.xs, cmd.ys, cmd.n, cmd.siteX, cmd.siteY)
		: pickBalanced(builds, fromX, fromY, cmd.able, cmd.n, cmd.siteX, cmd.siteY);
	if (k < 0) {
		showNoneCould();
		return;
	}
	CUnit* const unit = cmd.units[k];
	const u8 player = unit->playerId;
	//A worker with no build (mining, idle, moving), or a Drone (one at a
	//time), starts it now; a worker already building queues it.
	if (builds[k] == 0 || drones) {
		stamps[unit->getIndex() - 1] = ++lastStamp[player & 7];
		buildNow(unit, cmd.order, cmd.type, cmd.tiles);
		return;
	}
	if (unit->orderQueueCount >= MAX_QUEUED_ORDERS) {
		selexe::showStatTextTo(STR_WAYPOINTS_FULL, player);
		return;
	}
	if (*ORDERS_IN_USE >= MAX_ORDERS_IN_USE) {
		selexe::showStatTextTo(STR_LOW_ON_ORDERS, player);
		return;
	}
	stamps[unit->getIndex() - 1] = ++lastStamp[player & 7];
	unit->performAnotherOrder(cmd.order, (s16)cmd.siteX, (s16)cmd.siteY, NULL,
	                          cmd.type | QUEUED_BUILD_MARK, NULL);
}

void beforeOrder(CUnit* unit) {
	if (!(unit->orderUnitType & QUEUED_BUILD_MARK))
		return;
	const u16 type = unit->orderUnitType & ~QUEUED_BUILD_MARK;
	unit->orderUnitType = type;
	if (!isBuildOrder(unit->mainOrderId) || type >= UNIT_TYPES)
		return;
	//0x48DE70's command-time checks, now that the build starts.
	const u8 player = unit->playerId;
	MINERAL_COST[player] = units_dat::MineralCost[type];
	GAS_COST[player] = units_dat::GasCost[type];
	bool ok = selexe::hasSupplies(type, player);
	if (ok && resources->minerals[player] < MINERAL_COST[player]) {
		scbw::showErrorMessageWithSfx(player, STR_NOT_ENOUGH_MINERALS,
			SoundId::Zerg_Advisor_ZAdErr00_WAV_2 + *ADVISOR_RACE);
		ok = false;
	}
	else
	if (ok && resources->gas[player] < GAS_COST[player]) {
		scbw::showErrorMessageWithSfx(player, STR_NOT_ENOUGH_GAS,
			SoundId::Zerg_Advisor_ZAdErr01_WAV + *ADVISOR_RACE);
		ok = false;
	}
	if (ok) {
		selexe::refundQueueSlots(unit);
		ok = selexe::fillBuildSlot(unit, type);
	}
	if (!ok)
		unit->orderToIdle();	//the next queued order, or idle
}

void gaveUp(CUnit* unit) {
	if (!selexe::withinReach(unit, unit->orderTarget.pt.x, unit->orderTarget.pt.y, 128))
		selexe::showStatTextTo(STR_CANT_REACH_SITE, unit->playerId);
}

void reset() {
	memset(stamps, 0, sizeof(stamps));
	memset(lastStamp, 0, sizeof(lastStamp));
	chosenBuilder = NULL;
}

} //selbuild

namespace {

const u8* const SHIFT_HELD			= (const u8*)	0x00596A28;
const u32* const PLACING			= (const u32*)	0x00640880;
const u16* const PLACING_TYPE		= (const u16*)	0x0064088A;
const u8* const PLACING_ORDER		= (const u8*)	0x0064088D;
bool shiftPlacing;	//local: placing goes on after a Shift-placement
u16* const SPECIAL_BUTTON_SET		= (u16*)		0x0068C1C8;	//a build menu, Cancel, ...
const u16 NO_BUTTON_SET				= 0xE4;

//Shows the last build menu as its button would (0x459AF0: the special set,
//then 0x4599A0).
void showLastSubmenu() {
	if (selbuild::lastSubmenu == NO_BUTTON_SET)
		return;
	*SPECIAL_BUTTON_SET = selbuild::lastSubmenu;
	selexe::refreshButtonSet();
}

} //unnamed namespace

namespace selbuild {

u16 lastSubmenu = NO_BUTTON_SET;

void afterQueuedSend() {
	showLastSubmenu();
}

bool sendAsQueued(u8* cmd) {
	if (!*SHIFT_HELD || !isBuildOrder(cmd[1]))
		return false;
	cmd[0] = CMD_QUEUED_BUILD;
	shiftPlacing = true;
	return true;
}

bool placementStillValid() {
	static CUnit* members[SEL_MAX];
	const u32 m = selsub::activeMembers(members);
	if (m == 0)
		return *activePortraitUnit != NULL
			&& selexe::placeBuildingAllowed(*activePortraitUnit, *PLACING_ORDER, *PLACING_TYPE);
	for (u32 i = 0; i < m; i++)
		if (selexe::placeBuildingAllowed(members[i], *PLACING_ORDER, *PLACING_TYPE))
			return true;
	return false;
}

void frame() {
	if (!shiftPlacing)
		return;
	if (*PLACING == 0)
		shiftPlacing = false;	//ended otherwise (Esc, right-click, a plain placement)
	else
	if (!*SHIFT_HELD) {
		shiftPlacing = false;
		selexe::cancelPlacement();
		showLastSubmenu();
	}
}

} //selbuild
