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

//The unit type whose build time a unit counts down, by the game's own rule
//(0x4669E0, 0x45CD00): a Zerg building morphing into a Lair, Hive, Greater
//Spire, Sunken Colony or Spore Colony keeps its old type until it is done,
//with the new one in its build queue (queuedType: the queue's active slot).
//Anything else is its own type.
u32 typeBeingBuilt(u32 unitType, bool completed, u32 queuedType);

//How wide the bar's fill is, 0 to width: elapsed out of timeCost. It stays
//inside the bar whatever the two say, and a time of 0 (the data can say so)
//counts as done.
s32 fillWidth(s32 elapsed, s32 timeCost, s32 width);

} //buildprogress
