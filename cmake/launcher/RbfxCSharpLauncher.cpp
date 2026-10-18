// The default launcher of rbfx_csharp_package_application. It starts the .NET runtime and loads the C# plugin.
// The compile definition SHARPCPP_RBFX_PLUGIN_NAME is the assembly name of the plugin.

#include <Urho3D/CSharp/DotNetPlugin.h>
#include <Urho3D/Engine/EngineDefs.h>
#include <Urho3D/IO/FileSystem.h>
#include <Urho3D/IO/Log.h>
#include <Urho3D/Plugins/PluginManager.h>

#include <Player/PlayerApplication.h>

using namespace Urho3D;

class RbfxCSharpLauncher : public PlayerApplication
{
public:
    using PlayerApplication::PlayerApplication;

    void Setup() override
    {
        PlayerApplication::Setup();

#if MOBILE
        engineParameters_[EP_ORIENTATIONS] = "Portrait";
        engineParameters_[EP_RESOURCE_PREFIX_PATHS] = ";..;../..";
#else
        auto fs = context_->GetSubsystem<FileSystem>();
        engineParameters_[EP_RESOURCE_PREFIX_PATHS] = fs->GetProgramDir() + ";" + fs->GetCurrentDir() + "../";
#endif

        // PlayerApplication::Start keeps only the plugins from EP_PLUGINS, so the managed plugin must be in the list.
        auto plugins = LinkedPlugins::GetLinkedPlugins();
        plugins.push_back(SHARPCPP_RBFX_PLUGIN_NAME);
        engineParameters_[EP_PLUGINS] = ea::string::joined(plugins, ";");
        LinkedPlugins::RegisterStaticPlugins();
    }

    void Start() override
    {
        // The engine creates the plugin manager before Start().
        const ea::string directory = GetSubsystem<FileSystem>()->GetProgramDir();
        ea::string error;
        if (!StartDotNetPlugin(host_, GetSubsystem<PluginManager>(), directory, SHARPCPP_RBFX_PLUGIN_NAME, error))
        {
            URHO3D_LOGERROR("{}", error);
            ErrorExit(error);
            return;
        }
        PlayerApplication::Start();
    }

    void Stop() override
    {
        PlayerApplication::Stop();
        host_.Close();
    }

private:
    DotNetHost host_;
};

URHO3D_DEFINE_APPLICATION_MAIN(RbfxCSharpLauncher);
