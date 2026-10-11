#include "build_progress.h"
#include "enumerations/OrderId.h"
#include "enumerations/TechId.h"
#include "enumerations/UnitId.h"
#include "enumerations/UpgradeId.h"

namespace buildprogress {

bool isBeingBuilt(u32 orderId, u32 remainingBuildTime, bool completed) {
	switch (orderId) {
	case OrderId::BuildSelf1:		//Terran
	case OrderId::ZergBuildSelf:	//Zerg, grown from a Drone or morphing
	case OrderId::BuildSelf2:		//Protoss
		return true;
	case OrderId::Nothing2:
		return remainingBuildTime > 0 && !completed;
	default:
		return false;
	}
}

u32 typeBeingBuilt(u32 unitType, bool completed, u32 queuedType) {
	if (completed)
		return unitType;
	switch (queuedType) {
	case UnitId::ZergLair:
	case UnitId::ZergHive:
	case UnitId::ZergGreaterSpire:
	case UnitId::ZergSunkenColony:
	case UnitId::ZergSporeColony:
		return queuedType;
	default:
		return unitType;
	}
}

bool isResearching(u32 orderId, u32 techType) {
	return orderId == OrderId::ResearchTech && techType < TechId::None;
}

bool isUpgrading(u32 orderId, u32 upgradeType) {
	return orderId == OrderId::Upgrade && upgradeType < UpgradeId::None;
}

bool isTraining(u32 secondaryOrderId, u32 queuedType) {
	return secondaryOrderId == OrderId::Train && queuedType < UnitId::None;
}

s32 fillWidth(s32 elapsed, s32 timeCost, s32 width) {
	if (width <= 0)
		return 0;
	if (timeCost <= 0 || elapsed >= timeCost)
		return width;
	if (elapsed <= 0)
		return 0;
	return (s32)((long long)elapsed * width / timeCost);
}

} //buildprogress
