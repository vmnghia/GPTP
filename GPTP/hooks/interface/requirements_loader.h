//Applies FireGraft's Dat Requirements (the .fgp in the mod exe's MPQ) in place
//of FireGraft's runtime, which never runs in Manifold. Spec:
//docs/superpowers/specs/2026-10-29-fg-requirements-design.md.
#pragma once

namespace reqloader {

//Once per run, at the first game start: builds the five tables in the plugin's
//memory and points the exe's code at them. Anything wrong (no .fgp, a bad
//section, code that doesn't hold the vanilla address) prints its reason once
//and leaves every table vanilla.
void applyOnce();

} //reqloader
