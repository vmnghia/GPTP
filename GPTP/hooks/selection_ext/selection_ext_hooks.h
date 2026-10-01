//Hooks of the extended selection (SCBW/selection_ext.h).
#pragma once

namespace hooks {

//Stage 1: storage, iterator, every writer of a selection, circles, saves.
void injectSelectionExtHooks();
//Stage 2: the select-chunk command and the command-length sites.
void injectSelectChunkHooks();
//Stage 3: the selection panel's pages. Needs the wide rez\statdata.bin.
void injectSelectionPanelHooks();
//Stage 4: Alt-click and double-tap centring on the extended control groups.
void injectControlGroupHooks();
//Stage 5: the card's conditions over the active subgroup; send-side checks
//(button actions, the target-order check) see it first.
void injectCommandCardHooks();

} //hooks
