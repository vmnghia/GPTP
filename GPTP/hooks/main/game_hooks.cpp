/// This is where to put various behaviors that cannot fit in a hook
#include "game_hooks.h"

#include <graphics/graphics.h>

#include <SCBW/UnitFinder.h>
#include <SCBW/api.h>
#include <SCBW/build_progress.h>
#include <SCBW/order_lines.h>
#include "../interface/resolution.h"
#include "../interface/reshade_bridge.h"
#include "../recv_commands/smart_cast.h"
#include <SCBW/selection_ext.h>
#include "../selection_ext/sel_build.h"
#include "../selection_ext/sel_profile.h"
#include <cstdio>

#include "../psi_field.h"

namespace utils
{
void loopThroughVisibleUnits(void (*callback)(CUnit *))
{
    // Loop through all units in the game.
    for (CUnit *unit = *firstVisibleUnit; unit; unit = unit->link.next)
    {
        callback(unit);
    }
}

bool isMineral(const CUnit *resource)
{
    return resource->id >= UnitId::ResourceMineralField && resource->id <= UnitId::ResourceMineralFieldType3;
}
} // namespace utils

namespace plugins
{
class HarvestTargetFinder : public scbw::UnitFinderCallbackMatchInterface
{
    CUnit *mainHarvester;

  public:
    void setMainHarvester(CUnit *mainHarvester)
    {
        this->mainHarvester = mainHarvester;
    }
    bool match(CUnit *unit)
    {
        if (!unit)
            return false;

        if (mainHarvester->getDistanceToTarget(unit) > (16 << 5)) // Harvest distance
            return false;

        if (!(utils::isMineral(unit)))
            return false;

        return true;
    }
};
HarvestTargetFinder harvestTargetFinder;

void exploreMap()
{
    for (int x = 0; x < mapTileSize->width; x++)
    {
        for (int y = 0; y < mapTileSize->height; y++)
        {

            if ((x + y) % 2)
            {
                ActiveTile *currentTile = &(*activeTileArray)[(x) + mapTileSize->width * (y)];
                currentTile->exploredFlags = 0;
            }
        }
    }
    // By RavenWolf:
    // if vespene geyser or a mineral field, reveal it to all players
    for (CUnit *unit = *firstVisibleUnit; unit; unit = unit->link.next)
    {
        if (utils::isMineral(unit) || unit->id == UnitId::ResourceVespeneGeyser)
        {
            unit->sprite->visibilityFlags = 0xFF;
        }
    }
}

void initializeGame()
{
    if (*elapsedTimeFrames == 0)
    {
        scbw::printText(PLUGIN_NAME ": built " __DATE__ " " __TIME__);
        resolution::printSettings();

        u32 failedLine;
        const u32 failed = selext::selfTest(&failedLine);
        char selfTestText[96];
        if (failed == 0)
            sprintf_s(selfTestText, "extended selection: self-test passed");
        else
            sprintf_s(selfTestText, "extended selection: %u self-test checks FAILED (first at line %u)",
                      failed, failedLine);
        scbw::printText(selfTestText);

        if (*GAME_TYPE != GameType::UseMapSettings)
        {
            exploreMap();
            CUnit *firstMineral[8];
            for (CUnit *base = *firstVisibleUnit; base; base = base->link.next)
            {
                if (units_dat::BaseProperty[base->id] & UnitProperty::ResourceDepot)
                {
                    harvestTargetFinder.setMainHarvester(base);
                    firstMineral[base->playerId] =
                        scbw::UnitFinder::getNearestTarget(base->getX() - 512, base->getY() - 512, base->getX() + 512,
                                                           base->getY() + 512, base, harvestTargetFinder);
                }
            }

            for (CUnit *worker = *firstVisibleUnit; worker; worker = worker->link.next)
            {
                if (units_dat::BaseProperty[worker->id] & UnitProperty::Worker)
                {
                    if (firstMineral[worker->playerId])
                    {
                        worker->orderTo(OrderId::Harvest1, firstMineral[worker->playerId]);
                    }
                }
            }
        }
    }
}

// Which sprites the local player sees (a bit per player), in a game and in a replay
const u8 *const LOCAL_VISIBILITY = (u8 *)0x0057F0B0;
const u8 *const REPLAY_VISIBILITY = (u8 *)0x006D0F18;

// Whether the local player sees this unit now, by the rule a click selects by (0x46F161): its sprite is
// out of the fog, and a cloaked or burrowed unit is detected. False for NULL and for a pointer that is
// no living unit (an order may keep its target after the target dies).
// Drawing only: nothing here feeds game state.
bool seenByLocalPlayer(const CUnit *unit)
{
    if (unit < unitTable || unit >= unitTable + UNIT_ARRAY_LENGTH)
        return false;
    if (unit->sprite == NULL || unit->mainOrderId == OrderId::Die)
        return false;
    const u32 sees = scbw::isInReplay() ? *REPLAY_VISIBILITY : *LOCAL_VISIBILITY;
    if (!(unit->sprite->visibilityFlags & sees))
        return false;
    if (unit->status & (UnitStatus::Cloaked + UnitStatus::RequiresDetection))
        return (unit->visibilityStatus & sees) != 0;
    return true;
}

// Whose bars show (buildprogress::isShownFor): the local player's, allies' where the local player sees
// the unit, every player's in a replay.
// Drawing only: nothing here feeds game state.
bool showsBuildProgress(const CUnit *unit)
{
    const u8 local = (u8)*LOCAL_NATION_ID;
    const bool allied = unit->playerId < PLAYABLE_PLAYER_COUNT && scbw::isAlliedTo(local, unit->playerId);
    return buildprogress::isShownFor(unit->playerId, local, scbw::isInReplay(), allied, seenByLocalPlayer(unit));
}

void drawBuildProgress(CUnit *unit)
{
    if (!showsBuildProgress(unit))
        return;

    int progressWidth = 0;

    int queueLength = 0;

    // The order and the work it is for: after a cancel the order goes on for a frame or two with
    // nothing to do. Training is any unit making another inside it: a building, a Carrier, a Reaver.
    bool isTraining =
        buildprogress::isTraining(unit->secondaryOrderId, unit->buildQueue[unit->buildQueueSlot % 5]);

    // Where the bar goes: over a building as it always was, closer over a unit, and wide enough for
    // the queue boxes when there are any
    const Box16 &box = units_dat::UnitBounds[unit->id];
    const buildprogress::Frame frame = buildprogress::barFrame(
        unit->getX(), unit->getY(), box.left, box.top, box.right,
        (units_dat::BaseProperty[unit->id] & UnitProperty::Building) != 0,
        (units_dat::BaseProperty[unit->id] & UnitProperty::Addon) != 0, isTraining);
    int left = frame.left;
    int top = frame.top;
    int right = frame.right;
    int bottom = frame.bottom;
    int innerLeft = left + 1;
    int innerTop = top + 1;
    int innerRight = right - 1;
    int innerBottom = bottom;

    bool isUpgradingOrResearching =
        buildprogress::isResearching(unit->mainOrderId, unit->building.techType) ||
        buildprogress::isUpgrading(unit->mainOrderId, unit->building.upgradeType) && !isTraining;
    const bool isCompleted = (unit->status & UnitStatus::Completed) != 0;
    bool isBuildingSelf = buildprogress::isBeingBuilt(unit->mainOrderId, unit->remainingBuildTime, isCompleted);

    for (int i = 0; i < 5; i++)
    {
        if (unit->buildQueue[i] != UnitId::None)
        {
            queueLength++;
        }
    }

    if (isTraining)
    {
        for (int i = 0; i < 5; i++)
        {
            // draw empty boxes border and background: 4 px tall, a border all round and 2 px inside
            graphics::drawBox(left + 8 * i, top + 6, left + 8 * i + 8, bottom + 5, graphics::COBALT, graphics::ON_MAP);
            graphics::drawFilledBox(left + 8 * i + 1, innerTop + 6, left + 8 * i + 7, innerBottom + 4,
                                    graphics::CHARCOAL, graphics::ON_MAP);

            // draw filled boxes
            if (queueLength > 0 && i < queueLength)
            {
                graphics::drawFilledBox(left + 8 * i + 1, innerTop + 6, left + 8 * i + 7, innerBottom + 4, 126,
                                        graphics::ON_MAP);
            }
        }
    }

    // draw empty progress bar border and background
    if (isTraining || isUpgradingOrResearching || isBuildingSelf)
    {
        graphics::drawBox(left, top, right, bottom, graphics::COBALT, graphics::ON_MAP);
        graphics::drawFilledBox(innerLeft, innerTop, innerRight, innerBottom - 1, graphics::CHARCOAL, graphics::ON_MAP);
    }

    int timeCost = 1;
    int elapsedBuildTime = 0;

    if (unit->currentBuildUnit && isTraining)
    {
        CUnit *unitInQueue = unit->currentBuildUnit;
        timeCost = units_dat::TimeCost[unitInQueue->id];
        elapsedBuildTime = timeCost - unitInQueue->remainingBuildTime;
    }
    else if (isUpgradingOrResearching)
    {
        switch (unit->mainOrderId)
        {
        case OrderId::ResearchTech:
            timeCost = techdata_dat::TimeCost[unit->building.techType];
            break;
        case OrderId::Upgrade:
            timeCost = upgrades_dat::TimeCostBase[unit->building.upgradeType] +
                       (upgrades_dat::TimeCostFactor[unit->building.upgradeType] * (unit->building.upgradeLevel - 1));
            break;
        }
        elapsedBuildTime = timeCost - unit->building.upgradeResearchTime;
    }
    else if (isBuildingSelf)
    {
        // A Zerg building morphing (Hatchery to Lair) is still its old type and counts down the new one's
        // build time
        const u32 builtType =
            buildprogress::typeBeingBuilt(unit->id, isCompleted, unit->buildQueue[unit->buildQueueSlot % 5]);
        timeCost = units_dat::TimeCost[builtType];
        if (playerTable[unit->playerId].race == RaceId::Protoss)
        {
            timeCost += 24; // Protoss buildings warp flash animation time
        }
        elapsedBuildTime = timeCost - unit->remainingBuildTime;
    }

    // draw progress
    progressWidth = buildprogress::fillWidth(elapsedBuildTime, timeCost, innerRight - innerLeft);
    if (isTraining || isUpgradingOrResearching || isBuildingSelf)
    {
        graphics::drawFilledBox(innerLeft, innerTop, innerLeft + progressWidth, innerBottom - 1, graphics::AQUA,
                                graphics::ON_MAP);
    }
}

void drawRallyPoint(CUnit *unit)
{
    CUnit *rallyTarget = unit->rally.unit;

    // No rally point, no line: none was ever set, or the building lifted off since (the game then
    // makes the building its own rally unit and leaves the old point where it was)
    if (!orderlines::rallyIsSet(rallyTarget == unit, unit->rally.pt.x))
        return;

    u16 rallyX = unit->rally.pt.x;
    u16 rallyY = unit->rally.pt.y;
    graphics::ColorId color = 83;

    if (rallyTarget != NULL && units_dat::BaseProperty[rallyTarget->id] & UnitProperty::ResourceContainer)
    {
        color = graphics::YELLOW;
    }

    // New units follow a rally unit of their own player (0x466F50), so the line follows it too. With
    // any other unit they walk to the point where it stood when the rally point was set.
    if (seenByLocalPlayer(rallyTarget) && rallyTarget->playerId == unit->playerId)
    {
        rallyX = rallyTarget->position.x;
        rallyY = rallyTarget->position.y;
    }

    graphics::drawChevronLine(unit->position.x, unit->position.y, rallyX, rallyY, color, graphics::ON_MAP);
    graphics::drawDottedEllipse(rallyX - 8, rallyY - 4, rallyX + 8, rallyY + 4, color, graphics::ON_MAP);
    graphics::drawDot(rallyX, rallyY, color, graphics::ON_MAP);
}

// An order's stop: at its target unit while the local player sees it, else where the order says
orderlines::Stop stopOfOrder(u32 orderId, const Target &target)
{
    const bool seen = seenByLocalPlayer(target.unit);
    return orderlines::stopOf(orderId, target.pt.x, target.pt.y, seen, seen ? target.unit->position.x : 0,
                              seen ? target.unit->position.y : 0);
}

void drawOrderQueue(CUnit *unit)
{
    // The current order, then the queue. Capped: vanilla queues 8 orders at most, and a damaged list
    // must not hang the frame.
    const u32 QUEUED_MAX = 64;
    orderlines::Stop queued[QUEUED_MAX];
    u32 count = 0;
    for (COrder *order = unit->orderQueueHead; order != NULL && count < QUEUED_MAX; order = order->next)
    {
        queued[count++] = stopOfOrder(order->orderId, order->target);
    }
    const orderlines::Stop current = stopOfOrder(unit->mainOrderId, unit->orderTarget);

    orderlines::Leg legs[QUEUED_MAX + 1];
    const u32 legCount = orderlines::legsOf(unit->position.x, unit->position.y, current, queued, count, legs);
    for (u32 i = 0; i < legCount; ++i)
    {
        const orderlines::Leg &leg = legs[i];
        graphics::ColorId color = graphics::GREEN;
        if (leg.kind == orderlines::ATTACK)
            color = graphics::RED;
        else if (leg.kind == orderlines::PATROL)
            color = graphics::BLUE;

        graphics::drawChevronLine(leg.fromX, leg.fromY, leg.toX, leg.toY, color, graphics::ON_MAP);
        graphics::drawDottedEllipse(leg.toX - 8, leg.toY - 4, leg.toX + 8, leg.toY + 4, color, graphics::ON_MAP);
    }
}

} // namespace plugins

namespace hooks
{

/// This hook is called every frame.
bool nextFrame()
{
    reshadebridge::tick(); // The plugin as a ReShade add-on: registers once ReShade is loaded

    if (!scbw::isGamePaused())
    { // If the game is not paused

        selprof::frame(selext::clientCount);
        SEL_PROFILE_SCOPE(PLUGIN_FRAME);

        scbw::setInGameLoopState(true); // Needed for scbw::random() to work
        graphics::resetAllGraphics();
        hooks::updatePsiFieldProviders();
        plugins::initializeGame();
        resolution::debugReport();
        selbuild::frame(); // Smart-build: Shift released ends Shift-placing

        u32 idleWorkerCount = 0;

        for (CUnit *unit = *firstVisibleUnit; unit; unit = unit->link.next)
        {
            if ((unit->playerId == *LOCAL_NATION_ID || scbw::isInReplay()) &&
                units_dat::BaseProperty[unit->id] & UnitProperty::Worker && unit->mainOrderId == OrderId::PlayerGuard)
            {
                ++idleWorkerCount;
            }

            switch (unit->id)
            {
            case UnitId::TerranSCV:

                break;
            }

            plugins::drawBuildProgress(unit);
        }

        // The whole selection, not vanilla's mirror of its first 12
        for (u32 i = 0; i < selext::clientCount; ++i)
        {
            CUnit *selUnit = selext::clientSel[i];

            if (selUnit == nullptr)
                continue;

            if (selUnit->playerId == *LOCAL_NATION_ID || !scbw::isInReplay())
            {
                if (units_dat::GroupFlags[selUnit->id].isFactory)
                {
                    plugins::drawRallyPoint(selUnit);
                }

                if (selUnit != NULL)
                {
                    plugins::drawOrderQueue(selUnit);
                }
            }
        }

        /*utils::loopThroughVisibleUnits([](CUnit *unit) {

        });*/

        /*char buffer[200];
        sprintf(buffer, "actStringID: %05d; %05d; %05d; %05d; %05d; %05d",
                buttonSetTable[UnitId::TerranMedic].firstButton[5].actStringID,
                buttonSetTable[UnitId::TerranMedic].firstButton[5].actVar,
                buttonSetTable[UnitId::TerranMedic].firstButton[5].iconID,
                buttonSetTable[UnitId::TerranMedic].firstButton[5].position,
                buttonSetTable[UnitId::TerranMedic].firstButton[5].reqStringID,
                buttonSetTable[UnitId::TerranMedic].firstButton[5].reqVar);
        graphics::drawText(0, 0, buffer);*/

        if (idleWorkerCount != 0)
        {
            char idleworkers[64];
            sprintf_s(idleworkers, "Idle Workers: %d", idleWorkerCount);
            graphics::drawText(5, 5, idleworkers, graphics::FONT_MEDIUM, graphics::ON_SCREEN);
        }

        scbw::setInGameLoopState(false);
    }

    return true;
}

;

bool gameOn()
{
    smartCast::reset();
    return true;
}

;

bool gameEnd()
{
    reshadebridge::clearEffectsArea(); // Outside a game ReShade's masked effects show nowhere
    return true;
}

;

} // namespace hooks

namespace plugins
{
}

namespace helpers
{

const u32 Helper_CreateBullet = 0x0048C260;
void createBullet(u8 weaponId, const CUnit *source, s16 x, s16 y, u8 attackingPlayer, u8 direction)
{
    u32 attackingPlayer_ = attackingPlayer, direction_ = direction;
    s32 x_ = x, y_ = y;

    __asm {
    PUSHAD
    PUSH direction_
    PUSH attackingPlayer_
    PUSH y_
    PUSH x_
    MOV EAX, source
    MOVZX ECX, weaponId
    CALL Helper_CreateBullet
    POPAD
    }
}
} // namespace helpers
