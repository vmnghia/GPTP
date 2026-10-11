//ReShade calls back on the renderer's thread, so the two threads only share
//the effects area's buffer (under areaLock) and one flag. The game thread
//never calls ReShade's runtime; uploads happen in onPresent.
//
//The texture holds where effects apply (255) and where the frame from before
//them is kept (0). ReShade creates it zeroed, so until the first upload, and
//again from the end of each game, there are no effects anywhere: the menus
//stay free of them without the plugin having to run there.
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
const char* const AREA_TEXTURE = "EffectsTex";
//How often the renderer's thread checks that the texture is still the one it
//uploaded to (ReShade recreates it on a resize or when the effect's
//techniques are switched on).
const unsigned POLL_PRESENTS = 30;
//How often the game thread looks for ReShade while it is not loaded.
const unsigned REGISTER_TICKS = 100;

//Game thread only.
enum class RegisterState { NotYet, Registered, Refused };
RegisterState registerState = RegisterState::NotYet;
HMODULE registeredWith = NULL;
unsigned ticks = 0;

//Shared.
std::mutex areaLock;
std::vector<unsigned char> areaPixels;    //areaWidth * areaHeight
unsigned areaWidth = 0, areaHeight = 0;
std::atomic<bool> wantUpload(false);

//Renderer's thread only.
unsigned long long uploadedView = 0;      //the texture last uploaded to, 0 = none
unsigned presents = 0;

void upload(reshade::api::effect_runtime* runtime, bool force) {
  const reshade::api::effect_texture_variable texture =
      runtime->find_texture_variable(EFFECT_FILE, AREA_TEXTURE);
  reshade::api::resource_view view = {}, viewSrgb = {};
  if (texture.handle != 0)
    runtime->get_texture_binding(texture, &view, &viewSrgb);
  if (view.handle == 0) {
    uploadedView = 0;   //not loaded or not in use: the next poll looks again
    return;
  }
  if (!force && view.handle == uploadedView)
    return;

  std::lock_guard<std::mutex> guard(areaLock);
  //Nothing published yet: ReShade created the texture zeroed, no effects
  //anywhere, which is right outside a game.
  if (areaWidth != 0 && areaHeight != 0)
    runtime->update_texture(texture, areaWidth, areaHeight, areaPixels.data());
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
  if (registerState != RegisterState::NotYet)
    return;
  const HMODULE reshadeModule = findReShade();
  if (reshadeModule == NULL)
    return;             //not loaded (yet): it comes with the renderer
  HMODULE self = NULL;
  GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                     reinterpret_cast<LPCSTR>(&findReShade), &self);
  if (self == NULL || !reshade::register_addon(self, reshadeModule)) {
    registerState = RegisterState::Refused;    //an older ReShade, or the add-on is disabled there
    return;
  }
  reshade::register_event<reshade::addon_event::reshade_present>(&onPresent);
  reshade::register_event<reshade::addon_event::reshade_reloaded_effects>(&onReloadedEffects);
  registeredWith = reshadeModule;
  registerState = RegisterState::Registered;
}

} //unnamed namespace

void tick() {
  if (registerState == RegisterState::NotYet && ticks++ % REGISTER_TICKS == 0)
    tryRegister();
}

void publishEffectsArea(const unsigned char* pixels, unsigned width, unsigned height) {
  tryRegister();
  if (pixels == NULL || width == 0 || height == 0)
    return;
  {
    std::lock_guard<std::mutex> guard(areaLock);
    areaPixels.assign(pixels, pixels + static_cast<size_t>(width) * height);
    areaWidth = width;
    areaHeight = height;
  }
  wantUpload = true;
}

void clearEffectsArea() {
  tryRegister();
  {
    std::lock_guard<std::mutex> guard(areaLock);
    if (areaPixels.empty())
      return;           //never published: the texture is zeroed already
    memset(areaPixels.data(), 0, areaPixels.size());
  }
  wantUpload = true;
}

void shutdown() {
  //Only while that ReShade is still loaded: its handle is stale otherwise.
  if (registerState != RegisterState::Registered || findReShade() != registeredWith)
    return;
  reshade::unregister_event<reshade::addon_event::reshade_present>(&onPresent);
  reshade::unregister_event<reshade::addon_event::reshade_reloaded_effects>(&onReloadedEffects);
  reshade::unregister_addon(NULL);
  registerState = RegisterState::Refused;
}

} //reshadebridge
