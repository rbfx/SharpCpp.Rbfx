// Shim implementation of DotNetHost for Android, iOS and Emscripten.
// It never starts a runtime. It calls the function pointers that the managed side attached.

#include "Urho3D/CSharp/DotNetHost.h"
#include "Urho3D/CSharp/DotNetHostShim.h"

#include <exception>
#include <mutex>

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
        static_cast<std::string*>(state)->append(text, static_cast<std::size_t>(length));
}

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

bool DotNetHost::Initialize(const DotNetHostOptions& options, std::string& error)
{
    (void)options;
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
        error = std::string("Initialize failed: ") + exception.what();
        return false;
    }
    catch (...)
    {
        error = "Initialize failed with an unknown exception.";
        return false;
    }
}

void* DotNetHost::GetFunctionPointer(
    const std::filesystem::path& assemblyPath,
    std::string_view typeName,
    std::string_view methodName,
    std::string_view delegateTypeName,
    std::string& error)
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

        const std::u8string path = assemblyPath.u8string();
        const std::string utf8Path(path.begin(), path.end());
        const std::string type(typeName);
        const std::string method(methodName);
        std::string managedError;
        void* result = table.getFunctionPointer(
            utf8Path.c_str(), type.c_str(), method.c_str(), "", &AppendManagedError, &managedError);
        if (result == nullptr)
        {
            error = "Cannot get the function pointer of '" + type + "." + method + "' in '" + assemblyPath.generic_string()
                + "': " + (managedError.empty() ? std::string("unknown error.") : managedError);
            return nullptr;
        }
        return result;
    }
    catch (const std::exception& exception)
    {
        error = std::string("GetFunctionPointer failed: ") + exception.what();
        return nullptr;
    }
    catch (...)
    {
        error = "GetFunctionPointer failed with an unknown exception.";
        return nullptr;
    }
}

bool DotNetHost::LoadPlugin(PluginManager* pluginManager, const std::filesystem::path& assemblyPath, std::string& error)
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

        const std::u8string path = std::filesystem::absolute(assemblyPath).u8string();
        const std::string utf8Path(path.begin(), path.end());
        std::string managedError;
        if (table.loadPlugin(pluginManager, utf8Path.c_str(), &AppendManagedError, &managedError) == 0)
        {
            error = "Cannot load the managed plugin '" + assemblyPath.generic_string() + "': "
                + (managedError.empty() ? std::string("unknown error.") : managedError);
            return false;
        }
        return true;
    }
    catch (const std::exception& exception)
    {
        error = std::string("LoadPlugin failed: ") + exception.what();
        return false;
    }
    catch (...)
    {
        error = "LoadPlugin failed with an unknown exception.";
        return false;
    }
}

bool DotNetHost::LoadPluginEntryPoint(std::string& error)
{
    // The hostfxr class layout has this member. The shim never uses it.
    error = "LoadPluginEntryPoint is not available in the DotNetHost shim.";
    return false;
}

void DotNetHost::Close()
{
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
