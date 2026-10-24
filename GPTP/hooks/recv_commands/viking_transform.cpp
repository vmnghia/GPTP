#include "viking_transform.h"
#include <SCBW/viking_transform.h>
#include "../selection_ext/sel_synced.h"
#include "../selection_ext/sel_exe.h"

//The design (sc2grp docs/research/2026-10-23-viking-transform-scratch.md):
//a transforming Viking is always the ground form. Landing changes the type at
//its start (the spot is settled before the art descends), lift-off at its end;
//an interruption anywhere leaves a plain ground Viking. The art is the
//ground form's own: header 419's Unused1 (landing) and Unused2 (lift-off)
//play the morph's sets and end on `sigorder 1`.

//Temporary, for the first in-game tests: a line at each stage of the transform
//(printText is local only; nothing synced changes). Set to 0 once it works.
#define VIKING_DEBUG 1
#if VIKING_DEBUG
#include <cstdio>
#define VDBG(...) do { char vdbg[96]; sprintf_s(vdbg, __VA_ARGS__); scbw::printText(vdbg); } while (0)
#else
#define VDBG(...) do {} while (0)
#endif

namespace {

void replaceUnitWithType(CUnit* unit, u16 newUnitId);	//49FED0
void holdPosition(CUnit* unit);							//4EB5B0
void orderReturnToIdle(CUnit* unit);					//463770
void issueOrder(CUnit* unit, u32 orderId, Bool32 queued);	//4754F0

//The type change faces the new sprite north (0x49ED05 -> 0x496360, the
//design's A4): give the unit, and every image of its sprite, its facing back.
void restoreFacing(CUnit* unit, u8 direction) {
	unit->currentDirection1 = direction;
	unit->velocityDirection1 = direction;
	if (unit->sprite == NULL)
		return;
	for (CImage* image = unit->sprite->images.head; image != NULL; image = image->link.next)
		scbw::setImageDirection(image, (s8)direction);
}

//Whether a ground Viking may stand where the fighter is: a free, walkable
//spot within MAX_LANDING_NUDGE, by checkUnitCollisionPos (0x49D3E0, which
//tests terrain itself); moves it to the spot when there is one. Called with
//the unit already the ground form, so the test is its ground footprint.
//The search's collision callback (0x49CCE0) never skips the unit being
//placed, so the unit is first taken out of the unit finder, as Nydus does
//(prepareUnitMove hide, then setUnitPosition + refreshUnitAfterMove to the
//spot or back). Earlier builds refused every landing: the tile isWalkable
//bit (not the walkability units are placed by), then the unit colliding with
//its own footprint ("nearest spot too far", 2026-10-24).
//What the search found: land where it is (moved to the spot, at most
//MAX_LANDING_NUDGE away), a spot farther off (in @p away; the unit stays where
//it is), or none. With no area given, the search covers a box 128 px either
//side of the unit (0x49D3F2-0x49D418).
enum Landing { LAND_NONE, LAND_HERE, LAND_AWAY };

Landing settleLanding(CUnit* unit, Point16* away) {
	const Point16 at = unit->sprite->position;
	Point16 spot = at;
	scbw::prepareUnitMove(unit, true);
	scbw::setUnitPosition(unit, at.x, at.y);
	Landing found = LAND_NONE;
	if (!scbw::checkUnitCollisionPos(unit, &at, &spot, NULL, false, 0))
		VDBG("Viking: no free spot near %d,%d", at.x, at.y);
	else {
		const s32 dx = spot.x - at.x, dy = spot.y - at.y;
		found = dx * dx + dy * dy <= vikingtransform::MAX_LANDING_NUDGE * vikingtransform::MAX_LANDING_NUDGE
			? LAND_HERE : LAND_AWAY;
	}
	if (found == LAND_AWAY)
		*away = spot;
	if (found != LAND_HERE)
		spot = at;
	scbw::setUnitPosition(unit, spot.x, spot.y);
	scbw::refreshUnitAfterMove(unit);
	if (found == LAND_HERE)
		VDBG("Viking: landing at %d,%d (from %d,%d)", spot.x, spot.y, at.x, at.y);
	return found;
}

} //unnamed namespace

namespace vikingtransform {

void buttonPressed(u32 actVar, u8 queued) {
	actVar &= 0xFFFF;	//BUTTON.actVar is a u16, loaded into CX only (0x459918)
	u8 toForm;
	if (actVar == ACTVAR_TO_ASSAULT)
		toForm = (u8)VIKING_ASSAULT;
	else if (actVar == ACTVAR_TO_FIGHTER)
		toForm = (u8)VIKING_FIGHTER;
	else
		return;
	VDBG("Viking: button 0x%X, to form %d%s", actVar, toForm, queued ? " (queued)" : "");
	u8 command[VIKING_TRANSFORM_BYTES] = { CMD_VIKING_TRANSFORM, toForm, (u8)(queued ? 1 : 0) };
	selexe::queueCommand(command, VIKING_TRANSFORM_BYTES);
}

void recvTransform(const u8* packet) {
	const u16 toForm = toFormUnit(packet[1]);
	const Bool32 queued = packet[2] != 0;
	if (toForm == 0)
		return;
	const u16 from = sourceForm(toForm);
	u32 seen = 0, ordered = 0;
	*selectionIndexStart = 0;
	for (CUnit* unit = selsync::nextSelected(); unit != NULL; unit = selsync::nextSelected()) {
		seen++;
		if (unit->id != from || !(unit->status & UnitStatus::Completed))
			continue;
		if (unit->status & (UnitStatus::InTransport | UnitStatus::InBuilding))
			continue;
		if (!queued && unit->mainOrderId == ORDER_VIKING_TRANSFORM)
			continue;
		issueOrder(unit, ORDER_VIKING_TRANSFORM, queued);
		ordered++;
		VDBG("Viking: unit %d given order 0x%X (now 0x%X)", unit->id, ORDER_VIKING_TRANSFORM, unit->mainOrderId);
	}
	VDBG("Viking: command 0x40 to %d, %d selected, %d ordered", toForm, seen, ordered);
}

void orderTransform(CUnit* unit) {
	if (unit->id != VIKING_ASSAULT && unit->id != VIKING_FIGHTER) {
		unit->orderToIdle();
		return;
	}

	if (unit->mainOrderState == STATE_STOP) {
		if (unit->movementFlags & MovementFlags::Accelerating)
			holdPosition(unit);
		unit->mainOrderState = STATE_START;
	}

	if (unit->mainOrderState == STATE_START) {
		if (unit->movementFlags & MovementFlags::Accelerating)
			return;
		const u8 facing = unit->currentDirection1;
		VDBG("Viking: order start, unit %d facing %d", unit->id, facing);
		if (unit->id == VIKING_FIGHTER) {
			//landing: the ground form from the start, its spot settled now
			replaceUnitWithType(unit, VIKING_ASSAULT);
			Point16 away;
			const Landing found = settleLanding(unit, &away);
			if (found == LAND_AWAY) {
				//as SC2's: fly to the nearest spot it can stand on, then land
				//there (the order again, queued; it searches again on arrival)
				replaceUnitWithType(unit, VIKING_FIGHTER);
				restoreFacing(unit, facing);
				VDBG("Viking: flying to land at %d,%d (from %d,%d)", away.x, away.y, unit->position.x, unit->position.y);
				unit->orderTo(OrderId::Move, away.x, away.y);
				issueOrder(unit, ORDER_VIKING_TRANSFORM, 1);
				return;
			}
			if (found == LAND_NONE) {
				replaceUnitWithType(unit, VIKING_FIGHTER);
				restoreFacing(unit, facing);
				if (unit->playerId == *LOCAL_NATION_ID)
					scbw::printText("Can't land there.");
				//the same way back to idle as a finished transform (the first
				//build used orderToIdle here, and the refused fighter froze)
				orderReturnToIdle(unit);
				VDBG("Viking: refused; now unit %d, status 0x%X, move state %d, order 0x%X",
					unit->id, unit->status, unit->movementState, unit->mainOrderId);
				return;
			}
			restoreFacing(unit, facing);
			unit->orderSignal &= ~1;
			unit->sprite->mainGraphic->playIscriptAnim(IscriptAnimation::Unused1);
			unit->mainOrderState = STATE_LANDING;
		}
		else {
			//lift-off: stays the ground form until the art has risen
			unit->orderSignal &= ~1;
			unit->sprite->mainGraphic->playIscriptAnim(IscriptAnimation::Unused2);
			unit->mainOrderState = STATE_LIFTING;
		}
		return;
	}

	if (!(unit->orderSignal & 1))
		return;
	unit->orderSignal &= ~1;
	VDBG("Viking: morph done (state %d), unit %d", unit->mainOrderState, unit->id);
	if (unit->mainOrderState == STATE_LIFTING) {
		const u8 facing = unit->currentDirection1;
		replaceUnitWithType(unit, VIKING_FIGHTER);
		restoreFacing(unit, facing);
	}
	orderReturnToIdle(unit);
	VDBG("Viking: done; now unit %d, status 0x%X, move state %d, order 0x%X",
		unit->id, unit->status, unit->movementState, unit->mainOrderId);
}

} //vikingtransform

namespace {

const u32 Func_ReplaceUnitWithType = 0x0049FED0;
void replaceUnitWithType(CUnit* unit, u16 newUnitId) {
	u32 newUnitId_ = newUnitId;
	__asm {
		PUSHAD
		PUSH newUnitId_
		MOV EAX, unit
		CALL Func_ReplaceUnitWithType
		POPAD
	}
}

const u32 Func_OrdersHoldPositionSuicidal = 0x004EB5B0;
void holdPosition(CUnit* unit) {
	__asm {
		PUSHAD
		MOV ESI, unit
		CALL Func_OrdersHoldPositionSuicidal
		POPAD
	}
}

const u32 Func_OrderReturnToIdle = 0x00463770;
void orderReturnToIdle(CUnit* unit) {
	__asm {
		PUSHAD
		MOV ESI, unit
		CALL Func_OrderReturnToIdle
		POPAD
	}
}

//As CMDRECV_SiegeTank.cpp issues SiegeMode (0x4754F0), the same registers
//and pushes: no target, no unit type, the two points the receive leaves at
//0x6D5C24 / 0x6D5C20.
const u32 Func_IssueOrder = 0x004754F0;
void issueOrder(CUnit* unit, u32 orderId, Bool32 queued) {
	Point16 unkPos1 = *(Point16*)(0x006D5C24);
	Point16 unkPos2 = *(Point16*)(0x006D5C20);
	CUnit* target = NULL;
	u32 unitId = UnitId::None;
	u32 unkQueuedOrderType = 1;
	int x = 0, y = 0;
	__asm {
		PUSHAD
		MOV EAX, y
		MOV EDX, x
		MOV EBX, unitId
		MOV ESI, unit
		PUSH unkPos1
		PUSH unkPos2
		PUSH target
		PUSH orderId
		PUSH queued
		PUSH unkQueuedOrderType
		CALL Func_IssueOrder
		POPAD
	}
}

} //unnamed namespace
