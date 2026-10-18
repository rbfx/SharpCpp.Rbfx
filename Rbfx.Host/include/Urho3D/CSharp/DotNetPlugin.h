#pragma once

#include "Urho3D/CSharp/DotNetHost.h"

#include <Urho3D/IO/FileSystem.h>

#if defined(__APPLE__)
#include <TargetConditionals.h>
#endif

namespace Urho3D
{

/// Starts the .NET runtime and loads a C# plugin. Header-only helper for DotNetHost.
/// The runtime config is <directory>/<pluginName>.runtimeconfig.json.
/// The plugin assembly is <directory>/<pluginName>.dll.
/// Call it from Application::Start(), after Engine::Initialize() created the PluginManager.
/// Returns false and fills error on failure.
inline bool StartDotNetPlugin(DotNetHost& host, PluginManager* pluginManager, const ea::string& directory,
    ea::string_view pluginName, ea::string& error)
{
    const ea::string name(pluginName.data(), pluginName.size());
    const ea::string prefix = AddTrailingSlash(directory);

    DotNetHostOptions options;
    options.runtimeConfigPath = prefix + (name + ".runtimeconfig.json");
#if defined(URHO3D_DOTNETHOST_SHIM) && !defined(__ANDROID__) && !defined(__EMSCRIPTEN__) \
    && !(defined(__APPLE__) && TARGET_OS_IOS)
    // Desktop shim: the NativeAOT head is <name>.Head with the shared library suffix of the platform.
#if defined(_WIN32)
    options.headLibraryPath = prefix + (name + ".Head.dll");
#elif defined(__APPLE__)
    options.headLibraryPath = prefix + (name + ".Head.dylib");
#else
    options.headLibraryPath = prefix + (name + ".Head.so");
#endif
#endif
    ea::string detail;
    if (!host.Initialize(options, detail))
    {
        error = "Cannot start the .NET runtime: " + detail;
        return false;
    }

    if (!host.LoadPlugin(pluginManager, prefix + (name + ".dll"), detail))
    {
        error = "Cannot load the C# plugin: " + detail;
        return false;
    }

    return true;
}

} // namespace Urho3D
