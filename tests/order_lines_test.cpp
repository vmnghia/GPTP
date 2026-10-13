//Host test of the order lines' pure logic (SCBW/order_lines.cpp). Build and
//run with order_lines_test.bat (MSVC x86); it needs no game.
#include <SCBW/order_lines.h>
#include <cstdio>

using namespace orderlines;

namespace {

u32 failures;
u32 firstLine;

void check(bool ok, u32 line) {
	if (!ok) {
		if (failures == 0)
			firstLine = line;
		failures++;
	}
}

#define CHECK(x) check((x), __LINE__)

//The game's order ids (SCBW/enumerations/OrderId.h).
const u32 STOP = 0x01, PLAYER_GUARD = 0x03, MOVE_ORDER = 0x06, ATTACK_UNIT = 0x0A, ATTACK_MOVE = 0x0E,
          CONSTRUCTING = 0x21, SIEGE = 0x62, PATROL_ORDER = 0x98;

Stop stop(u32 orderId, s32 x, s32 y) {
	const Stop s = {orderId, x, y};
	return s;
}

bool is(const Leg& leg, s32 fromX, s32 fromY, s32 toX, s32 toY, Kind kind) {
	return leg.fromX == fromX && leg.fromY == fromY && leg.toX == toX && leg.toY == toY && leg.kind == kind;
}

//A leg no test expects: what out holds where nothing was written.
const Leg UNTOUCHED = {-1, -1, -1, -1, PATROL};

void clear(Leg* legs, u32 n) {
	for (u32 i = 0; i < n; ++i)
		legs[i] = UNTOUCHED;
}

} //unnamed namespace

int main() {
	Leg legs[8];

	//a position: anything but (0, 0); the map's left and top edges count.
	CHECK(hasPosition(stop(MOVE_ORDER, 300, 400)));
	CHECK(!hasPosition(stop(STOP, 0, 0)));
	CHECK(hasPosition(stop(MOVE_ORDER, 0, 400)));
	CHECK(hasPosition(stop(MOVE_ORDER, 300, 0)));

	//an order on a unit: the line follows the unit while the player sees it
	//(the order keeps the position the unit had when the order was given).
	{
		const Stop seen = stopOf(ATTACK_UNIT, 300, 400, true, 350, 480);
		CHECK(seen.orderId == ATTACK_UNIT && seen.x == 350 && seen.y == 480);
		//out of sight (fog, cloak, dead): the position the player last gave.
		const Stop unseen = stopOf(ATTACK_UNIT, 300, 400, false, 350, 480);
		CHECK(unseen.orderId == ATTACK_UNIT && unseen.x == 300 && unseen.y == 400);
		//an order without a target stays one.
		CHECK(!hasPosition(stopOf(STOP, 0, 0, false, 0, 0)));
	}

	//kinds: an attack on the ground or on a unit, patrol, and everything else.
	CHECK(kindOf(ATTACK_MOVE) == ATTACK);
	CHECK(kindOf(ATTACK_UNIT) == ATTACK);
	//kinds: units.dat gives unit types their own order for attacking a unit.
	//A Siege Tank's or a Goliath's (a body with a turret) is StayinRange, a
	//sieged tank's WatchTarget, a Lurker's AttackFixedRange; the Carrier,
	//the Reaver and the Infested Terran have theirs. All are attacks.
	{
		const u32 attacks[] = {0x08, 0x09, 0x0B, 0x0C, 0x15, 0x64, 0x35, 0x36, 0x38, 0x3B, 0x3C, 0x3D,
		                       0x86, 0x87, 0x9D, 0x9E, 0xB1};
		for (u32 i = 0; i < sizeof(attacks) / sizeof(attacks[0]); ++i)
			CHECK(kindOf(attacks[i]) == ATTACK);
	}
	//kinds: not attacks, though near them in the list: a turret's own order,
	//the idle orders of the Carrier and the Reaver, Follow, Hold Position.
	{
		const u32 others[] = {0x16, 0x32, 0x3A, 0x31, 0x6B, PLAYER_GUARD, CONSTRUCTING, SIEGE};
		for (u32 i = 0; i < sizeof(others) / sizeof(others[0]); ++i)
			CHECK(kindOf(others[i]) == MOVE);
	}
	CHECK(kindOf(PATROL_ORDER) == PATROL);
	CHECK(kindOf(MOVE_ORDER) == MOVE);
	CHECK(kindOf(STOP) == MOVE);

	//nothing queued: no line, even with a current order on its way.
	clear(legs, 8);
	CHECK(legsOf(100, 100, stop(MOVE_ORDER, 500, 100), NULL, 0, false, legs) == 0);
	CHECK(is(legs[0], -1, -1, -1, -1, PATROL));

	//moving to A with a move to B queued: the unit to A, A to B.
	{
		const Stop queued[] = {stop(MOVE_ORDER, 500, 400)};
		clear(legs, 8);
		CHECK(legsOf(100, 100, stop(MOVE_ORDER, 500, 100), queued, 1, false, legs) == 2);
		CHECK(is(legs[0], 100, 100, 500, 100, MOVE));
		CHECK(is(legs[1], 500, 100, 500, 400, MOVE));
		CHECK(is(legs[2], -1, -1, -1, -1, PATROL));
	}

	//an SCV constructing, with only the idle order the game queues behind
	//it: no line (it went to the map's top-left corner).
	{
		const Stop queued[] = {stop(PLAYER_GUARD, 0, 0)};
		clear(legs, 8);
		CHECK(legsOf(900, 700, stop(CONSTRUCTING, 910, 690), queued, 1, false, legs) == 0);
		CHECK(is(legs[0], -1, -1, -1, -1, PATROL));
	}

	//a tank going into siege mode (no target) with a move queued: one line,
	//from the unit to the move, none by way of the corner.
	{
		const Stop queued[] = {stop(MOVE_ORDER, 500, 400)};
		clear(legs, 8);
		CHECK(legsOf(100, 100, stop(SIEGE, 0, 0), queued, 1, false, legs) == 1);
		CHECK(is(legs[0], 100, 100, 500, 400, MOVE));
	}

	//an order without a target first in the queue: the chain goes on past it.
	{
		const Stop queued[] = {stop(PLAYER_GUARD, 0, 0), stop(MOVE_ORDER, 500, 400)};
		clear(legs, 8);
		CHECK(legsOf(900, 700, stop(CONSTRUCTING, 910, 690), queued, 2, false, legs) == 2);
		CHECK(is(legs[0], 900, 700, 910, 690, MOVE));
		CHECK(is(legs[1], 910, 690, 500, 400, MOVE));
	}

	//an order without a target in the middle of the queue: the next line
	//starts where the last one ended.
	{
		const Stop queued[] = {stop(MOVE_ORDER, 500, 400), stop(SIEGE, 0, 0), stop(MOVE_ORDER, 800, 400)};
		clear(legs, 8);
		CHECK(legsOf(100, 100, stop(MOVE_ORDER, 500, 100), queued, 3, false, legs) == 3);
		CHECK(is(legs[0], 100, 100, 500, 100, MOVE));
		CHECK(is(legs[1], 500, 100, 500, 400, MOVE));
		CHECK(is(legs[2], 500, 400, 800, 400, MOVE));
		CHECK(is(legs[3], -1, -1, -1, -1, PATROL));
	}

	//a target on the map's left or top edge (one coordinate 0) is a target
	//like any other: the chain does not stop there.
	{
		const Stop queued[] = {stop(MOVE_ORDER, 0, 400), stop(MOVE_ORDER, 300, 0), stop(MOVE_ORDER, 800, 400)};
		clear(legs, 8);
		CHECK(legsOf(100, 100, stop(MOVE_ORDER, 500, 100), queued, 3, false, legs) == 4);
		CHECK(is(legs[1], 500, 100, 0, 400, MOVE));
		CHECK(is(legs[2], 0, 400, 300, 0, MOVE));
		CHECK(is(legs[3], 300, 0, 800, 400, MOVE));
	}

	//a plain move given while the unit is in an attack it cannot break off
	//yet: the game keeps the move at the head of the queue until the
	//animation is over, and marks the unit as leaving its order. That is no
	//Shift chain: no lines (it showed red to the target, green on to the
	//move).
	{
		const Stop queued[] = {stop(MOVE_ORDER, 500, 400)};
		clear(legs, 8);
		CHECK(legsOf(100, 100, stop(ATTACK_UNIT, 180, 100), queued, 1, true, legs) == 0);
		CHECK(is(legs[0], -1, -1, -1, -1, PATROL));
		//the same orders as a Shift chain show.
		CHECK(legsOf(100, 100, stop(ATTACK_UNIT, 180, 100), queued, 1, false, legs) == 2);
		CHECK(is(legs[0], 100, 100, 180, 100, ATTACK));
		CHECK(is(legs[1], 180, 100, 500, 400, MOVE));
	}

	//each line is drawn as the order it leads to.
	{
		const Stop queued[] = {stop(ATTACK_MOVE, 500, 400), stop(PATROL_ORDER, 800, 400),
		                       stop(MOVE_ORDER, 800, 100)};
		clear(legs, 8);
		CHECK(legsOf(100, 100, stop(MOVE_ORDER, 500, 100), queued, 3, false, legs) == 4);
		CHECK(legs[0].kind == MOVE);
		CHECK(legs[1].kind == ATTACK);
		CHECK(legs[2].kind == PATROL);
		CHECK(legs[3].kind == MOVE);
	}
	{
		const Stop queued[] = {stop(MOVE_ORDER, 500, 400)};
		clear(legs, 8);
		CHECK(legsOf(100, 100, stop(ATTACK_MOVE, 500, 100), queued, 1, false, legs) == 2);
		CHECK(legs[0].kind == ATTACK);
		CHECK(legs[1].kind == MOVE);
	}

	//a rally point: set when new units would be sent to it (0x466F50).
	CHECK(rallyIsSet(false, 300));
	//the building is its own rally unit: none set. The game puts that back
	//on lift-off and leaves the old point where it was.
	CHECK(!rallyIsSet(true, 300));
	//never set: the point is (0, 0), and the game only looks at x.
	CHECK(!rallyIsSet(false, 0));
	CHECK(!rallyIsSet(true, 0));

	if (failures != 0) {
		printf("FAIL: %u checks (first at order_lines_test.cpp line %u)\n", failures, firstLine);
		return 1;
	}
	printf("PASS\n");
	return 0;
}
