#include "smart_rally_hooks.h"
#include <SCBW/smart_rally.h>
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
	                                       workerRally != NULL && workerRally->set);
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

} //smartrally
