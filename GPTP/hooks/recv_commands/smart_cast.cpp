#include "smart_cast.h"
#include <SCBW/api.h>

namespace {

//-------- The smart-cast spells --------//

//Every order in this list is cast by one unit at a time. To take a spell out,
//or put a new one in, edit this list. It must be the same for every player,
//which is why it is compiled in rather than read from the ini: it decides
//which unit gets an order, and a different list would desync.
const u8 smartCastOrders[] = {
  OrderId::FireYamatoGun1,      //Yamato Gun (Battlecruiser)
  OrderId::MagnaPulse,          //Lockdown (Ghost)
  OrderId::CastParasite,        //Parasite (Queen)
  OrderId::SummonBroodlings,    //Spawn Broodlings (Queen)
  OrderId::DarkSwarm,           //Dark Swarm (Defiler)
  OrderId::EmpShockwave,        //EMP Shockwave (Science Vessel)
  OrderId::Teleport,            //Recall (Arbiter)
  OrderId::DefensiveMatrix,     //Defensive Matrix (Science Vessel)
  OrderId::PsiStorm,            //Psionic Storm (High Templar)
  OrderId::Irradiate,           //Irradiate (Science Vessel)
  OrderId::Plague,              //Plague (Defiler)
  OrderId::Ensnare,             //Ensnare (Queen)
  OrderId::StasisField,         //Stasis Field (Arbiter)
  OrderId::Hallucianation1,     //Hallucination (High Templar)
  OrderId::Restoration,         //Restoration (Medic)
  OrderId::CastDisruptionWeb,   //Disruption Web (Corsair)
  OrderId::CastMindControl,     //Mind Control (Dark Archon)
  OrderId::CastFeedback,        //Feedback (Dark Archon)
  OrderId::CastOpticalFlare,    //Optical Flare (Medic)
  OrderId::CastMaelstrom,       //Maelstrom (Dark Archon)
  OrderId::WarpingArchon,       //Archon Warp: the closest pair of High Templar (CMDRECV_MergeArchon)
  OrderId::WarpingDarkArchon,   //Dark Archon Meld: the closest pair of Dark Templar (CMDRECV_MergeArchon)
};

//-------- Rounds --------//

//A unit's last cast: in which round of which player's selection. The unit's
//uniqueness byte (targetOrderSpecial) tells a unit from a later one that
//reused its slot in the unit table.
struct CastStamp {
  u32 round;
  u8 player;
  u8 uniqueness;
};
CastStamp castStamps[UNIT_ARRAY_LENGTH + 1];  //by CUnit::getIndex(), which is 1-based

u32 currentRound[PLAYER_COUNT];

//The selection the current round belongs to, per player.
struct UnitRef {
  u16 index;
  u8 uniqueness;
};
UnitRef roundSelection[PLAYER_COUNT][SELECTION_ARRAY_LENGTH];
u32 roundSelectionCount[PLAYER_COUNT];

UnitRef refOf(const CUnit* unit) {
  UnitRef ref = { unit->getIndex(), unit->targetOrderSpecial };
  return ref;
}

bool castThisRound(const CUnit* unit, u32 playerId) {
  const CastStamp& stamp = castStamps[unit->getIndex()];
  return stamp.round == currentRound[playerId] && stamp.player == playerId
         && stamp.uniqueness == unit->targetOrderSpecial;
}

//A selection with a unit the round's selection did not have starts a new
//round. Units dying or leaving it do not.
void followSelection(u32 playerId, CUnit* const* selection, u32 count) {
  bool grew = false;
  for (u32 i = 0; i < count && !grew; ++i) {
    const UnitRef ref = refOf(selection[i]);
    bool known = false;
    for (u32 j = 0; j < roundSelectionCount[playerId] && !known; ++j)
      known = roundSelection[playerId][j].index == ref.index
              && roundSelection[playerId][j].uniqueness == ref.uniqueness;
    grew = !known;
  }
  if (grew)
    ++currentRound[playerId];
  for (u32 i = 0; i < count && i < SELECTION_ARRAY_LENGTH; ++i)
    roundSelection[playerId][i] = refOf(selection[i]);
  roundSelectionCount[playerId] = count < SELECTION_ARRAY_LENGTH ? count : SELECTION_ARRAY_LENGTH;
}

s32 spellCost(u32 orderId) {
  const u8 tech = orders_dat::TechUsed[orderId];
  return tech < TechId::None ? techdata_dat::EnergyCost[tech] * 256 : 0;
}

//Energy left once the spells the unit is already on its way to cast are paid.
s32 energyLeft(const CUnit* unit) {
  s32 energy = unit->energy;
  if (smartCast::isSmartCastOrder(unit->mainOrderId))
    energy -= spellCost(unit->mainOrderId);
  for (const COrder* order = unit->orderQueueHead; order != NULL; order = order->next) {
    if (smartCast::isSmartCastOrder(order->orderId))
      energy -= spellCost(order->orderId);
  }
  return energy;
}

//Whether a is a better caster than b: not cast yet this round, then more
//energy left, then the lower unit index.
bool betterCaster(const CUnit* a, const CUnit* b, u32 playerId) {
  const bool aCast = castThisRound(a, playerId), bCast = castThisRound(b, playerId);
  if (aCast != bCast)
    return !aCast;
  const s32 aEnergy = energyLeft(a), bEnergy = energyLeft(b);
  if (aEnergy != bEnergy)
    return aEnergy > bEnergy;
  return a->getIndex() < b->getIndex();
}

//Whether pair (a1, a2) comes before (b1, b2) by unit index, lower first.
bool pairBefore(const CUnit* a1, const CUnit* a2, const CUnit* b1, const CUnit* b2) {
  u16 aLow = a1->getIndex(), aHigh = a2->getIndex();
  u16 bLow = b1->getIndex(), bHigh = b2->getIndex();
  if (aLow > aHigh) { const u16 t = aLow; aLow = aHigh; aHigh = t; }
  if (bLow > bHigh) { const u16 t = bLow; bLow = bHigh; bHigh = t; }
  return aLow != bLow ? aLow < bLow : aHigh < bHigh;
}

} //unnamed namespace

namespace smartCast {

bool isSmartCastOrder(u32 orderId) {
  for (u8 order : smartCastOrders) {
    if (order == orderId)
      return true;
  }
  return false;
}

CUnit* pickCaster(u32 playerId, u32 orderId, CUnit* const* candidates, u32 count,
                  CUnit* const* selection, u32 selectionCount) {
  if (count == 0 || playerId >= PLAYER_COUNT)
    return NULL;
  followSelection(playerId, selection, selectionCount);

  const s32 cost = spellCost(orderId);
  const bool freeSpells = (*CHEAT_STATE & CheatFlags::TheGathering) != 0;

  //Of the units that can pay, the best; when every one of them has cast this
  //round, a new round starts.
  CUnit* best = NULL;
  bool anyFresh = false;
  for (u32 i = 0; i < count; ++i) {
    CUnit* unit = candidates[i];
    if (!freeSpells && energyLeft(unit) < cost)
      continue;
    anyFresh = anyFresh || !castThisRound(unit, playerId);
    if (best == NULL || betterCaster(unit, best, playerId))
      best = unit;
  }
  if (best != NULL && !anyFresh) {
    ++currentRound[playerId];
    best = NULL;
    for (u32 i = 0; i < count; ++i) {
      CUnit* unit = candidates[i];
      if ((freeSpells || energyLeft(unit) >= cost) && (best == NULL || betterCaster(unit, best, playerId)))
        best = unit;
    }
  }

  //Nobody can pay: the one with the most energy gets the order and fails as
  //vanilla does.
  if (best == NULL) {
    for (u32 i = 0; i < count; ++i) {
      if (best == NULL || candidates[i]->energy > best->energy
          || (candidates[i]->energy == best->energy && candidates[i]->getIndex() < best->getIndex()))
        best = candidates[i];
    }
  }

  CastStamp& stamp = castStamps[best->getIndex()];
  stamp.round = currentRound[playerId];
  stamp.player = (u8)playerId;
  stamp.uniqueness = best->targetOrderSpecial;
  return best;
}

u32 keepClosestPair(CUnit** templars, u32 count, u32 mergeOrder) {
  //Templar already on their way to merge are left out.
  u32 free = 0;
  for (u32 i = 0; i < count; ++i) {
    if (templars[i] != NULL && templars[i]->mainOrderId != mergeOrder)
      templars[free++] = templars[i];
  }

  s32 bestFirst = -1, bestSecond = -1;
  u32 bestDistance = 0;
  for (u32 i = 0; i < free; ++i) {
    for (u32 j = i + 1; j < free; ++j) {
      const s32 dx = templars[i]->sprite->position.x - templars[j]->sprite->position.x;
      const s32 dy = templars[i]->sprite->position.y - templars[j]->sprite->position.y;
      const u32 distance = (u32)(dx * dx + dy * dy);
      //Ties: the pair with the lower unit indices.
      if (bestFirst < 0 || distance < bestDistance
          || (distance == bestDistance && pairBefore(templars[i], templars[j], templars[bestFirst], templars[bestSecond]))) {
        bestFirst = i;
        bestSecond = j;
        bestDistance = distance;
      }
    }
  }

  CUnit* const first = bestFirst < 0 ? NULL : templars[bestFirst];
  CUnit* const second = bestFirst < 0 ? NULL : templars[bestSecond];
  for (u32 i = 0; i < count; ++i)
    templars[i] = NULL;
  if (first == NULL)
    return 0;
  templars[0] = first;
  templars[1] = second;
  return 2;
}

void reset() {
  for (CastStamp& stamp : castStamps) {
    stamp.round = 0;
    stamp.player = 0xFF;
    stamp.uniqueness = 0;
  }
  for (u32 player = 0; player < PLAYER_COUNT; ++player) {
    currentRound[player] = 1;
    roundSelectionCount[player] = 0;
  }
}

} //smartCast
