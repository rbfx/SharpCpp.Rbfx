// The default editor of rbfx_csharp_add_editor. It is the rbfx editor that hosts the .NET runtime.
// The compile definition SHARPCPP_RBFX_EDITOR_NAME is the name of the editor target.

#include <Urho3D/CSharp/DotNetPlugin.h>
#include <Urho3D/Core/CoreEvents.h>
#include <Urho3D/IO/FileSystem.h>
#include <Urho3D/IO/Log.h>
#include <Urho3D/Plugins/PluginManager.h>

#include <Tools/Editor/EditorApplication.h>

#include <filesystem>
#include <string>

using namespace Urho3D;

// Registers the managed plugins for each PluginManager of the editor.
// The editor makes a new PluginManager each time that a project opens, and sends no event. The registration of the
// managed plugins must run again for each new manager. RegisterPlugin does not change the loaded plugins.
// The helper is a separate object, because an object keeps one handler for each event, and the editor has its own
// E_ENDFRAME handler (UpdateProjectStatus). The editor creates the helper after EditorApplication::Start(), so the
// handler of the helper runs after UpdateProjectStatus in the same E_ENDFRAME event. A new project has its
// PluginManager then, and the manager restores its plugins on E_ENDFRAMEPRIVATE, which is later.
class ManagedPluginRegistrar : public Object
{
    URHO3D_OBJECT(ManagedPluginRegistrar, Object);

public:
    ManagedPluginRegistrar(Context* context, DotNetHost& host, std::filesystem::path assemblyPath)
        : Object(context)
        , host_(host)
        , assemblyPath_(std::move(assemblyPath))
    {
        SubscribeToEvent(E_ENDFRAME, [this](StringHash, VariantMap&) { Poll(); });
    }

    // A weak pointer is null when the old manager is gone, so a new manager at the same address is also found.
    // The project sets the loaded plugins in its constructor, and the manager skips the names that are not registered
    // when it restores them. When no reload is pending (the manager has already restored), the reload loads the plugins.
    void Poll()
    {
        PluginManager* current = GetSubsystem<PluginManager>();
        if (!current || current == pluginManager_.Get())
            return;
        pluginManager_ = current;
        std::string error;
        if (!host_.RegisterPlugin(current, assemblyPath_, error))
            URHO3D_LOGERROR("{}", ea::string(error.c_str()));
        else if (!current->IsReloadPending())
            current->Reload();
    }

private:
    DotNetHost& host_;
    std::filesystem::path assemblyPath_;
    WeakPtr<PluginManager> pluginManager_;
};

class RbfxCSharpEditor : public EditorApplication
{
public:
    using EditorApplication::EditorApplication;

    void Setup() override
    {
        EditorApplication::Setup();
        // The static plugins register here. The macro below names this class, so this function runs.
        LinkedPlugins::RegisterStaticPlugins();
    }

    void Start() override
    {
        // The engine creates the first plugin manager before Start(). The bootstrap plugin SharpCpp.Rbfx.EditorHost
        // registers the managed plugins of the folder ManagedPlugins. The runtime config is
        // SharpCpp.Rbfx.EditorHost.runtimeconfig.json in the folder of the executable.
        directory_ = std::filesystem::path(GetSubsystem<FileSystem>()->GetProgramDir().c_str());
        // StartDotNetPlugin would add the bootstrap plugin to the loaded plugins of the manager, and the project saves
        // that list. So the editor starts the runtime and only registers the plugin.
        DotNetHostOptions options;
        options.runtimeConfigPath = directory_ / (std::string(SHARPCPP_RBFX_EDITOR_HOST_NAME) + ".runtimeconfig.json");
        std::string error;
        if (!host_.Initialize(options, error))
        {
            const ea::string message(("Cannot start the .NET runtime: " + error).c_str());
            URHO3D_LOGERROR("{}", message);
            ErrorExit(message);
            return;
        }
        EditorApplication::Start();
        // The helper subscribes to E_ENDFRAME after the editor did, so its handler runs after UpdateProjectStatus.
        registrar_ = MakeShared<ManagedPluginRegistrar>(
            context_, host_, directory_ / (std::string(SHARPCPP_RBFX_EDITOR_HOST_NAME) + ".dll"));
        registrar_->Poll();
    }

    void Stop() override
    {
        EditorApplication::Stop();
        registrar_.Reset();
        host_.Close();
    }

private:
    DotNetHost host_;
    std::filesystem::path directory_;
    SharedPtr<ManagedPluginRegistrar> registrar_;
};

URHO3D_DEFINE_APPLICATION_MAIN(RbfxCSharpEditor);
