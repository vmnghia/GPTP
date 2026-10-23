#pragma once

// The siege tank's SC2 effects that its iscripts cannot reach on their own
// (sc2grp, 2026-10-16):
//  - the sieged hull recoils when its turret fires: SCBW plays the attack on
//    the turret subunit only, so the base's GndAttkInit is started here;
//  - a siege shell that hits a building shows SC2's building impact (no
//    dust ring): its hit image draws image 972's GRP, which has the same
//    layout as image 533's, so the same iscript plays it frame for frame.
namespace siegefx
{
void frame(); // every game frame, from hooks::nextFrame
void reset(); // at the start of a game
} // namespace siegefx
