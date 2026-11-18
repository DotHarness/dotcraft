#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <node_api.h>
#include <algorithm>
#include <atomic>
#include <cstring>
#include <stdexcept>
#include <string>
#include <vector>
#include "vendor/minhook/include/MinHook.h"
#include "electron-42-win32-x64.h"

using Pointer = unsigned char*;
using Void2 = void(*)(Pointer, Pointer);
using Void3 = void(*)(Pointer, Pointer, Pointer);
using Pointer1 = Pointer(*)(Pointer);
using Pointer2 = Pointer(*)(Pointer, Pointer);
using MouseHandler = bool(*)(Pointer, Pointer, Pointer);

namespace {

Pointer executable;
DWORD uiThread;
thread_local unsigned programmaticDepth;

struct Guest {
  Pointer contents;
  Pointer tree;
  Pointer outerNode;
  int processId;
  int routingId;
  bool active;
};
std::vector<Guest> guests;

struct Counters {
  uint64_t focusTreeCalls, focusTreeBlocked, focusFrameCalls, focusFrameBlocked;
  uint64_t focusedWidgetCalls, focusedWidgetLocal, pageWidgetCalls, pageWidgetLocal;
  uint64_t mouseCalls, debuggerMouse, programmaticMouse, userTakeovers;
  uint64_t sendInputCalls, explicitUnregisters;
  std::atomic<uint64_t> wrongThreadCalls;
} counters{};

Void2 originalTree;
Void3 originalFrame, originalSend;
Pointer2 originalFocused;
Pointer1 originalPage;
MouseHandler originalMouse;

template<class T> T read(Pointer object, size_t offset) {
  return *reinterpret_cast<T*>(object + offset);
}
template<class T> T function(electron42::Address address) {
  return reinterpret_cast<T>(executable + address);
}
bool onUiThread() {
  if (GetCurrentThreadId() == uiThread) return true;
  ++counters.wrongThreadCalls;
  return false;
}
void requireUiThread() {
  if (!onUiThread()) throw std::runtime_error("Browser UI thread required.");
}
Guest* findGuest(Pointer contents) {
  auto found = std::find_if(guests.begin(), guests.end(), [&](const Guest& guest) {
    return guest.contents == contents;
  });
  return found == guests.end() ? nullptr : &*found;
}

void focusTree(Pointer self, Pointer tree) {
  if (onUiThread()) {
    ++counters.focusTreeCalls;
    for (const auto& guest : guests) {
      if (guest.active && tree == guest.tree) {
        ++counters.focusTreeBlocked;
        return;
      }
    }
  }
  originalTree(self, tree);
}
void focusFrame(Pointer self, Pointer node, Pointer source) {
  if (onUiThread()) {
    ++counters.focusFrameCalls;
    for (const auto& guest : guests) {
      if (guest.active && node == guest.outerNode) {
        ++counters.focusFrameBlocked;
        return;
      }
    }
  }
  originalFrame(self, node, source);
}
Pointer focusedWidget(Pointer self, Pointer receiving) {
  if (onUiThread()) {
    ++counters.focusedWidgetCalls;
    auto* guest = findGuest(self - electron42::WidgetDelegateOffset);
    if (guest && guest->active) {
      ++counters.focusedWidgetLocal;
      auto primary = function<Pointer1>(electron42::TreeMain)(guest->tree);
      if (receiving != function<Pointer1>(electron42::FrameWidget)(primary)) return receiving;
      auto focused = function<Pointer1>(electron42::TreeFocused)(guest->tree);
      if (!focused) return receiving;
      auto frame = read<Pointer>(focused, electron42::CurrentFrameOffset);
      if (!function<Pointer1>(electron42::FrameView)(frame)) return nullptr;
      return function<Pointer1>(electron42::FrameWidget)(frame);
    }
  }
  return originalFocused(self, receiving);
}
Pointer pageWidget(Pointer self) {
  if (onUiThread()) {
    ++counters.pageWidgetCalls;
    auto* guest = findGuest(self - electron42::WidgetDelegateOffset);
    if (guest && guest->active) {
      ++counters.pageWidgetLocal;
      return function<Pointer1>(electron42::FrameWidget)(
          function<Pointer1>(electron42::TreeMain)(guest->tree));
    }
  }
  return originalPage(self);
}
bool beforeMouse(Pointer self, Pointer source, Pointer event) {
  if (onUiThread()) {
    ++counters.mouseCalls;
    auto* guest = findGuest(source);
    if (guest && guest->active &&
        read<int>(event, electron42::InputTypeOffset) == electron42::MouseDown) {
      if (read<int>(event, electron42::InputModifiersOffset) & electron42::FromDebugger) {
        ++counters.debuggerMouse;
      } else if (programmaticDepth) {
        ++counters.programmaticMouse;
      } else {
        guest->active = false;
        ++counters.userTakeovers;
      }
    }
  }
  return originalMouse(self, source, event);
}
void sendInput(Pointer self, Pointer isolate, Pointer value) {
  ++counters.sendInputCalls;
  ++programmaticDepth;
  originalSend(self, isolate, value);
  --programmaticDepth;
}

void validateRuntime() {
  auto dos = reinterpret_cast<IMAGE_DOS_HEADER*>(executable);
  auto nt = reinterpret_cast<IMAGE_NT_HEADERS64*>(executable + dos->e_lfanew);
  if (nt->FileHeader.Machine != IMAGE_FILE_MACHINE_AMD64)
    throw std::runtime_error("Browser focus isolation requires Windows x64.");
  auto directory = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_DEBUG];
  auto entries = reinterpret_cast<IMAGE_DEBUG_DIRECTORY*>(executable + directory.VirtualAddress);
  constexpr unsigned char guid[] = {
    0x94,0x7a,0x46,0x31,0x4b,0xcc,0x5f,0x13,0x4c,0x4c,0x44,0x20,0x50,0x44,0x42,0x2e
  };
  bool matches = false;
  for (size_t i = 0; i < directory.Size / sizeof(*entries); ++i) {
    const auto& entry = entries[i];
    auto codeView = executable + entry.AddressOfRawData;
    if (entry.Type == IMAGE_DEBUG_TYPE_CODEVIEW && entry.SizeOfData >= 24 &&
        !std::memcmp(codeView, "RSDS", 4) &&
        !std::memcmp(codeView + 4, guid, sizeof(guid)) &&
        read<DWORD>(codeView, 20) == 1) {
      matches = true;
    }
  }
  if (!matches)
    throw std::runtime_error("Unsupported Electron build for browser focus isolation.");
  for (const auto& guard : electron42::Guards) {
    if (std::memcmp(executable + guard.address, guard.bytes, sizeof(guard.bytes)))
      throw std::runtime_error(std::string("Browser focus code mismatch: ") + guard.name);
  }
}
template<class T> void hook(electron42::Address address, void* handler, T* original) {
  auto status = MH_CreateHook(executable + address, handler, reinterpret_cast<void**>(original));
  if (status != MH_OK)
    throw std::runtime_error(std::string("Browser focus hook failed: ") + MH_StatusToString(status));
}
void install() {
  validateRuntime();
  if (MH_Initialize() != MH_OK) throw std::runtime_error("Browser focus hook initialization failed.");
  try {
    hook(electron42::FocusTree, reinterpret_cast<void*>(&focusTree), &originalTree);
    hook(electron42::FocusFrame, reinterpret_cast<void*>(&focusFrame), &originalFrame);
    hook(electron42::FocusedWidget, reinterpret_cast<void*>(&focusedWidget), &originalFocused);
    hook(electron42::PageWidget, reinterpret_cast<void*>(&pageWidget), &originalPage);
    hook(electron42::PreMouse, reinterpret_cast<void*>(&beforeMouse), &originalMouse);
    hook(electron42::SendInput, reinterpret_cast<void*>(&sendInput), &originalSend);
    if (MH_EnableHook(MH_ALL_HOOKS) != MH_OK)
      throw std::runtime_error("Browser focus hook activation failed.");
  } catch (...) {
    MH_DisableHook(MH_ALL_HOOKS);
    MH_Uninitialize();
    throw;
  }
}

#define API(name) decltype(&name) api_##name
API(napi_get_cb_info); API(napi_get_value_int32); API(napi_get_value_bool);
API(napi_throw_error); API(napi_create_object); API(napi_create_uint32);
API(napi_set_named_property); API(napi_create_function); API(napi_get_boolean);
#undef API

void check(napi_status status) {
  if (status != napi_ok) throw std::runtime_error("Invalid browser focus argument.");
}
napi_value boolean(napi_env env, bool value) {
  napi_value result;
  check(api_napi_get_boolean(env, value, &result));
  return result;
}
napi_value error(napi_env env, const char* message) {
  api_napi_throw_error(env, nullptr, message);
  return nullptr;
}
napi_value setIsolation(napi_env env, napi_callback_info info) {
  try {
    requireUiThread();
    napi_value args[3];
    size_t count = 3;
    check(api_napi_get_cb_info(env, info, &count, args, nullptr, nullptr));
    if (count != 3) throw std::runtime_error("setIsolation(processId, routingId, enabled) required.");
    int processId, routingId;
    bool active;
    check(api_napi_get_value_int32(env, args[0], &processId));
    check(api_napi_get_value_int32(env, args[1], &routingId));
    check(api_napi_get_value_bool(env, args[2], &active));
    using FromId = Pointer(*)(int, int);
    auto frame = function<FromId>(electron42::FromId)(processId, routingId);
    if (!frame) throw std::runtime_error("Browser frame not found.");
    auto contents = function<Pointer1>(electron42::FromFrame)(frame);
    if (!contents || function<Pointer1>(electron42::PrimaryFrame)(contents) != frame)
      throw std::runtime_error("Primary main frame required for browser focus isolation.");
    auto outer = function<Pointer1>(electron42::OuterFrame)(contents);
    if (!outer) throw std::runtime_error("Attached browser guest required for focus isolation.");
    int nodeId = -1;
    using NodeId = int*(*)(Pointer, int*);
    function<NodeId>(electron42::OuterNodeId)(
        contents + electron42::ContentsDelegateOffset, &nodeId);
    using FindNode = Pointer(*)(int);
    auto node = function<FindNode>(electron42::FindNode)(nodeId);
    auto tree = contents + electron42::PrimaryTreeOffset;
    if (!node || read<Pointer>(node, electron42::OuterFrameOffset) != outer ||
        function<Pointer1>(electron42::TreeMain)(tree) != frame)
      throw std::runtime_error("Unsupported browser guest layout.");
    Guest value{contents, tree, node, processId, routingId, active};
    if (auto* guest = findGuest(contents)) *guest = value;
    else guests.push_back(value);
    return boolean(env, active);
  } catch (const std::exception& failure) {
    return error(env, failure.what());
  }
}
napi_value unregisterGuest(napi_env env, napi_callback_info info) {
  try {
    requireUiThread();
    napi_value args[2];
    size_t count = 2;
    int processId, routingId;
    check(api_napi_get_cb_info(env, info, &count, args, nullptr, nullptr));
    if (count != 2) throw std::runtime_error("unregister(processId, routingId) required.");
    check(api_napi_get_value_int32(env, args[0], &processId));
    check(api_napi_get_value_int32(env, args[1], &routingId));
    auto previous = guests.size();
    std::erase_if(guests, [&](const Guest& guest) {
      return guest.processId == processId && guest.routingId == routingId;
    });
    counters.explicitUnregisters += previous - guests.size();
    return boolean(env, previous != guests.size());
  } catch (const std::exception& failure) {
    return error(env, failure.what());
  }
}
void number(napi_env env, napi_value object, const char* key, uint64_t value) {
  napi_value result;
  check(api_napi_create_uint32(env, static_cast<uint32_t>(value), &result));
  check(api_napi_set_named_property(env, object, key, result));
}
napi_value diagnostics(napi_env env, napi_callback_info) {
  try {
    requireUiThread();
    napi_value result;
    check(api_napi_create_object(env, &result));
    number(env, result, "installed", 1);
    number(env, result, "registeredGuests", guests.size());
    number(env, result, "activeGuests",
           std::count_if(guests.begin(), guests.end(), [](const Guest& guest) { return guest.active; }));
#define COUNT(name) number(env, result, #name, counters.name)
    COUNT(focusTreeCalls); COUNT(focusTreeBlocked); COUNT(focusFrameCalls); COUNT(focusFrameBlocked);
    COUNT(focusedWidgetCalls); COUNT(focusedWidgetLocal); COUNT(pageWidgetCalls); COUNT(pageWidgetLocal);
    COUNT(mouseCalls); COUNT(debuggerMouse); COUNT(programmaticMouse); COUNT(userTakeovers);
    COUNT(sendInputCalls); COUNT(explicitUnregisters); COUNT(wrongThreadCalls);
#undef COUNT
    return result;
  } catch (const std::exception& failure) {
    return error(env, failure.what());
  }
}

}

extern "C" __declspec(dllexport) napi_value napi_register_module_v1(napi_env env, napi_value exports) {
  executable = reinterpret_cast<Pointer>(GetModuleHandleW(nullptr));
  uiThread = GetCurrentThreadId();
#define LOAD(name) api_##name = reinterpret_cast<decltype(api_##name)>(GetProcAddress(reinterpret_cast<HMODULE>(executable), #name))
  LOAD(napi_throw_error);
  try {
    LOAD(napi_get_cb_info); LOAD(napi_get_value_int32); LOAD(napi_get_value_bool);
    LOAD(napi_create_object); LOAD(napi_create_uint32); LOAD(napi_set_named_property);
    LOAD(napi_create_function); LOAD(napi_get_boolean);
#undef LOAD
    install();
    struct Export { const char* name; napi_callback callback; };
    Export methods[] = {
      {"setIsolation", setIsolation}, {"unregister", unregisterGuest},
      {"diagnostics", diagnostics}
    };
    for (const auto& method : methods) {
      napi_value callback;
      check(api_napi_create_function(env, method.name, NAPI_AUTO_LENGTH, method.callback, nullptr, &callback));
      check(api_napi_set_named_property(env, exports, method.name, callback));
    }
    return exports;
  } catch (const std::exception& failure) {
    return error(env, failure.what());
  }
}
