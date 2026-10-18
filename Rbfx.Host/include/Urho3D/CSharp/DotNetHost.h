#pragma once

#include "Urho3D/CSharp/DotNetHostExport.h"

#include <Urho3D/Container/Str.h>
#include <Urho3D/Core/Object.h>

namespace Urho3D
{

class PluginManager;

/// Options of DotNetHost::Initialize. All paths are UTF-8 strings, like the paths of the FileSystem subsystem.
struct DotNetHostOptions
{
    /// Path of the <Assembly>.runtimeconfig.json file in UTF-8. Required.
    ea::string runtimeConfigPath;
    /// Root directory of the .NET installation. Optional. Empty means automatic lookup.
    ea::string dotnetRoot;
    /// Path of SharpCpp.Rbfx.Bindings.dll. Optional. Empty means the file next to the runtime config.
    /// LoadPlugin loads this assembly once, into the default load context of the runtime.
    ea::string bindingsAssemblyPath;
    /// Shim mode on desktop: native library of the NativeAOT head. Empty on other platforms.
    ea::string headLibraryPath;
};

/// Hosts the .NET runtime in the current process through hostfxr.
/// On Android, iOS and Emscripten (RBFX_HOST_MODE=shim) the class is a shim. It starts no runtime.
/// It reads function pointers that the managed side attached (see DotNetHostShim.h).
/// The public API and the class layout are the same in both modes.
/// No function throws across the library boundary. Errors are returned as text.
/// The runtime stays loaded in the process after Stop(). .NET cannot unload it.
///
/// The class is an Object. Hold it as SharedPtr<DotNetHost> and create it with MakeShared<DotNetHost>(context).
/// The host finds the FileSystem and the PluginManager through its Context.
///
/// Simple use, in Application::Start() after Engine::Initialize() created the PluginManager:
///     host_ = MakeShared<DotNetHost>(context_);
///     if (!host_->Start("MyPlugin")) { ErrorExit(host_->GetLastError()); return; }
/// and host_->Stop() in Application::Stop().
class URHO3D_DOTNETHOST_API DotNetHost : public Object
{
    URHO3D_OBJECT(DotNetHost, Object);

public:
    explicit DotNetHost(Context* context);
    ~DotNetHost() override;

    /// Sets the directory of the runtime config, the plugin assembly and the head library for Start().
    /// The default is FileSystem::GetProgramDir().
    void SetDirectory(const ea::string& directory);
    const ea::string& GetDirectory() const { return directory_; }

    /// Starts the .NET runtime and loads a C# plugin. Call it after Engine::Initialize() created the PluginManager.
    /// pluginName is the assembly name of the plugin. It must not be empty.
    /// The runtime config is <directory>/<pluginName>.runtimeconfig.json.
    /// The plugin assembly is <directory>/<pluginName>.dll.
    /// In the desktop shim mode the head library is <directory>/<pluginName>.Head with the shared library suffix.
    /// Runs Initialize() and then LoadPlugin(). Returns false on failure. The error is logged and kept in GetLastError().
    /// A failure leaves the host stopped. Start() returns false when the host is already started.
    bool Start(const ea::string& pluginName);

    /// Returns the text of the last error of Start(). Empty after a successful Start().
    const ea::string& GetLastError() const { return lastError_; }

    /// Closes the host context. The runtime itself stays loaded. Use it after Start() or Initialize().
    void Stop();

    /// Loads hostfxr and starts the runtime that the runtime config describes.
    /// Returns false and fills error on failure.
    bool Initialize(const DotNetHostOptions& options, ea::string& error);

    /// Returns a native function pointer to a managed static method. Returns nullptr on failure.
    /// typeName is assembly qualified: "Ns.Type, AssemblyName".
    /// The method must have [UnmanagedCallersOnly] when delegateTypeName is empty.
    void* GetFunctionPointer(
        const ea::string& assemblyPath,
        ea::string_view typeName,
        ea::string_view methodName,
        ea::string_view delegateTypeName,
        ea::string& error);

    /// Typed variant of GetFunctionPointer.
    template <typename Fn>
    Fn GetFunction(
        const ea::string& assemblyPath,
        ea::string_view typeName,
        ea::string_view methodName,
        ea::string_view delegateTypeName,
        ea::string& error)
    {
        return reinterpret_cast<Fn>(GetFunctionPointer(assemblyPath, typeName, methodName, delegateTypeName, error));
    }

    /// Loads a managed rbfx plugin assembly into the plugin manager of the engine.
    /// The assembly references SharpCpp.Rbfx.Bindings and has a Plugin subclass, usually a ManagedPlugin.
    /// The plugin registers its types, so C++ can create them by name, for example Node::CreateComponent("Spinner").
    ///
    /// Call order:
    /// 1. Initialize().
    /// 2. Engine::Initialize(). It creates the PluginManager. Application::Start() runs after it.
    /// 3. LoadPlugin(). It registers the plugin, adds its name to PluginManager::SetPluginsLoaded and calls
    ///    PluginManager::Commit(). The plugin is loaded when LoadPlugin returns.
    /// 4. Create the managed types by name.
    /// A later call to PluginManager::SetPluginsLoaded must keep the plugin name, else the plugin unloads.
    /// A plugin name can be loaded only once. Returns false and fills error on failure.
    bool LoadPlugin(PluginManager* pluginManager, const ea::string& assemblyPath, ea::string& error);

    /// Registers a managed rbfx plugin assembly in the plugin manager without loading it. It does the first part of
    /// LoadPlugin only: the plugin manager adds the plugin as a static plugin, and PluginManager::SetPluginsLoaded and
    /// Commit are not called, so the list of the loaded plugins does not change. A plugin name can be registered only
    /// once for a plugin manager. Not available in the shim mode (returns false). Returns false and fills error on failure.
    bool RegisterPlugin(PluginManager* pluginManager, const ea::string& assemblyPath, ea::string& error);

    bool IsInitialized() const;

private:
    bool LoadPluginEntryPoint(ea::string& error);

    void* hostfxrLibrary_{};
    void* hostContext_{};
    void* closeFunction_{};
    void* loadAssemblyFunction_{};
    void* loadAssemblyIntoDefaultFunction_{};
    void* getFunctionPointerFunction_{};
    void* loadPluginFunction_{};
    void* registerPluginFunction_{};
    ea::string bindingsAssemblyPath_;
    ea::string directory_;
    ea::string lastError_;
    /// Used only in shim mode. The layout is the same in both modes.
    bool shimInitialized_{};
};

} // namespace Urho3D
