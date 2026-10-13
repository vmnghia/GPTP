#include "smart_rally_hooks.h"
#include <SCBW/smart_rally.h>
#include <SCBW/selection_ext.h>
#include "../selection_ext/sel_synced.h"
#include <cstring>

namespace smartrally {

namespace {

WorkerRally workerRallies[UNIT_ARRAY_LENGTH];

u32 slotOf(const CUnit* unit) {
	return (u32)(unit - unitTable);
}

bool inTable(const CUnit* unit) {
	return unit >= unitTable && unit < unitTable + UNIT_ARRAY_LENGTH;
}

//Its right-click gathers: the units.dat right-click action.
bool gathersOnRightClick(const CUnit* unit) {
	const u8 action = units_dat::RightClickAction[unit->id];
	return action == RightClickActions::Harvest || action == RightClickActions::HarvestAndRepair;
}

//The game's 0x4E6BA0 (ECX = unit): a transport able to carry now. Not a
//hallucination, an Overlord only with Ventral Sacs, and units.dat space.
const u32 Func_UnitIsActiveTransport = 0x004E6BA0;
bool unitIsActiveTransport(CUnit* unit) {
	static u32 result;
	__asm {
		PUSHAD
		MOV ECX, unit
		CALL Func_UnitIsActiveTransport
		MOV result, EAX
		POPAD
	}
	return result != 0;
}

//A right-click from unit on target would load it: the right-click rule's
//test before EnterTransport (0x455973-0x45598B).
bool rightClickLoads(CUnit* unit, CUnit* target) {
	return unitIsActiveTransport(target) && scbw::canBeEnteredBy(target, unit);
}

bool workerRallyTargeting;	//local: the last rally button pressed was Set Worker Rally Point
u8 workerRallyCommand[WORKER_RALLY_BYTES];

//As the game sets a rally on a unit (0x45620D): the building itself for
//none, at the target's position.
void setNormalRally(CUnit* building, CUnit* target) {
	if (target == NULL)
		target = building;
	building->rally.unit = target;
	building->rally.pt.x = target->sprite->position.x;
	building->rally.pt.y = target->sprite->position.y;
}

//A worker rally on a unit (at its position) or, target NULL, on (x, y).
void setWorkerRally(WorkerRally* rally, CUnit* target, u16 x, u16 y) {
	rally->set = true;
	rally->target = target;
	rally->x = target != NULL ? target->sprite->position.x : x;
	rally->y = target != NULL ? target->sprite->position.y : y;
}

} //unnamed namespace

void clearAll() {
	memset(workerRallies, 0, sizeof(workerRallies));
}

WorkerRally* workerRallyOf(CUnit* building) {
	if (building == NULL || !inTable(building) || !hasTwoRallies(building->id))
		return NULL;
	return &workerRallies[slotOf(building)];
}

void orderNewUnitToRally(CUnit* unit, CUnit* factory) {
	if (factory == NULL || !factory->isFactory())
		return;
	const bool worker = gathersOnRightClick(unit);
	const WorkerRally* const workerRally = workerRallyOf(factory);
	const RallySource source = rallySource(hasTwoRallies(factory->id), worker,
	                                       workerRally != NULL && workerRallyCounts(workerRally->set,
	                                                                                workerRally->target == factory));
	if (source == USE_NONE)
		return;

	CUnit* target;
	u16 x, y;
	if (source == USE_WORKER) {
		target = workerRally->target;
		x = workerRally->x;
		y = workerRally->y;
	}
	else {
		//Vanilla's: no rally while it is the building itself or at x 0.
		if (factory->rally.unit == factory || factory->rally.pt.x == 0)
			return;
		target = factory->rally.unit;
		x = factory->rally.pt.x;
		y = factory->rally.pt.y;
	}

	if (target != NULL) {
		const bool sameOwner = target->playerId == unit->playerId;
		const RallyAction action = rallyAction(worker, target->id, (target->status & UnitStatus::Completed) != 0,
		                                       sameOwner, sameOwner && rightClickLoads(unit, target));
		if (action == ACT_MINERALS) {
			unit->orderTo(OrderId::MoveToMinerals, target);
			return;
		}
		if (action == ACT_GAS) {
			unit->orderTo(OrderId::HarvestGas1, target);
			return;
		}
		if (action == ACT_ENTER) {
			unit->orderTo(OrderId::EnterTransport, target);
			return;
		}
		//Vanilla: follow a unit of the owner's.
		if (sameOwner) {
			unit->orderTo(OrderId::Follow, target);
			return;
		}
	}
	unit->orderTo(OrderId::Move, x, y);
}

void unitDying(CUnit* unit) {
	if (inTable(unit))
		memset(&workerRallies[slotOf(unit)], 0, sizeof(WorkerRally));
}

void referenceCleared(CUnit* holder, CUnit* dying) {
	if (!inTable(holder))
		return;
	WorkerRally& rally = workerRallies[slotOf(holder)];
	if (!rally.set || rally.target != dying)
		return;
	//As 0x49E64B-0x49E666: the point where the dying unit's sprite is.
	rally.target = NULL;
	rally.x = dying->sprite->position.x;
	rally.y = dying->sprite->position.y;
}

void rightClickRally(CUnit* building, CUnit* target) {
	WorkerRally* const workerRally = workerRallyOf(building);
	if (workerRally == NULL) {
		setNormalRally(building, target);
		return;
	}
	const bool hasTarget = target != NULL;
	const RightClickSets sets = rightClickSets(
		hasTarget,
		hasTarget && isRallyResource(target->id, target->playerId == building->playerId),
		hasTarget && workerRally->set && workerRally->target == target);
	if (sets == SETS_WORKER || sets == SETS_BOTH)
		setWorkerRally(workerRally, target, 0, 0);
	if (sets == SETS_NORMAL || sets == SETS_BOTH)
		setNormalRally(building, target);
}

void noteRallyButton(u32 actVar) {
	workerRallyTargeting = (u16)actVar == 1;
}

u8* workerRallySendAs(const u8* targeted) {
	const bool forWorkers = workerRallyTargeting;
	workerRallyTargeting = false;
	if (!forWorkers || !toWorkerRallyCommand(targeted, workerRallyCommand))
		return NULL;
	return workerRallyCommand;
}

void recvWorkerRally(const u8* packet) {
	const u16 x = *(const u16*)(packet + 1);
	const u16 y = *(const u16*)(packet + 3);
	CUnit* const target = selext::unitOfTag(*(const u16*)(packet + 5));	//NULL: the ground, or it died
	*selectionIndexStart = 0;
	for (CUnit* unit = selsync::nextSelected(); unit != NULL; unit = selsync::nextSelected()) {
		WorkerRally* const rally = workerRallyOf(unit);
		if (rally != NULL && unit->playerId == *ACTIVE_NATION_ID)
			setWorkerRally(rally, target, x, y);
	}
}

void setStartRally(CUnit* base, CUnit* mineral) {
	WorkerRally* const workerRally = workerRallyOf(base);
	if (workerRally != NULL)
		setWorkerRally(workerRally, mineral, 0, 0);
	else if (base->isFactory())
		setNormalRally(base, mineral);
}

void saveWorkerRallies(SavedWorkerRally* out) {
	for (u32 i = 0; i < UNIT_ARRAY_LENGTH; i++) {
		const WorkerRally& rally = workerRallies[i];
		out[i].targetTag = rally.set ? selext::tagOf(rally.target) : 0;
		out[i].x = rally.x;
		out[i].y = rally.y;
		out[i].set = rally.set ? 1 : 0;
		out[i].unused = 0;
	}
}

void loadWorkerRallies(const SavedWorkerRally* in) {
	for (u32 i = 0; i < UNIT_ARRAY_LENGTH; i++) {
		WorkerRally& rally = workerRallies[i];
		rally.set = in[i].set != 0;
		rally.target = in[i].targetTag != 0 ? selext::unitOfTag(in[i].targetTag) : NULL;
		rally.x = in[i].x;
		rally.y = in[i].y;
	}
}

} //smartrally
