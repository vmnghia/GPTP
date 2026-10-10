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
enum class RegisterState { NotYet, Registered, Refused };
RegisterState registerState = RegisterState::NotYet;
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
  if (registerState != RegisterState::Registered || findReShade() != registeredWith)
    return;
  reshade::unregister_event<reshade::addon_event::reshade_present>(&onPresent);
  reshade::unregister_event<reshade::addon_event::reshade_reloaded_effects>(&onReloadedEffects);
  reshade::unregister_addon(NULL);
  registerState = RegisterState::Refused;
}

} //reshadebridge
