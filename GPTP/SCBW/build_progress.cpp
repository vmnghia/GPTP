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
	if (secondaryOrderId != OrderId::Train && secondaryOrderId != OrderId::TrainFighter)
		return false;
	return queuedType < UnitId::None;
}

bool isShownFor(u32 owner, u32 localPlayer, bool inReplay, bool allied, bool seen) {
	const u32 PLAYING_PLAYERS = 8;
	if (owner >= PLAYING_PLAYERS)
		return false;
	if (inReplay || owner == localPlayer)
		return true;
	return allied && seen;
}

Frame barFrame(s32 x, s32 y, s32 boxLeft, s32 boxUp, s32 boxRight, bool building, bool addon, bool withQueue) {
	const s32 ABOVE_BUILDING = 24, ABOVE_UNIT = 16, HEIGHT = 4;
	const s32 ADDON_RIGHT = 8, ADDON_DOWN = 4;

	Frame frame;
	frame.left = x - boxLeft;
	frame.right = x + boxRight;
	if (withQueue && frame.right - frame.left < QUEUE_WIDTH) {
		frame.left = x - QUEUE_WIDTH / 2;
		frame.right = frame.left + QUEUE_WIDTH;
	}
	frame.top = y - boxUp - (building ? ABOVE_BUILDING : ABOVE_UNIT);
	if (addon) {
		frame.left += ADDON_RIGHT;
		frame.right += ADDON_RIGHT;
		frame.top += ADDON_DOWN;
	}
	frame.bottom = frame.top + HEIGHT;
	return frame;
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
