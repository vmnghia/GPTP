//The plugin as a ReShade add-on: tells the effect ManifoldUIMask.fx where
//other effects may show (docs/superpowers/specs/2026-10-11-reshade-ui-mask-design.md
//§4.2). Does nothing when ReShade is not in the process.
//
//Free of the plugin's other headers on purpose: reshade_bridge.cpp is the only
//file that includes ReShade's, so Windows.h and the engine's packed structs
//never meet.
#pragma once

namespace reshadebridge {

//Game thread, every frame: registers with ReShade once it is loaded.
void tick();

//Game thread. pixels is width * height bytes, row 0 at the top: 255 where
//effects apply (the game view), 0 where the frame from before them is kept
//(the UI); uimask::effectsArea makes it. The bytes are copied.
void publishEffectsArea(const unsigned char* pixels, unsigned width, unsigned height);

//Game thread: effects nowhere, for outside a game. That is also how ReShade
//creates the texture, so the menus have no effects before the first game.
void clearEffectsArea();

//Plugin::TerminatePlugin.
void shutdown();

} //reshadebridge
