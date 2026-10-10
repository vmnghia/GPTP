# ReShade UI Mask Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** the plugin tells ReShade where the console is, so a mask effect restores the
untouched console after the player's other effects, for any race and resolution.

**Architecture:** three parts. `SCBW/ui_mask` builds the mask's pixels from the console
image (pure, host-tested). `hooks/interface/reshade_bridge` registers the plugin as a
ReShade add-on and uploads the mask into an effect's texture on the renderer's thread.
`ManifoldUIMask.fx` saves the frame before the player's effects and puts the masked part
back after them. Two hooks that already exist (console image loaded, game end) drive it.

**Tech Stack:** C++17, MSVC v145, Win32; ReShade 6.8.0 add-on API (API version 20,
header-only, vendored); ReShade FX for the effect.

**Spec:** `docs/superpowers/specs/2026-10-11-reshade-ui-mask-design.md`

## Global Constraints

- Cosmetic only: no game state is read beyond the console image, and none is written.
- Without ReShade in the process the plugin behaves as before: no message, no per-frame
  work beyond one counter, one module enumeration every 100 frames.
- The game thread never calls ReShade's runtime. Only the `reshade_present` callback does.
- `reshade_bridge.cpp` is the only file that includes ReShade's headers, and it includes
  none of the plugin's headers except `reshade_bridge.h`.
- No ReShade call and no C++ statement with temporaries inside a `__declspec(naked)` function.
- Effect file `ManifoldUIMask.fx`, namespace `ManifoldUIMask`, texture `MaskTex` (R8, frame
  size, no `source`), techniques `ManifoldMask_Top` and `ManifoldMask_Bottom`.
- Mask bytes: 0 = effects, 255 = keep the UI; row 0 is the top of the screen.
- ReShade headers: v6.8.0, under `GPTP/thirdparty/reshade/`, licence BSD-3-Clause OR MIT.
- Indentation follows the neighbours: tabs in `SCBW/` and `tests/`, two spaces in
  `hooks/interface/`, four in `hooks/main/game_hooks.cpp`.
- `.bat` files are CRLF. Commit to the current branch; never push.
- Claude can build here (`tests/verify.ps1`) but cannot run StarCraft: nothing in game is
  verified until the user's round (Task 5).

## Review Focus

Inputs the spec implies that no host test can reach, most likely first. Each is pinned to
a step below.

1. **The mask upside down.** OpenGL's rows run bottom-up; the spike's pattern was the same
   on every row, so row order is untested. Expected: white at the bottom, over the console.
   Pinned: Task 5 round, item 1.
2. **A console replaced while a mask is up**: restart mission, load a save of another race.
   Expected: the new console's mask, no remains of the old one. Pinned: Task 5, item 3.
3. **The texture recreated behind our back**: window resized, Alt+Tab, techniques switched
   on mid-game. Expected: the mask is back within a second. Pinned: Task 5, items 6 and 7.
4. **A transparent pixel that touches the game view only at a corner.** Expected: masked
   (4-connected fill), so a one-pixel diagonal gap in the art's outline does not leak.
   Pinned: Task 1 test `diagonal`.
5. **The largest screen, 2048x1536.** Expected: no stack overflow and no visible delay.
   Pinned: Task 1 test `largest`.

---

### Task 1: the mask's pixels (`SCBW/ui_mask`)

**Files:**
- Create: `GPTP/SCBW/ui_mask.h`, `GPTP/SCBW/ui_mask.cpp`
- Create: `tests/ui_mask_test.cpp`, `tests/ui_mask_test.bat`
- Modify: `tests/verify.ps1` (after the button set file test), `GPTP/GPTP.vcxproj`
  (next to `SCBW\console_raise.cpp` and `SCBW\console_raise.h`)

**Interfaces:**
- Consumes: `u8`, `u32` from `GPTP/types.h`.
- Produces: `uimask::EFFECTS` (0), `uimask::UI` (255), `uimask::Mask { u32 width, height;
  std::vector<u8> pixels; }`, `void uimask::reset(Mask&, u32 width, u32 height)`,
  `void uimask::addConsoleImage(Mask&, const u8* image)`.

- [ ] **Step 1: Write the failing test**

`tests/ui_mask_test.cpp`:

```cpp
//Host test of the UI mask's pure logic (SCBW/ui_mask.cpp). Build and run with
//ui_mask_test.bat (MSVC x86); it needs no game.
#include <SCBW/ui_mask.h>
#include <cstdio>
#include <string>
#include <vector>

using namespace uimask;

namespace {

u32 failures;
u32 firstLine;

void check(bool ok, u32 line) {
	if (!ok) {
		if (failures == 0)
			firstLine = line;
		failures++;
	}
}

#define CHECK(x) check((x), __LINE__)

//An image from rows of text: '.' is transparent (index 0), anything else art.
std::vector<u8> image(const char* const* rows, u32 width, u32 height) {
	std::vector<u8> v;
	for (u32 y = 0; y < height; ++y)
		for (u32 x = 0; x < width; ++x)
			v.push_back(rows[y][x] == '.' ? 0 : 7);
	return v;
}

//A mask as rows of text: '.' effects, '#' UI.
std::string text(const Mask& mask) {
	std::string s;
	for (u32 y = 0; y < mask.height; ++y) {
		for (u32 x = 0; x < mask.width; ++x)
			s += mask.pixels[y * mask.width + x] == UI ? '#' : '.';
		s += '\n';
	}
	return s;
}

std::string masked(const char* const* rows, u32 width, u32 height) {
	const std::vector<u8> art = image(rows, width, height);
	Mask mask;
	reset(mask, width, height);
	addConsoleImage(mask, art.data());
	return text(mask);
}

} //unnamed namespace

int main() {
	//The game view above a full-width console.
	{
		const char* rows[] = { "........", "........", "########", "########" };
		CHECK(masked(rows, 8, 4) == "........\n........\n########\n########\n");
	}
	//A tall block at one side (the minimap's frame): masked, the view above it is not.
	{
		const char* rows[] = { "........", "###.....", "###.....", "########" };
		CHECK(masked(rows, 8, 4) == "........\n###.....\n###.....\n########\n");
	}
	//A transparent hole enclosed by the console stays masked.
	{
		const char* rows[] = { "........", "########", "#..#####", "########" };
		CHECK(masked(rows, 8, 4) == "........\n########\n########\n########\n");
	}
	//A transparent pocket open to the top is game view.
	{
		const char* rows[] = { "........", "##.#####", "##.#####", "########" };
		CHECK(masked(rows, 8, 4) == "........\n##.#####\n##.#####\n########\n");
	}
	//No transparent pixel in the top row: nothing is game view.
	{
		const char* rows[] = { "########", "........", "........" };
		CHECK(masked(rows, 8, 3) == "########\n########\n########\n");
	}
	//diagonal: a transparent pixel touching the view only at a corner is masked.
	{
		const char* rows[] = { "....####", "....####", "####.###", "########" };
		CHECK(masked(rows, 8, 4) == "....####\n....####\n########\n########\n");
	}
	//Adding only sets pixels: one already marked UI in the game view stays
	//(a later region), and reset() clears to the new size.
	{
		const char* rows[] = { "....", "...." };
		const std::vector<u8> art = image(rows, 4, 2);
		Mask mask;
		reset(mask, 4, 2);
		mask.pixels[1] = UI;
		addConsoleImage(mask, art.data());
		CHECK(text(mask) == ".#..\n....\n");
		reset(mask, 3, 1);
		CHECK(mask.width == 3 && mask.height == 1 && text(mask) == "...\n");
	}
	//A null image and a zero-sized mask change nothing.
	{
		Mask mask;
		reset(mask, 2, 2);
		addConsoleImage(mask, NULL);
		CHECK(text(mask) == "..\n..\n");
		const u8 art[4] = { 7, 7, 7, 7 };
		Mask empty;
		CHECK(empty.width == 0 && empty.height == 0);
		addConsoleImage(empty, art);
		CHECK(empty.pixels.empty());
	}
	//largest: 2048x1536, a console over the bottom 100 rows.
	{
		const u32 width = 2048, height = 1536, console = 100;
		std::vector<u8> art((size_t)width * height, 0);
		for (size_t i = (size_t)width * (height - console); i < art.size(); ++i)
			art[i] = 7;
		Mask mask;
		reset(mask, width, height);
		addConsoleImage(mask, art.data());
		size_t ui = 0;
		for (size_t i = 0; i < mask.pixels.size(); ++i)
			ui += mask.pixels[i] == UI;
		CHECK(ui == (size_t)width * console);
		CHECK(mask.pixels[0] == EFFECTS && mask.pixels.back() == UI);
	}

	if (failures != 0) {
		printf("FAIL: %u checks (first at ui_mask_test.cpp line %u)\n", failures, firstLine);
		return 1;
	}
	printf("PASS\n");
	return 0;
}
```

`tests/ui_mask_test.bat`:

```bat
@echo off
rem Builds and runs the host test of the UI mask's pure logic.
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars32.bat" >nul
cd /d "%~dp0"
if not exist out mkdir out
cl /nologo /EHsc /W3 /I..\GPTP /Fo:out\ /Fe:out\ui_mask_test.exe ui_mask_test.cpp ..\GPTP\SCBW\ui_mask.cpp > out\ui_mask_build.log
if errorlevel 1 (
	type out\ui_mask_build.log
	exit /b 1
)
out\ui_mask_test.exe
```

Make the `.bat` CRLF: `unix2dos tests/ui_mask_test.bat`.

- [ ] **Step 2: Run it to see it fail**

Run (PowerShell): `& "D:\SC Modding\GPTP\tests\ui_mask_test.bat"`
Expected: the build log with `Cannot open include file: 'SCBW/ui_mask.h'`, exit code 1.

- [ ] **Step 3: Write the implementation**

`GPTP/SCBW/ui_mask.h`:

```cpp
//The mask that keeps ReShade's effects off the UI
//(docs/superpowers/specs/2026-10-11-reshade-ui-mask-design.md §4.1). Pure: the
//host test tests/ui_mask_test.bat builds it on its own.
#pragma once
#include "../types.h"
#include <vector>

namespace uimask {

const u8 EFFECTS = 0;	//the game view: effects apply
const u8 UI = 255;		//the frame from before the effects is put back

struct Mask {
	u32 width = 0;
	u32 height = 0;
	std::vector<u8> pixels;	//width * height, row 0 at the top
};

//The mask at that size, all EFFECTS.
void reset(Mask& mask, u32 width, u32 height);

//Marks the console as UI. image is the console image at the mask's size, one
//byte per pixel, 0 = transparent. The game view is the transparent area
//connected (left, right, up, down) to the top row; everything else is UI, a
//transparent hole enclosed by the console included. Only ever sets pixels, so
//further regions can be added before or after. A null image or a zero-sized
//mask changes nothing.
void addConsoleImage(Mask& mask, const u8* image);

} //uimask
```

`GPTP/SCBW/ui_mask.cpp`:

```cpp
#include "ui_mask.h"
#include <cstddef>

namespace uimask {

void reset(Mask& mask, u32 width, u32 height) {
	mask.width = width;
	mask.height = height;
	mask.pixels.assign((size_t)width * height, EFFECTS);
}

void addConsoleImage(Mask& mask, const u8* image) {
	const u32 width = mask.width;
	const u32 height = mask.height;
	if (image == NULL || width == 0 || height == 0)
		return;
	const u32 count = width * height;

	//The game view: a flood fill over the transparent pixels from the top row.
	//An explicit stack, since the view of a large screen is millions of pixels.
	std::vector<u8> view(count, 0);
	std::vector<u32> open;	//in the view, neighbours not looked at yet
	for (u32 x = 0; x < width; ++x)
		if (image[x] == 0) {
			view[x] = 1;
			open.push_back(x);
		}
	while (!open.empty()) {
		const u32 at = open.back();
		open.pop_back();
		const u32 x = at % width;
		//A missing neighbour is replaced by the pixel itself, already in the view.
		const u32 neighbours[4] = {
			x > 0 ? at - 1 : at,
			x + 1 < width ? at + 1 : at,
			at >= width ? at - width : at,
			at + width < count ? at + width : at,
		};
		for (u32 i = 0; i < 4; ++i) {
			const u32 next = neighbours[i];
			if (image[next] == 0 && !view[next]) {
				view[next] = 1;
				open.push_back(next);
			}
		}
	}

	for (u32 i = 0; i < count; ++i)
		if (!view[i])
			mask.pixels[i] = UI;
}

} //uimask
```

- [ ] **Step 4: Run the test to see it pass**

Run: `& "D:\SC Modding\GPTP\tests\ui_mask_test.bat"`
Expected: `PASS`.

- [ ] **Step 5: Add it to the build and to `verify.ps1`**

`GPTP/GPTP.vcxproj`: after `<ClCompile Include="SCBW\console_raise.cpp" />` add
`<ClCompile Include="SCBW\ui_mask.cpp" />`; after
`<ClInclude Include="SCBW\console_raise.h" />` add `<ClInclude Include="SCBW\ui_mask.h" />`.

`tests/verify.ps1`: after the two lines of the button set file test add

```powershell
& "$PSScriptRoot\ui_mask_test.bat" 2>$null
if ($LASTEXITCODE -ne 0) { Write-Output "UI mask test FAILED"; exit 1 }
```

and extend the comment on line 1 to "Runs the host tests, then builds the plugin (Debug)."

Run: `powershell -ExecutionPolicy Bypass -File "D:\SC Modding\GPTP\tests\verify.ps1"`
Expected: `PASS` three times, `naked wrappers: ok`, `plugin build succeeded`.

- [ ] **Step 6: Commit**

```bash
git add GPTP/SCBW/ui_mask.h GPTP/SCBW/ui_mask.cpp tests/ui_mask_test.cpp tests/ui_mask_test.bat tests/verify.ps1 GPTP/GPTP.vcxproj
git commit -m "UI mask: the console's mask from its image, host-tested"
```

---

### Task 2: the effect and what ships (`reshade/`)

**Files:**
- Create: `reshade/reshade-shaders/Shaders/ManifoldUIMask.fx`
- Create: `reshade/ReShadePreset.ini`
- Create: `reshade/README.md`

**Interfaces:**
- Produces, for Task 3: effect file name `ManifoldUIMask.fx`, texture `MaskTex` (R8, frame
  size). For players: techniques `ManifoldMask_Top`, `ManifoldMask_Bottom`, option
  "Display Mask".

The effect cannot be compiled outside ReShade. It is checked in Task 5 from `ReShade.log`
(`Successfully compiled ...ManifoldUIMask.fx`) before the user's round.

- [ ] **Step 1: Write the effect**

Self-contained on purpose (no `ReShade.fxh`), so it works with any shader set installed.

`reshade/reshade-shaders/Shaders/ManifoldUIMask.fx`:

```hlsl
// Starcraft: Manifold - keeps other effects off the console.
//
// Put ManifoldMask_Top above the effects that should stay off the console and
// ManifoldMask_Bottom below them. Top saves the frame; Bottom puts the saved
// frame back wherever the mask says "UI". The Manifold plugin uploads the mask
// (MaskTex) when a game starts, for the console of the race being played at
// the current resolution, and empties it when the game ends. Without the
// plugin the mask stays empty and the pair changes nothing.
//
// Effects placed after ManifoldMask_Bottom are not masked.

namespace ManifoldUIMask
{

uniform bool DisplayMask <
	ui_label = "Display Mask";
	ui_tooltip = "Shows the mask instead of the picture: white is kept free of effects.\n"
	             "For checking only. Outside a game the mask is empty, so the screen is black.";
> = false;

texture BackBufferTex : COLOR;
sampler BackBuffer { Texture = BackBufferTex; };

texture BackupTex { Width = BUFFER_WIDTH; Height = BUFFER_HEIGHT; };
sampler Backup { Texture = BackupTex; };

// Filled by the plugin through ReShade's add-on API; ReShade clears it to zero.
texture MaskTex { Width = BUFFER_WIDTH; Height = BUFFER_HEIGHT; Format = R8; };
sampler Mask { Texture = MaskTex; };

void FullscreenVS(in uint id : SV_VertexID, out float4 position : SV_Position, out float2 texcoord : TEXCOORD)
{
	texcoord.x = (id == 2) ? 2.0 : 0.0;
	texcoord.y = (id == 1) ? 2.0 : 0.0;
	position = float4(texcoord * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
}

float4 BackupPS(float4 position : SV_Position, float2 texcoord : TEXCOORD) : SV_Target
{
	return tex2D(BackBuffer, texcoord);
}

float4 RestorePS(float4 position : SV_Position, float2 texcoord : TEXCOORD) : SV_Target
{
	float mask = tex2D(Mask, texcoord).r;
	if (DisplayMask)
		return float4(mask, mask, mask, 1.0);
	return lerp(tex2D(BackBuffer, texcoord), tex2D(Backup, texcoord), mask);
}

technique ManifoldMask_Top <
	ui_tooltip = "Place this above the effects to keep off the console.";
>
{
	pass
	{
		VertexShader = FullscreenVS;
		PixelShader = BackupPS;
		RenderTarget = BackupTex;
	}
}

technique ManifoldMask_Bottom <
	ui_tooltip = "Place this below the effects to keep off the console.";
>
{
	pass
	{
		VertexShader = FullscreenVS;
		PixelShader = RestorePS;
	}
}

} // namespace ManifoldUIMask
```

- [ ] **Step 2: Write the minimal preset**

`reshade/ReShadePreset.ini`:

```ini
Techniques=ManifoldMask_Top@ManifoldUIMask.fx,ManifoldMask_Bottom@ManifoldUIMask.fx
TechniqueSorting=ManifoldMask_Top@ManifoldUIMask.fx,ManifoldMask_Bottom@ManifoldUIMask.fx
```

- [ ] **Step 3: Write the player notes**

`reshade/README.md`:

```markdown
# ReShade with Starcraft: Manifold

ReShade adds post-processing (sharpening, anti-aliasing and so on) to the game's picture.
The mod's plugin tells ReShade where the console is, so those effects can be kept off it.
All of this is optional: the mod runs the same without ReShade.

## Setup

1. cnc-ddraw, next to `StarCraft.exe`, with these lines in `ddraw.ini`:

       renderer=opengl
       maintas=false

2. ReShade (https://reshade.me), installed for OpenGL: its DLL sits next to `StarCraft.exe`
   as `opengl32.dll`. For multiplayer use the installer "with full add-on support"; see
   Limits.
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

To see what is masked, tick "Display Mask" in ManifoldUIMask's settings: white is kept
free of effects. Outside a game the mask is empty and the screen goes black, so untick it
afterwards.

## Limits

- **Multiplayer.** ReShade's standard build stops talking to add-ons while it sees network
  traffic, so the mask may not arrive in a multiplayer game and the console gets the
  effects. The build "with full add-on support" does not have this limit.
- **Aspect-ratio bars.** With `maintas=true` in `ddraw.ini` the mask is misplaced.
- **Only the console is masked.** The resource counters, messages and menus drawn over the
  game view get the effects.
```

- [ ] **Step 4: Commit**

```bash
git add reshade
git commit -m "UI mask: the mask effect, a minimal preset and the player notes"
```

---

### Task 3: the ReShade side (`hooks/interface/reshade_bridge`)

**Files:**
- Create: `GPTP/thirdparty/reshade/` with ReShade's eight API headers, `LICENSE.md` and
  `README.txt`
- Create: `GPTP/hooks/interface/reshade_bridge.h`, `GPTP/hooks/interface/reshade_bridge.cpp`
- Modify: `GPTP/GPTP.vcxproj` (next to `hooks\interface\resolution_hud.cpp` and
  `hooks\interface\resolution.h`)

**Interfaces:**
- Consumes: effect file `ManifoldUIMask.fx`, texture `MaskTex` (Task 2).
- Produces: `void reshadebridge::tick()`, `void reshadebridge::publishMask(const unsigned
  char* pixels, unsigned width, unsigned height)`, `void reshadebridge::clearMask()`,
  `void reshadebridge::shutdown()`; DLL exports `NAME` and `DESCRIPTION`.

No host test is possible (it needs ReShade in the process); the check is the plugin build
here and the round of Task 5.

- [ ] **Step 1: Vendor ReShade's headers**

```bash
cd "/d/SC Modding/GPTP/GPTP" && mkdir -p thirdparty/reshade && cd thirdparty/reshade
for f in reshade.hpp reshade_api.hpp reshade_events.hpp reshade_overlay.hpp reshade_api_device.hpp reshade_api_pipeline.hpp reshade_api_resource.hpp reshade_api_format.hpp; do
  curl -sfL -o $f https://raw.githubusercontent.com/crosire/reshade/v6.8.0/include/$f || echo "FAILED $f"
done
curl -sfL -o LICENSE.md https://raw.githubusercontent.com/crosire/reshade/v6.8.0/LICENSE.md
grep -n "define RESHADE_API_VERSION" reshade.hpp
```

Expected: no `FAILED` line, and `#define RESHADE_API_VERSION 20`.

`GPTP/thirdparty/reshade/README.txt`:

```
ReShade's add-on API headers, unchanged.

Source:   https://github.com/crosire/reshade, tag v6.8.0, folder include/
API:      RESHADE_API_VERSION 20
Licence:  BSD-3-Clause OR MIT, (c) Patrick Mours (LICENSE.md)
Used by:  hooks/interface/reshade_bridge.cpp only.

The headers only declare ReShade's interface; ReShade itself is not part of the plugin and
is looked up in the process at run time. To update, replace the eight .hpp files and
LICENSE.md from a newer tag; a ReShade older than the headers refuses the registration.
```

- [ ] **Step 2: Write the header**

`GPTP/hooks/interface/reshade_bridge.h`:

```cpp
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
```

- [ ] **Step 3: Write the implementation**

`GPTP/hooks/interface/reshade_bridge.cpp`:

```cpp
//ReShade calls back on the renderer's thread, so the two threads only share
//the mask buffer (under maskLock) and one flag. The game thread never calls
//ReShade's runtime; uploads happen in onPresent.
#include "reshade_bridge.h"
#include <thirdparty/reshade/reshade.hpp>
#include <atomic>
#include <cstring>
#include <mutex>
#include <vector>

//ReShade lists an add-on under these instead of the file name the launcher
//loads the plugin under. NAME is PLUGIN_NAME (definitions.h).
extern "C" __declspec(dllexport) const char* NAME = "Starcraft: Manifold";
extern "C" __declspec(dllexport) const char* DESCRIPTION =
    "Tells the effect ManifoldUIMask.fx where the console is.";

namespace reshadebridge {

namespace {

const char* const EFFECT_FILE = "ManifoldUIMask.fx";
const char* const MASK_TEXTURE = "MaskTex";
//How often the renderer's thread checks that the mask texture is still the
//one it uploaded to (ReShade recreates it on a resize or when the effect's
//techniques are switched on).
const unsigned POLL_PRESENTS = 30;
//How often the game thread looks for ReShade while it is not loaded.
const unsigned REGISTER_TICKS = 100;

//Game thread only.
enum RegisterState { NOT_YET, REGISTERED, REFUSED };
RegisterState registerState = NOT_YET;
HMODULE registeredWith = NULL;
unsigned ticks = 0;

//Shared.
std::mutex maskLock;
std::vector<unsigned char> maskPixels;    //maskWidth * maskHeight
unsigned maskWidth = 0, maskHeight = 0;
std::atomic<bool> wantUpload(false);

//Renderer's thread only.
unsigned long long uploadedView = 0;      //the texture last uploaded to, 0 = none
unsigned presents = 0;

void upload(reshade::api::effect_runtime* runtime, bool force) {
  const reshade::api::effect_texture_variable texture =
      runtime->find_texture_variable(EFFECT_FILE, MASK_TEXTURE);
  reshade::api::resource_view view = {}, viewSrgb = {};
  if (texture.handle != 0)
    runtime->get_texture_binding(texture, &view, &viewSrgb);
  if (view.handle == 0) {
    uploadedView = 0;   //not loaded or not in use: the next poll looks again
    return;
  }
  if (!force && view.handle == uploadedView)
    return;

  std::lock_guard<std::mutex> guard(maskLock);
  //Nothing published yet: ReShade created the texture empty, which is right.
  if (maskWidth != 0 && maskHeight != 0)
    runtime->update_texture(texture, maskWidth, maskHeight, maskPixels.data());
  uploadedView = view.handle;
}

void onPresent(reshade::api::effect_runtime* runtime) {
  const bool wanted = wantUpload.exchange(false);
  if (wanted || ++presents % POLL_PRESENTS == 0)
    upload(runtime, wanted);
}

void onReloadedEffects(reshade::api::effect_runtime*) {
  wantUpload = true;    //every texture was recreated
}

HMODULE findReShade() {
  HMODULE modules[1024];
  DWORD bytes = 0;
  if (!K32EnumProcessModules(GetCurrentProcess(), modules, sizeof(modules), &bytes))
    return NULL;
  if (bytes > sizeof(modules))
    bytes = sizeof(modules);
  for (DWORD i = 0; i < bytes / sizeof(HMODULE); ++i)
    if (GetProcAddress(modules[i], "ReShadeRegisterAddon") != NULL &&
        GetProcAddress(modules[i], "ReShadeUnregisterAddon") != NULL)
      return modules[i];
  return NULL;
}

void tryRegister() {
  if (registerState != NOT_YET)
    return;
  const HMODULE reshadeModule = findReShade();
  if (reshadeModule == NULL)
    return;             //not loaded (yet): it comes with the renderer
  HMODULE self = NULL;
  GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                     reinterpret_cast<LPCSTR>(&findReShade), &self);
  if (self == NULL || !reshade::register_addon(self, reshadeModule)) {
    registerState = REFUSED;    //an older ReShade, or the add-on is disabled there
    return;
  }
  reshade::register_event<reshade::addon_event::reshade_present>(&onPresent);
  reshade::register_event<reshade::addon_event::reshade_reloaded_effects>(&onReloadedEffects);
  registeredWith = reshadeModule;
  registerState = REGISTERED;
}

} //unnamed namespace

void tick() {
  if (registerState == NOT_YET && ticks++ % REGISTER_TICKS == 0)
    tryRegister();
}

void publishMask(const unsigned char* pixels, unsigned width, unsigned height) {
  tryRegister();
  if (pixels == NULL || width == 0 || height == 0)
    return;
  {
    std::lock_guard<std::mutex> guard(maskLock);
    maskPixels.assign(pixels, pixels + static_cast<size_t>(width) * height);
    maskWidth = width;
    maskHeight = height;
  }
  wantUpload = true;
}

void clearMask() {
  tryRegister();
  {
    std::lock_guard<std::mutex> guard(maskLock);
    if (maskPixels.empty())
      return;           //never published: the texture is empty already
    memset(maskPixels.data(), 0, maskPixels.size());
  }
  wantUpload = true;
}

void shutdown() {
  //Only while that ReShade is still loaded: its handle is stale otherwise.
  if (registerState != REGISTERED || findReShade() != registeredWith)
    return;
  reshade::unregister_event<reshade::addon_event::reshade_present>(&onPresent);
  reshade::unregister_event<reshade::addon_event::reshade_reloaded_effects>(&onReloadedEffects);
  reshade::unregister_addon(NULL);
  registerState = REFUSED;
}

} //reshadebridge
```

Notes for the implementer:
- `reshade::register_addon(self, reshadeModule)` caches both handles in function-local
  statics inside ReShade's header; `unregister_addon(NULL)` and the event helpers then use
  the cached ones. That is why `tryRegister` never calls it before ReShade is found.
- `wanted` forces an upload to the same texture (new pixels); a poll uploads only when the
  texture's view handle changed.

- [ ] **Step 4: Add it to the build**

`GPTP/GPTP.vcxproj`: after `<ClCompile Include="hooks\interface\resolution_hud.cpp" />` add
`<ClCompile Include="hooks\interface\reshade_bridge.cpp" />`; after
`<ClInclude Include="hooks\interface\resolution.h" />` add
`<ClInclude Include="hooks\interface\reshade_bridge.h" />`.

Run: `powershell -ExecutionPolicy Bypass -File "D:\SC Modding\GPTP\tests\verify.ps1"`
Expected: three `PASS`, `naked wrappers: ok`, `plugin build succeeded`.

Then check the two exports:

```bash
cmd //c "\"C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars32.bat\" >nul 2>&1 && dumpbin /nologo /exports \"D:\SC Modding\GPTP\GPTP\Debug\GPTP.qdp\"" | grep -E "NAME|DESCRIPTION|GetMPQDraftPlugin"
```

Expected: three lines: `GetMPQDraftPlugin` at ordinal 100, `NAME` and `DESCRIPTION` (no
leading underscore).

- [ ] **Step 5: Commit**

```bash
git add GPTP/thirdparty/reshade GPTP/hooks/interface/reshade_bridge.h GPTP/hooks/interface/reshade_bridge.cpp GPTP/GPTP.vcxproj
git commit -m "UI mask: the plugin registers with ReShade and uploads the mask"
```

---

### Task 4: the glue

**Files:**
- Modify: `GPTP/hooks/interface/resolution_hud.cpp` (includes; after `widenConsoleImage()`,
  about line 447; the stub `consoleImageLoaded_4C39F5`, about line 476)
- Modify: `GPTP/hooks/main/game_hooks.cpp` (includes; `nextFrame()`; `gameEnd()`)
- Modify: `GPTP/Plugin.cpp` (`TerminatePlugin`)

**Interfaces:**
- Consumes: `uimask::Mask`, `uimask::reset`, `uimask::addConsoleImage` (Task 1);
  `reshadebridge::tick`, `publishMask`, `clearMask`, `shutdown` (Task 3).

- [ ] **Step 1: Publish the mask when the console image is ready**

`GPTP/hooks/interface/resolution_hud.cpp`, with the other includes:

```cpp
#include "reshade_bridge.h"
#include <SCBW/ui_mask.h>
```

Inside the unnamed namespace, right after `widenConsoleImage()`'s closing brace:

```cpp
//The console image as the game will use it, at any screen size: tell ReShade
//where the console is, so effects can be kept off it
//(docs/superpowers/specs/2026-10-11-reshade-ui-mask-design.md).
void __cdecl consoleImageLoaded() {
  widenConsoleImage();
  if (consoleImage->pixels == NULL)
    return;
  static uimask::Mask mask;
  uimask::reset(mask, consoleImage->width, consoleImage->height);
  uimask::addConsoleImage(mask, consoleImage->pixels);
  reshadebridge::publishMask(mask.pixels.data(), mask.width, mask.height);
}
```

In `consoleImageLoaded_4C39F5`, change `call widenConsoleImage` to
`call consoleImageLoaded`. The stub keeps its single call; nothing else in it changes.

- [ ] **Step 2: Register, and empty the mask when the game ends**

`GPTP/hooks/main/game_hooks.cpp`, with the other includes:

```cpp
#include "../interface/reshade_bridge.h"
```

At the very top of `hooks::nextFrame()`, before `if (!scbw::isGamePaused())`:

```cpp
    reshadebridge::tick(); // The plugin as a ReShade add-on: registers once ReShade is loaded
```

In `hooks::gameEnd()`, before `return true;`:

```cpp
    reshadebridge::clearMask(); // Outside a game ReShade's effects cover everything
```

- [ ] **Step 3: Unregister when the plugin ends**

`GPTP/Plugin.cpp`: add `#include "hooks/interface/reshade_bridge.h"` after
`#include "Plugin.h"`, and in `Plugin::TerminatePlugin()` replace the comment
`//By default, do nothing` with `reshadebridge::shutdown();`.

- [ ] **Step 4: Build**

Run: `powershell -ExecutionPolicy Bypass -File "D:\SC Modding\GPTP\tests\verify.ps1"`
Expected: three `PASS`, `naked wrappers: ok`, `plugin build succeeded`.

Re-read the three edits for: the include paths, `consoleImageLoaded` defined before the
stub that calls it, and no statement added inside a naked function.

- [ ] **Step 5: Commit**

```bash
git add GPTP/hooks/interface/resolution_hud.cpp GPTP/hooks/main/game_hooks.cpp GPTP/Plugin.cpp
git commit -m "UI mask: publish the console's mask at game start, empty it at game end"
```

---

### Task 5: docs, deploy, and the user's round

**Files:**
- Modify: `CLAUDE.md` ("Building", the sentence listing what `tests/verify.ps1` runs; "The
  fork's own modules", a new entry after Smart-build)
- Modify: `docs/superpowers/specs/2026-10-11-reshade-ui-mask-design.md` (status line)
- Copy (not in the repo): the effect into the game folder

- [ ] **Step 1: Docs**

`CLAUDE.md`, "Building": in the sentence about `tests/verify.ps1`, add the host test of the
UI mask (`tests/ui_mask_test.bat`) to the two it lists.

`CLAUDE.md`, "The fork's own modules", new entry:

```markdown
- **ReShade UI mask** (`SCBW/ui_mask*`, `hooks/interface/reshade_bridge*`, `reshade/`, spec
  `docs/superpowers/specs/2026-10-11-reshade-ui-mask-design.md`): the plugin registers with
  ReShade as an add-on, when ReShade is in the process, and uploads a mask of the console
  so `ManifoldUIMask.fx` can keep other effects off it. Cosmetic. ReShade calls back on
  the renderer's thread: only `reshade_bridge.cpp` includes ReShade's headers
  (`GPTP/thirdparty/reshade/`), and the game thread never calls ReShade's runtime.
```

Spec status line: "Built 2026-10-11 (plan `docs/superpowers/plans/2026-10-11-reshade-ui-mask.md`):
the mask's logic is host-tested; the rest is built and not yet run in game."

- [ ] **Step 2: Final build and the effect in the game folder**

```bash
touch "/d/SC Modding/GPTP/GPTP/hooks/main/game_hooks.cpp"
```

Run: `powershell -ExecutionPolicy Bypass -File "D:\SC Modding\GPTP\tests\verify.ps1"`
Expected: three `PASS`, `naked wrappers: ok`, `plugin build succeeded`. Read the build
stamp out of `D:\SC Modding\SCManifold\GPTP.qdp` (`built <date> <time>`).

```bash
cp "/d/SC Modding/GPTP/reshade/reshade-shaders/Shaders/ManifoldUIMask.fx" "/d/Games/Starcraft 1.16.1/reshade-shaders/Shaders/"
```

The user's own `ReShadePreset.ini` in the game folder is not touched.

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md docs/superpowers/specs/2026-10-11-reshade-ui-mask-design.md
git commit -m "Docs: the ReShade UI mask is built, not yet run in game"
```

- [ ] **Step 4: The user's round (round 4)**

Give the user the build stamp and this list. Before it: repack, start the game, and in the
overlay switch off `UIMask_Top`/`UIMask_Bottom` (the third-party pair), enable
`ManifoldMask_Top` above the effects and `ManifoldMask_Bottom` below them, and tick
ManifoldUIMask's "Display Mask".

After the user's first launch, read `ReShade.log` before anything else: it must show
`Successfully compiled ...ManifoldUIMask.fx` and `Registered add-on "Starcraft: Manifold"`.
A compile error there is fixed in `reshade/.../ManifoldUIMask.fx` and copied again; it
needs no plugin rebuild.

1. Terran game: white over the console at the **bottom** of the screen, in the console's
   exact outline; black over the game view; no white specks in the view, no black holes in
   the console.
2. Zerg, Protoss and a replay: the same, each with its own outline, without touching ReShade.
3. Restart the mission, and load a save of another race: the mask is the new console's.
4. Back in the menus: all black.
5. The overlay's Reload, in game: the mask comes back.
6. Resize the window or Alt+Tab away and back, in game: the mask is back within a second.
7. Start a game with both techniques off, then enable them: the mask appears.
8. Display Mask off, a strong effect between the two: the view shows it, the console does
   not, and buttons, wireframes and the minimap update as usual.
9. ReShade's Add-ons tab lists "Starcraft: Manifold".
10. Another resolution in `Manifold.ini`, restart: the mask still fits.
11. Exactly 640x480 in `Manifold.ini`: the mask fits the vanilla-sized console.
12. `opengl32.dll` renamed away: the game runs as before, no message, no slowdown. Rename
    it back.
13. Any crash, hang or new stutter in any of the above?

- [ ] **Step 5: After the round passes**

- Delete from the game folder what the earlier attempts left:
  `reshade-shaders\make_uimask.py` and `reshade-shaders\Shaders\UIMask.fx`.
- Spec status line: what the round showed, and any `[VERIFY]` it settled.
- Memory: update `manifold-test-setup` (the mask effect, the add-on) and add a note for
  this feature.
- Commit the spec change, then use superpowers:finishing-a-development-branch
  (fast-forward into local `master`; never push).
