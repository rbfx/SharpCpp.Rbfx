#pragma once

#include "Urho3D/CSharp/DotNetHost.h"

#include <filesystem>
#include <string>
#include <string_view>

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
inline bool StartDotNetPlugin(DotNetHost& host, PluginManager* pluginManager, const std::filesystem::path& directory,
    std::string_view pluginName, std::string& error)
{
    const std::string name(pluginName);

    DotNetHostOptions options;
    options.runtimeConfigPath = directory / (name + ".runtimeconfig.json");
#if defined(URHO3D_DOTNETHOST_SHIM) && !defined(__ANDROID__) && !defined(__EMSCRIPTEN__) \
    && !(defined(__APPLE__) && TARGET_OS_IOS)
    // Desktop shim: the NativeAOT head is <name>.Head with the shared library suffix of the platform.
#if defined(_WIN32)
    options.headLibraryPath = directory / (name + ".Head.dll");
#elif defined(__APPLE__)
    options.headLibraryPath = directory / (name + ".Head.dylib");
#else
    options.headLibraryPath = directory / (name + ".Head.so");
#endif
#endif
    std::string detail;
    if (!host.Initialize(options, detail))
    {
        error = "Cannot start the .NET runtime: " + detail;
        return false;
    }

    if (!host.LoadPlugin(pluginManager, directory / (name + ".dll"), detail))
    {
        error = "Cannot load the C# plugin: " + detail;
        return false;
    }

    return true;
}

} // namespace Urho3D
