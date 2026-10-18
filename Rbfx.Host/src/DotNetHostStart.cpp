// The high-level API of DotNetHost: Start and the properties. It is the same in the hostfxr mode and the shim mode.

#include "Urho3D/CSharp/DotNetHost.h"

#include <Urho3D/IO/FileSystem.h>
#include <Urho3D/IO/Log.h>
#include <Urho3D/Plugins/PluginManager.h>

#if defined(__APPLE__)
#include <TargetConditionals.h>
#endif

namespace Urho3D
{

DotNetHost::DotNetHost(Context* context)
    : Object(context)
{
    if (auto* fileSystem = GetSubsystem<FileSystem>())
        directory_ = fileSystem->GetProgramDir();
}

void DotNetHost::SetDirectory(const ea::string& directory)
{
    directory_ = directory;
}

bool DotNetHost::Start(const ea::string& pluginName)
{
    lastError_.clear();

    const auto fail = [this](const ea::string& message)
    {
        lastError_ = message;
        URHO3D_LOGERROR("{}", lastError_);
        return false;
    };

    if (pluginName.empty())
        return fail("Cannot start the .NET runtime: the plugin name is empty.");
    if (IsInitialized())
        return fail("Cannot start the .NET runtime: the host is already started.");

    auto* pluginManager = GetSubsystem<PluginManager>();
    if (!pluginManager)
        return fail("Cannot start the .NET runtime: there is no PluginManager. Call Start after Engine::Initialize.");

    const ea::string prefix = AddTrailingSlash(directory_);

    DotNetHostOptions options;
    options.runtimeConfigPath = prefix + (pluginName + ".runtimeconfig.json");
#if defined(URHO3D_DOTNETHOST_SHIM) && !defined(__ANDROID__) && !defined(__EMSCRIPTEN__) \
    && !(defined(__APPLE__) && TARGET_OS_IOS)
    // Desktop shim: the NativeAOT head is <name>.Head with the shared library suffix of the platform.
#if defined(_WIN32)
    options.headLibraryPath = prefix + (pluginName + ".Head.dll");
#elif defined(__APPLE__)
    options.headLibraryPath = prefix + (pluginName + ".Head.dylib");
#else
    options.headLibraryPath = prefix + (pluginName + ".Head.so");
#endif
#endif
    ea::string detail;
    if (!Initialize(options, detail))
        return fail("Cannot start the .NET runtime: " + detail);

    if (!LoadPlugin(pluginManager, prefix + (pluginName + ".dll"), detail))
    {
        Stop();
        return fail("Cannot load the C# plugin: " + detail);
    }

    return true;
}

} // namespace Urho3D
