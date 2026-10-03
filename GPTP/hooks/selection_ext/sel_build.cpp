#include "sel_build.h"
#include "sel_exe.h"
#include "sel_subgroups.h"
#include <SCBW/selection_ext.h>
#include <cstring>

using namespace selext;

namespace selbuild {

u32 stamps[UNIT_ARRAY_LENGTH];
u32 lastStamp[8];
u32 paidMinerals[UNIT_ARRAY_LENGTH];
u32 paidGas[UNIT_ARRAY_LENGTH];
u16 paidCurrentType[UNIT_ARRAY_LENGTH];
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

const u16 NOT_PAID = 0xFFFF;

u32 mineralCost(u16 type) {
	return type < UNIT_TYPES ? units_dat::MineralCost[type] : 0;
}

u32 gasCost(u16 type) {
	return type < UNIT_TYPES ? units_dat::GasCost[type] : 0;
}

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
	//An SCV constructing holds a building too: queue behind it, never pull it off.
	if (holdsBuild(unit->mainOrderId)) {
		n++;
		*lastX = unit->orderTarget.pt.x;
		*lastY = unit->orderTarget.pt.y;
	}
	for (COrder* order = unit->orderQueueHead; order != NULL; order = order->next)
		if (holdsBuild(order->orderId)) {
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

bool readCommand(const u8* packet, bool forQueue) {
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
		//A Shift-queued build may also go to a worker already building (it
		//queues behind): vanilla's 0x48DBD0 refuses a busy one outright.
		const bool allowedNow = selexe::placeBuildingAllowed(unit, cmd.order, cmd.type);
		if (!ableToQueue(allowedNow,
		                 forQueue && holdsBuild(unit->mainOrderId),
		                 !allowedNow && unit->canMakeUnit(cmd.type, unit->playerId) != 0))
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
	if (!readCommand(packet, false))
		return;
	//Prefer a worker that isn't constructing (it would leave its building).
	static bool notConstructing[SEL_MAX];
	for (u32 i = 0; i < cmd.n; i++)
		notConstructing[i] = !holdsBuild(cmd.units[i]->mainOrderId);
	const int k = nearestPreferring(cmd.xs, cmd.ys, cmd.able, notConstructing, cmd.n, cmd.siteX, cmd.siteY);
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
	if (!readCommand(packet, true))
		return;
	static u32 builds[SEL_MAX];
	static s32 fromX[SEL_MAX], fromY[SEL_MAX];
	static bool isFree[SEL_MAX], recyclable[SEL_MAX];
	static u32 stampOf[SEL_MAX];
	for (u32 i = 0; i < cmd.n; i++) {
		CUnit* const unit = cmd.units[i];
		builds[i] = buildsOf(unit, &fromX[i], &fromY[i]);
		isFree[i] = cmd.able[i] && builds[i] == 0 && !droneCommitted(unit);	//not landing
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
	const u32 index = unit->getIndex() - 1;
	//Shift-placements are paid now (user: as SC2): never more than you can afford.
	const u32 costM = mineralCost(cmd.type), costG = gasCost(cmd.type);
	if (!canAfford(resources->minerals[player], resources->gas[player], costM, costG)) {
		if (resources->minerals[player] < (s32)costM)
			scbw::showErrorMessageWithSfx(player, STR_NOT_ENOUGH_MINERALS,
				SoundId::Zerg_Advisor_ZAdErr00_WAV_2 + *ADVISOR_RACE);
		else
			scbw::showErrorMessageWithSfx(player, STR_NOT_ENOUGH_GAS,
				SoundId::Zerg_Advisor_ZAdErr01_WAV + *ADVISOR_RACE);
		return;
	}
	//A worker with no build (mining, idle, moving), or a Drone (one at a
	//time), starts it now; a worker already building queues it.
	if (builds[k] == 0 || drones) {
		stamps[index] = ++lastStamp[player & 7];
		buildNow(unit, cmd.order, cmd.type, cmd.tiles);
		//Paid once vanilla's path gave the order (its own checks passed).
		if (isBuildOrder(unit->mainOrderId)
			&& unit->orderTarget.pt.x == cmd.siteX && unit->orderTarget.pt.y == cmd.siteY)
		{
			resources->minerals[player] -= costM;
			resources->gas[player] -= costG;
			paidMinerals[index] += costM;
			paidGas[index] += costG;
			paidCurrentType[index] = cmd.type;
		}
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
	stamps[index] = ++lastStamp[player & 7];
	resources->minerals[player] -= costM;
	resources->gas[player] -= costG;
	paidMinerals[index] += costM;
	paidGas[index] += costG;
	//The mark: set up when it becomes current, and prepaid.
	unit->performAnotherOrder(cmd.order, (s16)cmd.siteX, (s16)cmd.siteY, NULL,
	                          cmd.type | QUEUED_BUILD_MARK, NULL);
}

} //selbuild

namespace {

//What the unit has paid for but no longer holds goes back to its owner.
void reconcile(CUnit* unit, u32 index) {
	u32 heldM = 0, heldG = 0;
	u16* const current = &selbuild::paidCurrentType[index];
	if (*current != NOT_PAID) {
		if (holdsBuild(unit->mainOrderId)) {
			heldM += mineralCost(*current);
			heldG += gasCost(*current);
		}
		else
			*current = NOT_PAID;
	}
	for (COrder* order = unit->orderQueueHead; order != NULL; order = order->next)
		if (order->unitId != UnitId::None && (order->unitId & QUEUED_BUILD_MARK)) {
			heldM += mineralCost(order->unitId & ~QUEUED_BUILD_MARK);
			heldG += gasCost(order->unitId & ~QUEUED_BUILD_MARK);
		}
	u32 refundM, refundG;
	reconcilePaid(&selbuild::paidMinerals[index], &selbuild::paidGas[index], heldM, heldG,
	              &refundM, &refundG);
	resources->minerals[unit->playerId] += refundM;
	resources->gas[unit->playerId] += refundG;
}

} //unnamed namespace

namespace selbuild {

void arriving(CUnit* unit) {
	const u32 index = unit->getIndex() - 1;
	const u16 type = paidCurrentType[index];
	if (type == NOT_PAID)
		return;
	//Its cost goes back just before vanilla's own check spends it.
	const u32 costM = mineralCost(type), costG = gasCost(type);
	resources->minerals[unit->playerId] += costM;
	resources->gas[unit->playerId] += costG;
	paidMinerals[index] -= costM < paidMinerals[index] ? costM : paidMinerals[index];
	paidGas[index] -= costG < paidGas[index] ? costG : paidGas[index];
	paidCurrentType[index] = NOT_PAID;
}

void beforeOrder(CUnit* unit) {
	const u32 index = unit->getIndex() - 1;
	if (unit->orderUnitType != UnitId::None && (unit->orderUnitType & QUEUED_BUILD_MARK)) {
		const u16 type = unit->orderUnitType & ~QUEUED_BUILD_MARK;
		unit->orderUnitType = type;
		if (isBuildOrder(unit->mainOrderId) && type < UNIT_TYPES) {
			//A queued build starting: already paid; the supply check and the
			//build slot, as 0x48DE70 does at command time.
			paidCurrentType[index] = type;
			const u8 player = unit->playerId;
			MINERAL_COST[player] = units_dat::MineralCost[type];
			GAS_COST[player] = units_dat::GasCost[type];
			bool ok = selexe::hasSupplies(type, player);
			if (ok) {
				selexe::refundQueueSlots(unit);
				ok = selexe::fillBuildSlot(unit, type);
			}
			if (!ok)
				unit->orderToIdle();	//the next queued order, or idle (refunded below)
		}
	}
	if (paidMinerals[index] != 0 || paidGas[index] != 0)
		reconcile(unit, index);
}

void gaveUp(CUnit* unit) {
	const bool stuck = (unit->status & UnitStatus::Unmovable) != 0;
	const bool within = selexe::withinReach(unit, unit->orderTarget.pt.x, unit->orderTarget.pt.y, 128);
	if (showCantReach(stuck, within))
		selexe::showStatTextTo(STR_CANT_REACH_SITE, unit->playerId);
}

void reset() {
	memset(stamps, 0, sizeof(stamps));
	memset(lastStamp, 0, sizeof(lastStamp));
	memset(paidMinerals, 0, sizeof(paidMinerals));
	memset(paidGas, 0, sizeof(paidGas));
	memset(paidCurrentType, 0xFF, sizeof(paidCurrentType));
	chosenBuilder = NULL;
}

} //selbuild

namespace {

const u8* const SHIFT_HELD			= (const u8*)	0x00596A28;
const u32* const PLACING			= (const u32*)	0x00640880;
const u16* const PLACING_TYPE		= (const u16*)	0x0064088A;
const u8* const PLACING_ORDER		= (const u8*)	0x0064088D;
bool shiftPlacing;	//local: placing goes on after a Shift-placement

} //unnamed namespace

namespace selbuild {

bool sendAsQueued(u8* cmd) {
	if (!shiftQueues(*SHIFT_HELD != 0, cmd[1]))
		return false;
	cmd[0] = CMD_QUEUED_BUILD;
	shiftPlacing = true;
	return true;
}

bool keepsPlacing() {
	return shiftQueues(*SHIFT_HELD != 0, *PLACING_ORDER);
}

//The selected workers (the active subgroup, or the portrait), and whether
//any can place the building now.
u32 selectedWorkers(bool* anyCan) {
	static CUnit* members[SEL_MAX];
	u32 m = selsub::activeMembers(members);
	if (m == 0 && *activePortraitUnit != NULL) {
		members[0] = *activePortraitUnit;
		m = 1;
	}
	u32 workers = 0;
	*anyCan = false;
	for (u32 i = 0; i < m; i++) {
		if (units_dat::BaseProperty[members[i]->id] & UnitProperty::Worker)
			workers++;
		if (!*anyCan)
			*anyCan = selexe::placeBuildingAllowed(members[i], *PLACING_ORDER, *PLACING_TYPE);
	}
	return workers;
}

bool placementStillValid() {
	bool anyCan;
	const u32 workers = selectedWorkers(&anyCan);
	//Shift-queuing: the cursor stays while Shift is held and a worker is
	//selected (as SC2), even once the only builder has started building.
	return placingHolds(shiftPlacing && keepsPlacing(), anyCan, workers);
}

void frame() {
	if (!shiftPlacing)
		return;
	bool anyCan;
	if (*PLACING == 0)
		shiftPlacing = false;	//ended otherwise (Esc, right-click, a plain placement)
	else
	if (selectedWorkers(&anyCan) == 0) {
		shiftPlacing = false;	//no worker left (the last Drone became the building)
		selexe::cancelPlacement();
	}
	else
	if (!*SHIFT_HELD) {
		shiftPlacing = false;
		selexe::cancelPlacement();	//and the card back to basic, as SC2
	}
}

} //selbuild
