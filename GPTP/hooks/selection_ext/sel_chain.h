//Which units hold an order their player queued with Shift.
//
//The game keeps a plain order that has to wait, and its own clean-up orders,
//in the same queue as Shift-queued ones, and marks none of them. The order
//lines show only the player's plan, so the plugin notes it where a command's
//order goes into the queue.
//
//Local, for drawing only: nothing here may decide an order. It is not saved:
//after a load, a chain queued before the save has no lines until the player
//queues again.
#pragma once
#include <SCBW/api.h>

namespace selchain {

//A game starts.
void reset();
//A command's order goes behind the unit's others (the call at 0x4755C8), or
//smart-build queues a building.
void queued(const CUnit* unit);
//A command's order replaces the unit's others (the call at 0x4755E3), or
//smart-build gives a plain build.
void replaced(const CUnit* unit);
//Whether the unit holds a Shift-queued order.
bool holds(const CUnit* unit);
//Once per frame for a unit on the map: a chain that has run out is forgotten.
void sweep(const CUnit* unit);

} //selchain
