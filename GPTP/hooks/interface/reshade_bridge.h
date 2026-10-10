//The plugin as a ReShade add-on: hands the UI mask to the effect
//ManifoldUIMask.fx (docs/superpowers/specs/2026-10-11-reshade-ui-mask-design.md
//§4.2). Does nothing when ReShade is not in the process.
//
//Free of the plugin's other headers on purpose: reshade_bridge.cpp is the only
//file that includes ReShade's, so Windows.h and the engine's packed structs
//never meet.
#pragma once

namespace reshadebridge {

//Game thread, every frame: registers with ReShade once it is loaded.
void tick();

//Game thread. pixels is width * height bytes, row 0 at the top, 0 = effects,
//255 = keep the UI. The bytes are copied.
void publishMask(const unsigned char* pixels, unsigned width, unsigned height);

//Game thread: an empty mask, for outside a game.
void clearMask();

//Plugin::TerminatePlugin.
void shutdown();

} //reshadebridge
