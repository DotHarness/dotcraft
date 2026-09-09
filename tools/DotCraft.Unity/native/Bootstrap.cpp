#include <windows.h>
#include <string>

struct DomainSearch { void* found; const char* (*name)(void*); };
static void FindDomain(void* domain, void* state)
{
    auto search = static_cast<DomainSearch*>(state);
    if (std::string(search->name(domain)) == "Unity Child Domain") search->found = domain;
}

static std::string Utf8(const std::wstring& value)
{
    int size = WideCharToMultiByte(CP_UTF8, 0, value.c_str(), -1, nullptr, 0, nullptr, nullptr);
    std::string result(size, '\0');
    WideCharToMultiByte(CP_UTF8, 0, value.c_str(), -1, &result[0], size, nullptr, nullptr);
    return result;
}

extern "C" __declspec(dllexport) DWORD WINAPI Bootstrap(void* argument)
{
    HMODULE mono = GetModuleHandleW(L"mono-2.0-bdwgc.dll");
    if (!mono) mono = GetModuleHandleW(L"mono-2.0-sgen.dll");
    if (!mono) return 1;
#define API(name, type) auto name = reinterpret_cast<type>(GetProcAddress(mono, #name)); if (!name) return 2
    API(mono_get_root_domain, void* (*)());
    API(mono_thread_attach, void* (*)(void*));
    API(mono_thread_detach, void (*)(void*));
    API(mono_domain_foreach, void (*)(void (*)(void*, void*), void*));
    API(mono_domain_set, int (*)(void*, int));
    API(mono_domain_assembly_open, void* (*)(void*, const char*));
    API(mono_assembly_get_image, void* (*)(void*));
    API(mono_class_from_name, void* (*)(void*, const char*, const char*));
    API(mono_class_get_method_from_name, void* (*)(void*, const char*, int));
    API(mono_string_new, void* (*)(void*, const char*));
    API(mono_runtime_invoke, void* (*)(void*, void*, void**, void**));
    DomainSearch search { nullptr, reinterpret_cast<const char* (*)(void*)>(GetProcAddress(mono, "mono_domain_get_friendly_name")) };
    if (!search.name) return 2;
    std::wstring input(static_cast<wchar_t*>(argument));
    auto separator = input.find(L'\n');
    if (separator == std::wstring::npos) return 3;
    auto path = Utf8(input.substr(0, separator));
    auto config = Utf8(input.substr(separator + 1));
    void* thread = mono_thread_attach(mono_get_root_domain());
    if (!thread) return 8;
    mono_domain_foreach(FindDomain, &search);
    void* scriptDomain = search.found;
    DWORD status = 4;
    if (scriptDomain && mono_domain_set(scriptDomain, 0))
    {
        auto assembly = mono_domain_assembly_open(scriptDomain, path.c_str());
        status = 5;
        if (assembly)
        {
            auto klass = mono_class_from_name(mono_assembly_get_image(assembly), "DotCraft.Unity", "Entry");
            auto method = klass ? mono_class_get_method_from_name(klass, "Initialize", 1) : nullptr;
            status = 6;
            if (method)
            {
                void* args[] = { mono_string_new(scriptDomain, config.c_str()) };
                void* exception = nullptr;
                mono_runtime_invoke(method, nullptr, args, &exception);
                status = exception ? 7 : 0;
            }
        }
    }
    mono_thread_detach(thread);
    return status;
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH) DisableThreadLibraryCalls(instance);
    return TRUE;
}
