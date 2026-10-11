# ReShade with Starcraft: Manifold

ReShade adds post-processing (sharpening, anti-aliasing and so on) to the game's picture.
The mod's plugin tells ReShade where the game view is, so those effects show only there:
the console and the menus stay as the game draws them. All of this is optional: the mod
runs the same without ReShade.

## Setup

1. cnc-ddraw, next to `StarCraft.exe`, with these lines in `ddraw.ini`:

       renderer=opengl
       maintas=false

2. ReShade 6.8.0 or newer (https://reshade.me), installed for OpenGL: its DLL sits next to
   `StarCraft.exe` as `opengl32.dll`. An older ReShade refuses the plugin, and the masked
   effects then show nowhere. For multiplayer use the installer "with full add-on
   support"; see Limits.

   If OpenGL gives trouble, Direct3D 9 works too: `renderer=direct3d9` in `ddraw.ini`, and
   ReShade's DLL named `d3d9.dll` instead (keep only one of the two names). cnc-ddraw's own
   `shader=` scalers then do nothing; they need OpenGL.
3. Copy this folder's `reshade-shaders` folder and `ReShadePreset.ini` next to
   `StarCraft.exe`. If you already have a preset, keep it and enable the two mask
   techniques yourself (below).

## Using the mask

Open ReShade's overlay (Home). In the list of techniques:

- `ManifoldMask_Top` goes above the effects you want only in the game view;
- `ManifoldMask_Bottom` goes below them.

Effects between the two show in the game view during a game, and nowhere else: not on the
console, and not in the menus, briefings or score screens. Effects after
`ManifoldMask_Bottom` cover everything, menus included. Nothing else is needed: the mask
follows the race you play and the resolution in `Manifold.ini`.

To see what is masked, tick "Display Mask" in ManifoldUIMask's settings: the area kept free
of effects is tinted magenta. Outside a game that is the whole screen.

## If the effects do not show in a game

The effects between the two techniques show only where the plugin has told ReShade they
may. If the plugin cannot reach ReShade, they show nowhere.

- `ReShade.log`, next to `StarCraft.exe`, should have the line
  `Registered add-on "Starcraft: Manifold"` once a game has started. If it has none, the
  ReShade is older than 6.8.0, or the plugin is not loaded.
- ReShade's Add-ons tab lists "Starcraft: Manifold". An add-on unticked there is refused
  until it is ticked again and the game restarted.
- To use effects without the mask, switch both mask techniques off.

## Limits

- **Multiplayer.** ReShade's standard build stops talking to add-ons while it sees network
  traffic, so in a multiplayer game the plugin may not get through and the masked effects
  do not show. The build "with full add-on support" does not have this limit.
- **Aspect-ratio bars.** With `maintas=true` in `ddraw.ini` the mask is misplaced.
- **In a game, only the console is masked.** The resource counters, messages and dialogs
  drawn over the game view get the effects.
