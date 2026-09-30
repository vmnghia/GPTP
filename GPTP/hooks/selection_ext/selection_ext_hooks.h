//Hooks of the extended selection (SCBW/selection_ext.h).
#pragma once

namespace hooks {

//Stage 1: storage, iterator, every writer of a selection, circles, saves.
void injectSelectionExtHooks();
//Stage 2: the select-chunk command and the command-length sites.
void injectSelectChunkHooks();

} //hooks
