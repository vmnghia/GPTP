#include "order_lines.h"
#include "enumerations/OrderId.h"

namespace orderlines {

bool hasPosition(const Stop& stop) {
	return stop.x != 0 || stop.y != 0;
}

Kind kindOf(u32 orderId) {
	switch (orderId) {
	//The attack commands, on the ground or on a unit.
	case OrderId::Attack1:
	case OrderId::Attack2:
	case OrderId::AttackUnit:
	case OrderId::AttackTile:
	case OrderId::AttackMove:
	//units.dat's own "attack unit" orders: a body with a turret (Siege Tank,
	//Goliath), a sieged tank, the Lurker, the Carrier, the Reaver, the
	//Infested Terran. A right-click on an enemy queues these.
	case OrderId::StayinRange:
	case OrderId::WatchTarget:
	case OrderId::AttackFixedRange:
	case OrderId::CarrierAttack1:
	case OrderId::CarrierAttack2:
	case OrderId::CarrierFight:
	case OrderId::ReaverAttack1:
	case OrderId::ReaverAttack2:
	case OrderId::ReaverFight:
	case OrderId::SapUnit:
	case OrderId::SapLocation:
	//The Medic's attack-move, and the computer's.
	case OrderId::HealMove:
	case OrderId::AtkMoveEP:
	case OrderId::HarassMove:
		return ATTACK;
	case OrderId::Patrol:
		return PATROL;
	default:
		return MOVE;
	}
}

u32 legsOf(s32 unitX, s32 unitY, const Stop& current, const Stop* queued, u32 count, Leg* out) {
	bool queuedHasPosition = false;
	for (u32 i = 0; i < count; ++i)
		queuedHasPosition = queuedHasPosition || hasPosition(queued[i]);
	if (!queuedHasPosition)
		return 0;

	u32 legs = 0;
	s32 x = unitX, y = unitY;	//where the next line starts
	for (u32 i = 0; i <= count; ++i) {
		const Stop& stop = i == 0 ? current : queued[i - 1];
		if (!hasPosition(stop))
			continue;
		const Leg leg = {x, y, stop.x, stop.y, kindOf(stop.orderId)};
		out[legs++] = leg;
		x = stop.x;
		y = stop.y;
	}
	return legs;
}

} //orderlines
