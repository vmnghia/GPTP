#include "siege_tank_fx.h"
#include <SCBW/api.h>
#include <cstring>

namespace
{
// images.dat entry holding thingy\mushroomB.grp, the siege impact on a
// building; spawned by nothing, only its GRP is borrowed
const u16 BUILDING_IMPACT_IMAGE = 972;

// each sieged tank's turret cooldown last frame: it jumps up when the
// turret fires (attackwith), which is when the hull recoils
u8 lastCooldown[UNIT_ARRAY_LENGTH];

bool isSiegedTank(const CUnit *unit)
{
    return unit->id == UnitId::TerranSiegeTankSiegeMode || unit->id == UnitId::Hero_EdmundDukeSiegeMode;
}

bool isSiegeShell(u8 weaponId)
{
    return weaponId == WeaponId::ArcliteShockCannon || weaponId == WeaponId::ArcliteShockCannon_EdmundDuke;
}
} // namespace

namespace siegefx
{
void reset()
{
    std::memset(lastCooldown, 0, sizeof(lastCooldown));
}

void frame()
{
    for (CUnit *unit = *firstVisibleUnit; unit; unit = unit->link.next)
    {
        if (!isSiegedTank(unit) || unit->subunit == NULL)
            continue;
        const u16 index = unit->getIndex();
        if (index >= UNIT_ARRAY_LENGTH)
            continue;
        const u8 cooldown = unit->subunit->groundWeaponCooldown;
        // Unused1, not GndAttkInit: the engine follows an image's attack
        // anims with GndAttkToIdle, which a vanilla-shaped base header leaves
        // empty (offset 0, garbage frames, a crash in the .lol read)
        if (cooldown > lastCooldown[index] && unit->sprite != NULL && unit->sprite->mainGraphic != NULL)
            unit->sprite->mainGraphic->playIscriptAnim(IscriptAnimation::Unused1);
        lastCooldown[index] = cooldown;
    }

    GrpHead *building = imageGrpGraphics[BUILDING_IMPACT_IMAGE];
    for (CBullet *bullet = *firstBullet; bullet; bullet = bullet->next)
    {
        if (!isSiegeShell(bullet->weaponType) || bullet->sprite == NULL || bullet->sprite->mainGraphic == NULL)
            continue;
        const CUnit *target = bullet->attackTarget.unit;
        if (target == NULL || !(units_dat::BaseProperty[target->id] & UnitProperty::Building))
            continue;
        CImage *hit = bullet->sprite->mainGraphic;
        if (building != NULL && hit->grpOffset != building)
            hit->grpOffset = building;
    }
}
} // namespace siegefx
