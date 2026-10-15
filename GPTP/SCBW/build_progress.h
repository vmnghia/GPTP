//How far a building under construction is: the rules behind the progress
//bar drawn over it.
//
//Pure: no game memory. tests/build_progress_test.cpp builds it on its own.
#pragma once
#include "../types.h"

namespace buildprogress {

//Whether a unit is a building being made, by its main order: built (Terran),
//warped in (Protoss), grown from a Drone or morphing into its next form
//(Zerg). A unit with no order counts while it has build time left and is not
//completed.
//The order that starts a Zerg building's morph is not one: it runs for a
//frame, before the build time is set, and hands over to the order a growing
//Zerg building has (0x45D0D0).
bool isBeingBuilt(u32 orderId, u32 remainingBuildTime, bool completed);

//Whether a unit is an egg morphing into its next form (an Egg, Cocoon or
//Lurker Egg), by its order: Morph1, not completed, with the type it becomes
//in its build queue; it counts down that type's build time (0x4669E0). The
//unit that starts the order (a Larva, Hydralisk, Mutalisk) is still
//completed until the order's first run makes it the egg (0x45E019).
bool isMorphingInEgg(u32 orderId, bool completed, u32 queuedType);

//The unit type whose build time a unit counts down, by the game's own rule
//(0x4669E0, 0x45CD00): a Zerg building morphing into a Lair, Hive, Greater
//Spire, Sunken Colony or Spore Colony keeps its old type until it is done,
//with the new one in its build queue (queuedType: the queue's active slot).
//Anything else is its own type.
u32 typeBeingBuilt(u32 unitType, bool completed, u32 queuedType);

//Whether a unit is researching, upgrading or training. Each takes its order
//and the work the order is for (the tech, the upgrade, the unit in the build
//queue's active slot). Training is either of the game's two orders for
//making a unit inside another: a building's, and the one a Carrier builds
//Interceptors and a Reaver builds Scarabs by. No unit type is named: a unit
//that produces by either order is training.
//The order alone is not enough: a cancel takes the work away at once and
//leaves the order to end on its next run (0x453E30, 0x454280, 0x466A70), and
//so does the last unit of a queue when it is done (0x468420). In between the
//order says busy with nothing to do. A Carrier keeps its order for good.
bool isResearching(u32 orderId, u32 techType);
bool isUpgrading(u32 orderId, u32 upgradeType);
bool isTraining(u32 secondaryOrderId, u32 queuedType);

//Whose bars show: the local player's own; an ally's where the local player
//sees the unit (seen: out of the fog, and detected if cloaked); every
//player's in a replay; never an enemy's, nor the neutral players' (owner 8
//and up: a Vespene Geyser).
bool isShownFor(u32 owner, u32 localPlayer, bool inReplay, bool allied, bool seen);

//The bar's frame, its border included: 5 rows tall.
struct Frame {
	s32 left, top, right, bottom;
};
//The five queue boxes under a bar reach this far right of its left edge.
const s32 QUEUE_WIDTH = 40;
//The frame over a unit whose sprite is at (x, y) and whose box reaches
//boxLeft, boxUp and boxRight from there. Over a building it is as wide as
//the box and 24 px above it (an add-on's is 8 px right and 4 px down of
//that); over any other unit it is 16 px above. With queue boxes under it
//(withQueue) it is at least as wide as they are, centred on the unit.
Frame barFrame(s32 x, s32 y, s32 boxLeft, s32 boxUp, s32 boxRight, bool building, bool addon, bool withQueue);

//How wide the bar's fill is, 0 to width: elapsed out of timeCost. It stays
//inside the bar whatever the two say, and a time of 0 (the data can say so)
//counts as done.
s32 fillWidth(s32 elapsed, s32 timeCost, s32 width);

} //buildprogress
