//SC2-style smart-casting. For the spells listed in smart_cast.cpp, one unit
//of the selection casts per command, instead of every unit that can.
//
//The choice is made when the command is received (receive_command), from
//synced state only, so every computer and every replay makes the same one.
//See docs/selection.md section 6.
#pragma once
#include "../../SCBW/structures/CUnit.h"

namespace smartCast {

//Whether this order is cast by one unit at a time.
bool isSmartCastOrder(u32 orderId);

//Picks the caster among the units of the player's selection that could take
//the order (in selection order). Each selection starts a round: every cast
//goes to the unit with the most energy left (after spells it is already on its
//way to cast) that has not cast yet this round, ties going to the lower unit
//index. When all of them have cast, a new round starts. If none has enough
//energy, the one with the most gets it, so the usual "not enough energy" shows.
CUnit* pickCaster(u32 playerId, u32 orderId, CUnit* const* candidates, u32 count,
                  CUnit* const* selection, u32 selectionCount);

//Archon merges: narrows the selected Templar (count entries) to the pair
//standing closest together, among those not already on their way to merge,
//ties going to the lower unit indices. Returns 2, or 0 with no pair.
u32 keepClosestPair(CUnit** templars, u32 count, u32 mergeOrder);

//Forgets all rounds. Called at the start of each game.
void reset();

} //smartCast
