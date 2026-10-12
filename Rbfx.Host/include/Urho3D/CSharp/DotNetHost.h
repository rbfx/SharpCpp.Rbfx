#pragma once

#include "Urho3D/CSharp/DotNetHostExport.h"

#include <filesystem>
#include <string>
#include <string_view>

namespace Urho3D
{

/// Options of DotNetHost::Initialize.
struct DotNetHostOptions
{
    /// Path of the <Assembly>.runtimeconfig.json file. Required.
    std::filesystem::path runtimeConfigPath;
    /// Root directory of the .NET installation. Optional. Empty means automatic lookup.
    std::filesystem::path dotnetRoot;
};

/// Hosts the .NET runtime in the current process through hostfxr.
/// No function throws across the library boundary. Errors are returned as text.
/// The runtime stays loaded in the process after Close(). .NET cannot unload it.
class URHO3D_DOTNETHOST_API DotNetHost
{
public:
    DotNetHost();
    ~DotNetHost();
    DotNetHost(const DotNetHost&) = delete;
    DotNetHost& operator=(const DotNetHost&) = delete;

    /// Loads hostfxr and starts the runtime that the runtime config describes.
    /// Returns false and fills error on failure.
    bool Initialize(const DotNetHostOptions& options, std::string& error);

    /// Returns a native function pointer to a managed static method. Returns nullptr on failure.
    /// typeName is assembly qualified: "Ns.Type, AssemblyName".
    /// The method must have [UnmanagedCallersOnly] when delegateTypeName is empty.
    void* GetFunctionPointer(
        const std::filesystem::path& assemblyPath,
        std::string_view typeName,
        std::string_view methodName,
        std::string_view delegateTypeName,
        std::string& error);

    /// Typed variant of GetFunctionPointer.
    template <typename Fn>
    Fn GetFunction(
        const std::filesystem::path& assemblyPath,
        std::string_view typeName,
        std::string_view methodName,
        std::string_view delegateTypeName,
        std::string& error)
    {
        return reinterpret_cast<Fn>(GetFunctionPointer(assemblyPath, typeName, methodName, delegateTypeName, error));
    }

    bool IsInitialized() const;

    /// Closes the host context. The runtime itself stays loaded.
    void Close();

private:
    void* hostfxrLibrary_{};
    void* hostContext_{};
    void* closeFunction_{};
    void* loadAssemblyFunction_{};
};

} // namespace Urho3D
