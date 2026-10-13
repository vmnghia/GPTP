#include "smart_rally.h"

namespace smartrally {

namespace {

//The game's ids (SCBW/enumerations/UnitId.h, OrderId.h).
const u16 REFINERY = 110, HATCHERY = 131, LAIR = 132, HIVE = 133, EXTRACTOR = 149, ASSIMILATOR = 157,
          MINERAL_FIELD_1 = 176, MINERAL_FIELD_3 = 178, VESPENE_GEYSER = 188;
const u8 TARGETED_ORDER = 0x15, RALLY_UNIT = 0x27, RALLY_POINT = 0x28;

bool isMineralField(u16 id) {
	return id >= MINERAL_FIELD_1 && id <= MINERAL_FIELD_3;
}

bool isGasBuilding(u16 id) {
	return id == REFINERY || id == EXTRACTOR || id == ASSIMILATOR;
}

} //unnamed namespace

bool hasTwoRallies(u16 buildingId) {
	return buildingId == HATCHERY || buildingId == LAIR || buildingId == HIVE;
}

bool isRallyResource(u16 targetId, bool sameOwner) {
	return isMineralField(targetId) || targetId == VESPENE_GEYSER || (isGasBuilding(targetId) && sameOwner);
}

RightClickSets rightClickSets(bool hasTarget, bool targetIsResource, bool workerRallyOnTarget) {
	if (!hasTarget || !targetIsResource)
		return SETS_NORMAL;
	return workerRallyOnTarget ? SETS_BOTH : SETS_WORKER;
}

RallySource rallySource(bool twoRallies, bool unitIsWorker, bool workerRallySet) {
	if (!twoRallies || !unitIsWorker)
		return USE_NORMAL;
	return workerRallySet ? USE_WORKER : USE_NONE;
}

bool workerRallyCounts(bool set, bool onBuildingItself) {
	return set && !onBuildingItself;
}

RallyAction rallyAction(bool gathersOnRightClick, u16 targetId, bool targetCompleted,
                        bool sameOwner, bool rightClickLoads) {
	if (gathersOnRightClick && isMineralField(targetId))
		return ACT_MINERALS;
	if (gathersOnRightClick && isGasBuilding(targetId) && targetCompleted && sameOwner)
		return ACT_GAS;
	if (rightClickLoads)
		return ACT_ENTER;
	return ACT_VANILLA;
}

bool toWorkerRallyCommand(const u8* targeted, u8* out) {
	if (targeted[0] != TARGETED_ORDER || (targeted[9] != RALLY_UNIT && targeted[9] != RALLY_POINT))
		return false;
	out[0] = CMD_WORKER_RALLY;
	for (u32 i = 1; i < WORKER_RALLY_BYTES; i++)
		out[i] = targeted[i];	//x, y and the target's tag sit at the same places
	return true;
}

} //smartrally
