//Which lines show a selected unit's orders: from the unit to its current
//order's target, then on through the orders queued behind it.
//
//Pure: no game memory. tests/order_lines_test.cpp builds it on its own.
#pragma once
#include "../types.h"

namespace orderlines {

//An order as the lines see it: its id and its target's position. The game
//gives an order without a target (Stop, Siege, the idle order queued behind
//an SCV's construction) the position (0, 0).
struct Stop {
	u32 orderId;
	s32 x, y;
};
bool hasPosition(const Stop& stop);

//What a line is drawn as. ATTACK is any order that attacks, on the ground or
//on a unit: unit types have their own orders for it (units.dat).
enum Kind { MOVE, ATTACK, PATROL };
Kind kindOf(u32 orderId);

//One line, drawn as the order it leads to.
struct Leg {
	s32 fromX, fromY, toX, toY;
	Kind kind;
};

//The lines of a unit at (unitX, unitY) with the order current and count
//orders queued behind it, written to out (count + 1 legs at most). Returns
//how many. An order without a position gets no line and the chain goes on
//past it. Nothing shows unless a queued order has a position: a unit's
//current order alone has no line.
u32 legsOf(s32 unitX, s32 unitY, const Stop& current, const Stop* queued, u32 count, Leg* out);

} //orderlines
