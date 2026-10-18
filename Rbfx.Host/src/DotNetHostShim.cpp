// Shim implementation of DotNetHost for Android, iOS and Emscripten.
// It never starts a runtime. It calls the function pointers that the managed side attached.

#include "Urho3D/CSharp/DotNetHost.h"
#include "Urho3D/CSharp/DotNetHostShim.h"
#include "HostPaths.h"

#include <Urho3D/Container/Str.h>

#include <exception>
#include <mutex>

// Desktop shim: the native side loads the NativeAOT head library. Mobile and web heads attach by themselves.
#if defined(__APPLE__)
#include <TargetConditionals.h>
#endif
#if defined(_WIN32) || (defined(__linux__) && !defined(__ANDROID__)) || (defined(__APPLE__) && TARGET_OS_OSX)
#define SHARPCPP_RBFX_SHIM_LOAD_HEAD 1
#if defined(_WIN32)
#ifndef NOMINMAX
#define NOMINMAX
#endif
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#else
#include <dlfcn.h>
#endif
#endif

namespace Urho3D
{

namespace
{

constexpr const char* NotAttachedError =
    "The managed runtime is not attached. Touch the SharpCpp.Rbfx bindings assembly in the .NET head before DotNetHost::Initialize.";

std::mutex& TableMutex()
{
    static std::mutex mutex;
    return mutex;
}

// The managed side finds sharpcpp_rbfx_attach_managed_host by name, and no native code calls it.
// A pointer that Initialize reads makes the export reachable, so the linker keeps it when it
// removes unused code (for example dead_strip on iOS).
using AttachFunction = int (*)(const SharpCppRbfxManagedHost*);
AttachFunction volatile KeepAttachExport = &::sharpcpp_rbfx_attach_managed_host;

SharpCppRbfxManagedHost& Table()
{
    static SharpCppRbfxManagedHost table{};
    return table;
}

bool IsAttachedLocked()
{
    return Table().getFunctionPointer != nullptr && Table().loadPlugin != nullptr;
}

SharpCppRbfxManagedHost CopyTable()
{
    std::lock_guard<std::mutex> lock(TableMutex());
    return Table();
}

void AppendManagedError(void* state, const char* text, int length)
{
    if (state && text && length > 0)
        static_cast<ea::string*>(state)->append(text, static_cast<std::size_t>(length));
}

#if defined(SHARPCPP_RBFX_SHIM_LOAD_HEAD)
using HeadAttachFunction = int (*)();
using HeadDetachFunction = void (*)();

void* LoadHeadLibrary(const ea::string& path, ea::string& reason)
{
#if defined(_WIN32)
    void* library = reinterpret_cast<void*>(LoadLibraryW(MultiByteToWide(path).c_str()));
    if (!library)
        reason = ea::string(ea::string::CtorSprintf{}, "LoadLibrary failed with error code %lu", static_cast<unsigned long>(GetLastError()));
    return library;
#else
    void* library = dlopen(path.c_str(), RTLD_LAZY | RTLD_LOCAL);
    if (!library)
    {
        const char* message = dlerror();
        reason = message ? message : "unknown error";
    }
    return library;
#endif
}

void* FindHeadExport(void* library, const char* name)
{
#if defined(_WIN32)
    return reinterpret_cast<void*>(GetProcAddress(static_cast<HMODULE>(library), name));
#else
    return dlsym(library, name);
#endif
}

// Loads the head library and calls its attach export. The handle stays loaded: a NativeAOT runtime cannot unload.
bool AttachHead(const ea::string& path, void*& handle, ea::string& error)
{
    ea::string reason;
    void* library = LoadHeadLibrary(path, reason);
    if (!library)
    {
        error = "Cannot load the NativeAOT head library '" + path + "': " + reason;
        return false;
    }
    const auto attach = reinterpret_cast<HeadAttachFunction>(FindHeadExport(library, "sharpcpp_rbfx_head_attach"));
    if (!attach)
    {
        error = "The NativeAOT head library '" + path + "' has no export 'sharpcpp_rbfx_head_attach'.";
        return false;
    }
    if (attach() != 1)
    {
        error = "The export 'sharpcpp_rbfx_head_attach' of '" + path + "' failed.";
        return false;
    }
    handle = library;
    return true;
}

void DetachHead(void* handle)
{
    if (const auto detach = reinterpret_cast<HeadDetachFunction>(FindHeadExport(handle, "sharpcpp_rbfx_head_detach")))
        detach();
}
#endif

} // namespace

DotNetHost::DotNetHost() = default;

DotNetHost::~DotNetHost()
{
    Close();
}

bool DotNetHost::IsInitialized() const
{
    return shimInitialized_;
}

bool DotNetHost::Initialize(const DotNetHostOptions& options, ea::string& error)
{
    error.clear();
    const AttachFunction keepAttachExport = KeepAttachExport;
    (void)keepAttachExport;
    try
    {
        bool attached;
        {
            std::lock_guard<std::mutex> lock(TableMutex());
            attached = IsAttachedLocked();
        }
#if defined(SHARPCPP_RBFX_SHIM_LOAD_HEAD)
        // The shim has no hostfxr library. It keeps the handle of the head library in hostfxrLibrary_.
        if (!attached && !options.headLibraryPath.empty())
        {
            if (!AttachHead(options.headLibraryPath, hostfxrLibrary_, error))
                return false;
            std::lock_guard<std::mutex> lock(TableMutex());
            attached = IsAttachedLocked();
            if (!attached)
            {
                error = "The NativeAOT head library '" + options.headLibraryPath
                    + "' did not attach the managed host table.";
                return false;
            }
        }
#else
        (void)options;
#endif
        if (!attached)
        {
            error = NotAttachedError;
            return false;
        }
        shimInitialized_ = true;
        return true;
    }
    catch (const std::exception& exception)
    {
        error = ea::string("Initialize failed: ") + exception.what();
        return false;
    }
    catch (...)
    {
        error = "Initialize failed with an unknown exception.";
        return false;
    }
}

void* DotNetHost::GetFunctionPointer(
    const ea::string& assemblyPath,
    ea::string_view typeName,
    ea::string_view methodName,
    ea::string_view delegateTypeName,
    ea::string& error)
{
    error.clear();
    try
    {
        if (!IsInitialized())
        {
            error = "The .NET host is not initialized.";
            return nullptr;
        }
        if (!delegateTypeName.empty())
        {
            error = "delegateTypeName is not supported by the DotNetHost shim.";
            return nullptr;
        }

        const SharpCppRbfxManagedHost table = CopyTable();
        if (!table.getFunctionPointer)
        {
            error = NotAttachedError;
            return nullptr;
        }

        const ea::string type(typeName.data(), typeName.size());
        const ea::string method(methodName.data(), methodName.size());
        ea::string managedError;
        void* result = table.getFunctionPointer(
            assemblyPath.c_str(), type.c_str(), method.c_str(), "", &AppendManagedError, &managedError);
        if (result == nullptr)
        {
            error = "Cannot get the function pointer of '" + type + "." + method + "' in '" + assemblyPath
                + "': " + (managedError.empty() ? ea::string("unknown error.") : managedError);
            return nullptr;
        }
        return result;
    }
    catch (const std::exception& exception)
    {
        error = ea::string("GetFunctionPointer failed: ") + exception.what();
        return nullptr;
    }
    catch (...)
    {
        error = "GetFunctionPointer failed with an unknown exception.";
        return nullptr;
    }
}

bool DotNetHost::LoadPlugin(PluginManager* pluginManager, const ea::string& assemblyPath, ea::string& error)
{
    error.clear();
    try
    {
        if (!IsInitialized())
        {
            error = "The .NET host is not initialized.";
            return false;
        }
        if (!pluginManager)
        {
            error = "The plugin manager is null. Call LoadPlugin after Engine::Initialize.";
            return false;
        }

        const SharpCppRbfxManagedHost table = CopyTable();
        if (!table.loadPlugin)
        {
            error = NotAttachedError;
            return false;
        }

        const ea::string utf8Path = MakeAbsoluteHostPath(assemblyPath);
        ea::string managedError;
        if (table.loadPlugin(pluginManager, utf8Path.c_str(), &AppendManagedError, &managedError) == 0)
        {
            error = "Cannot load the managed plugin '" + assemblyPath + "': "
                + (managedError.empty() ? ea::string("unknown error.") : managedError);
            return false;
        }
        return true;
    }
    catch (const std::exception& exception)
    {
        error = ea::string("LoadPlugin failed: ") + exception.what();
        return false;
    }
    catch (...)
    {
        error = "LoadPlugin failed with an unknown exception.";
        return false;
    }
}

bool DotNetHost::RegisterPlugin(PluginManager*, const ea::string&, ea::string& error)
{
    // The managed host table of the shim has no register entry. Only the desktop editor uses RegisterPlugin.
    error = "RegisterPlugin is not available in the DotNetHost shim.";
    return false;
}

bool DotNetHost::LoadPluginEntryPoint(ea::string& error)
{
    // The hostfxr class layout has this member. The shim never uses it.
    error = "LoadPluginEntryPoint is not available in the DotNetHost shim.";
    return false;
}

void DotNetHost::Close()
{
#if defined(SHARPCPP_RBFX_SHIM_LOAD_HEAD)
    if (hostfxrLibrary_)
    {
        try
        {
            DetachHead(hostfxrLibrary_);
        }
        catch (...)
        {
        }
        // A NativeAOT runtime cannot unload, so the library stays loaded. Only the handle is dropped.
        hostfxrLibrary_ = nullptr;
    }
#endif
    shimInitialized_ = false;
}

} // namespace Urho3D

extern "C"
{

int sharpcpp_rbfx_attach_managed_host(const SharpCppRbfxManagedHost* host)
{
    try
    {
        if (!host || host->version != SHARPCPP_RBFX_MANAGED_HOST_VERSION
            || host->size < static_cast<int32_t>(sizeof(SharpCppRbfxManagedHost))
            || !host->getFunctionPointer || !host->loadPlugin)
            return 0;
        std::lock_guard<std::mutex> lock(Urho3D::TableMutex());
        Urho3D::Table() = *host;
        return 1;
    }
    catch (...)
    {
        return 0;
    }
}

void sharpcpp_rbfx_detach_managed_host()
{
    try
    {
        std::lock_guard<std::mutex> lock(Urho3D::TableMutex());
        Urho3D::Table() = SharpCppRbfxManagedHost{};
    }
    catch (...)
    {
    }
}

}
