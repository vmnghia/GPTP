# ReShade with Starcraft: Manifold

ReShade adds post-processing (sharpening, anti-aliasing and so on) to the game's picture.
The mod's plugin tells ReShade where the console is, so those effects can be kept off it.
All of this is optional: the mod runs the same without ReShade.

## Setup

1. cnc-ddraw, next to `StarCraft.exe`, with these lines in `ddraw.ini`:

       renderer=opengl
       maintas=false

2. ReShade 6.8.0 or newer (https://reshade.me), installed for OpenGL: its DLL sits next to
   `StarCraft.exe` as `opengl32.dll`. An older ReShade refuses the plugin and the mask
   stays empty. For multiplayer use the installer "with full add-on support"; see Limits.
3. Copy this folder's `reshade-shaders` folder and `ReShadePreset.ini` next to
   `StarCraft.exe`. If you already have a preset, keep it and enable the two mask
   techniques yourself (below).

## Using the mask

Open ReShade's overlay (Home). In the list of techniques:

- `ManifoldMask_Top` goes above the effects you want kept off the console;
- `ManifoldMask_Bottom` goes below them.

Effects between the two are undone on the console; effects after `ManifoldMask_Bottom`
cover everything. Nothing else is needed: the mask follows the race you play and the
resolution in `Manifold.ini`.

To see what is masked, tick "Display Mask" in ManifoldUIMask's settings: the area kept free
of effects is tinted magenta. Outside a game the mask is empty and nothing is tinted.

## If the mask does not appear

- `ReShade.log`, next to `StarCraft.exe`, should have the line
  `Registered add-on "Starcraft: Manifold"` once a game has started. If it has none, the
  ReShade is older than 6.8.0, or the plugin is not loaded.
- ReShade's Add-ons tab lists "Starcraft: Manifold". An add-on unticked there is refused
  until it is ticked again and the game restarted.
- Both mask techniques must be enabled, Top above the effects and Bottom below them.

## Limits

- **Multiplayer.** ReShade's standard build stops talking to add-ons while it sees network
  traffic, so the mask may not arrive in a multiplayer game and the console gets the
  effects. The build "with full add-on support" does not have this limit.
- **Aspect-ratio bars.** With `maintas=true` in `ddraw.ini` the mask is misplaced.
- **Only the console is masked.** The resource counters, messages and menus drawn over the
  game view get the effects.
