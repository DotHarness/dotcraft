#include <windows.h>
#include <algorithm>
#include <array>
#include <cstdint>
#include <cstring>
#include <limits>
#include <vector>
#include "MinHook.h"
#include "node_api.h"

namespace {

constexpr size_t AddMenuItemAtRva = 0xaeb05a0;
constexpr std::array<unsigned char, 16> PdbId{
  0x94, 0x7a, 0x46, 0x31, 0x4b, 0xcc, 0x5f, 0x13, 0x4c, 0x4c, 0x44, 0x20, 0x50, 0x44, 0x42, 0x2e
};
constexpr std::array<unsigned char, 16> EntryCode{
  0x41, 0x57, 0x41, 0x56, 0x41, 0x55, 0x41, 0x54, 0x56, 0x57, 0x55, 0x53, 0x48, 0x81, 0xec, 0x88
};
const DWORD uiThread = GetCurrentThreadId();
std::vector<int32_t> commandIds;

#define NAPI_FUNCTIONS(X) \
  X(napi_get_cb_info) \
  X(napi_is_array) \
  X(napi_get_array_length) \
  X(napi_get_element) \
  X(napi_get_value_double) \
  X(napi_get_boolean) \
  X(napi_create_function) \
  X(napi_set_named_property)
#define DECLARE(name) decltype(&name) api_##name;
NAPI_FUNCTIONS(DECLARE)
#undef DECLARE

// Forward Chromium objects opaquely and preserve the Windows x64 argument slots for optional colors.
using AddItem = void* (*)(void*, size_t, int, const void*, const void*, const void*, const void*,
                          const void*, int, int, uint64_t, uint64_t, uint64_t);
AddItem original = nullptr;

void* addItem(void* self, size_t index, int id, const void* label, const void* secondary,
              const void* minor, const void* minorIcon, const void* icon, int type, int separator,
              uint64_t background, uint64_t foreground, uint64_t selection) {
  if (GetCurrentThreadId() == uiThread && std::binary_search(commandIds.begin(), commandIds.end(), id)) {
    std::swap(secondary, minor);
  }
  return original(self, index, id, label, secondary, minor, minorIcon, icon, type, separator,
                  background, foreground, selection);
}

bool matchesBuild(const unsigned char* base) {
  if (!base) return false;
  const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
  if (dos->e_magic != IMAGE_DOS_SIGNATURE || dos->e_lfanew <= 0) return false;
  const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS64*>(base + dos->e_lfanew);
  if (nt->Signature != IMAGE_NT_SIGNATURE || nt->FileHeader.Machine != IMAGE_FILE_MACHINE_AMD64 ||
      nt->OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR64_MAGIC ||
      nt->OptionalHeader.NumberOfRvaAndSizes <= IMAGE_DIRECTORY_ENTRY_DEBUG) return false;
  const auto imageSize = nt->OptionalHeader.SizeOfImage;
  if (imageSize < AddMenuItemAtRva + EntryCode.size()) return false;
  const auto debug = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_DEBUG];
  if (!debug.VirtualAddress || debug.VirtualAddress >= imageSize ||
      debug.Size > imageSize - debug.VirtualAddress) return false;

  const auto* entries = reinterpret_cast<const IMAGE_DEBUG_DIRECTORY*>(base + debug.VirtualAddress);
  for (size_t i = 0; i < debug.Size / sizeof(*entries); ++i) {
    const auto& entry = entries[i];
    if (entry.Type != IMAGE_DEBUG_TYPE_CODEVIEW || entry.SizeOfData < 24 ||
        !entry.AddressOfRawData || entry.AddressOfRawData >= imageSize ||
        entry.SizeOfData > imageSize - entry.AddressOfRawData) continue;
    const auto* record = base + entry.AddressOfRawData;
    DWORD age = 0;
    std::memcpy(&age, record + 20, sizeof(age));
    if (std::memcmp(record, "RSDS", 4) == 0 &&
        std::memcmp(record + 4, PdbId.data(), PdbId.size()) == 0 && age == 1)
      return std::memcmp(base + AddMenuItemAtRva, EntryCode.data(), EntryCode.size()) == 0;
  }
  return false;
}

bool installHook() {
  auto* base = reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
  if (!matchesBuild(base) || MH_Initialize() != MH_OK) return false;
  auto* target = base + AddMenuItemAtRva;
  if (MH_CreateHook(target, reinterpret_cast<void*>(&addItem), reinterpret_cast<void**>(&original)) == MH_OK &&
      MH_EnableHook(target) == MH_OK) return true;
  MH_Uninitialize();
  original = nullptr;
  return false;
}

bool replaceCommandIds(napi_env env, napi_callback_info info) {
  if (GetCurrentThreadId() != uiThread) return false;
  size_t count = 1;
  napi_value value{};
  bool isArray = false;
  uint32_t length = 0;
  if (api_napi_get_cb_info(env, info, &count, &value, nullptr, nullptr) != napi_ok ||
      count != 1 || api_napi_is_array(env, value, &isArray) != napi_ok || !isArray ||
      api_napi_get_array_length(env, value, &length) != napi_ok) return false;

  std::vector<int32_t> ids;
  ids.reserve(length);
  for (uint32_t i = 0; i < length; ++i) {
    napi_value item{};
    double number = 0;
    if (api_napi_get_element(env, value, i, &item) != napi_ok ||
        api_napi_get_value_double(env, item, &number) != napi_ok ||
        !(number >= 1 && number <= (std::numeric_limits<int32_t>::max)())) return false;
    const auto id = static_cast<int32_t>(number);
    if (id != number) return false;
    ids.push_back(id);
  }
  std::sort(ids.begin(), ids.end());
  ids.erase(std::unique(ids.begin(), ids.end()), ids.end());
  if (!ids.empty() && !original && !installHook()) return false;
  commandIds.swap(ids);
  return true;
}

napi_value setCommandIds(napi_env env, napi_callback_info info) noexcept {
  bool success = false;
  try {
    success = replaceCommandIds(env, info);
  } catch (...) {
  }
  napi_value result{};
  api_napi_get_boolean(env, success, &result);
  return result;
}

}

extern "C" __declspec(dllexport) napi_value napi_register_module_v1(napi_env env, napi_value exports) {
  if (GetCurrentThreadId() != uiThread) return nullptr;
  const auto executable = GetModuleHandleW(nullptr);
#define LOAD(name) \
  api_##name = reinterpret_cast<decltype(api_##name)>(GetProcAddress(executable, #name)); \
  if (!api_##name) return nullptr;
  NAPI_FUNCTIONS(LOAD)
#undef LOAD
#undef NAPI_FUNCTIONS
  napi_value function{};
  if (api_napi_create_function(env, "setCommandIds", NAPI_AUTO_LENGTH, setCommandIds, nullptr, &function) != napi_ok ||
      api_napi_set_named_property(env, exports, "setCommandIds", function) != napi_ok)
    return nullptr;
  return exports;
}
