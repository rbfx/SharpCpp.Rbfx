// The .NET hosting of an rbfx editor. The header has no .cpp file. An editor that rbfx_csharp_add_editor or
// rbfx_csharp_host_editor prepared can include it. Both functions define SHARPCPP_RBFX_EDITOR_HOST_NAME, which is the
// name of the bootstrap plugin SharpCpp.Rbfx.EditorHost, and add this folder to the include folders of the editor.
//
// Use in an EditorApplication:
//   Start(): ea::string error;
//            if (!dotNet_.Start(context_, SHARPCPP_RBFX_EDITOR_HOST_NAME, error)) { ErrorExit(error); return; }
//            EditorApplication::Start();
//            dotNet_.OnEditorStarted();
//   Stop():  EditorApplication::Stop();
//            dotNet_.Stop();

#pragma once

#include <Urho3D/CSharp/DotNetHost.h>
#include <Urho3D/Core/CoreEvents.h>
#include <Urho3D/Core/Object.h>
#include <Urho3D/IO/FileSystem.h>
#include <Urho3D/IO/Log.h>
#include <Urho3D/Plugins/PluginManager.h>

#include <EASTL/string.h>
#include <EASTL/string_view.h>

namespace Urho3D
{

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
    ManagedPluginRegistrar(Context* context, DotNetHost* host, ea::string assemblyPath)
        : Object(context)
        , host_(host)
        , assemblyPath_(ea::move(assemblyPath))
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
        ea::string error;
        if (!host_->RegisterPlugin(current, assemblyPath_, error))
            URHO3D_LOGERROR("{}", error);
        else if (!current->IsReloadPending())
            current->Reload();
    }

private:
    SharedPtr<DotNetHost> host_;
    ea::string assemblyPath_;
    WeakPtr<PluginManager> pluginManager_;
};

// Starts the .NET runtime of an editor and registers the bootstrap plugin SharpCpp.Rbfx.EditorHost.
// The bootstrap plugin registers the managed plugins of the folder ManagedPlugins next to the executable.
class RbfxEditorDotNetHost
{
public:
    // Starts the runtime. Call it before EditorApplication::Start(), because the engine creates the first plugin manager
    // before that call. hostName is the name of the bootstrap plugin (SHARPCPP_RBFX_EDITOR_HOST_NAME). The runtime
    // config is <hostName>.runtimeconfig.json in the folder of the executable.
    // DotNetHost::Start would add the bootstrap plugin to the loaded plugins of the manager, and the project saves
    // that list. So this function starts the runtime and only registers the plugin later.
    bool Start(Context* context, ea::string_view hostName, ea::string& error)
    {
        directory_ = AddTrailingSlash(context->GetSubsystem<FileSystem>()->GetProgramDir());
        const ea::string name{hostName.data(), static_cast<ea::string::size_type>(hostName.size())};
        assemblyPath_ = directory_ + name + ".dll";
        DotNetHostOptions options;
        options.runtimeConfigPath = directory_ + name + ".runtimeconfig.json";
        context_ = context;
        host_ = MakeShared<DotNetHost>(context);
        if (!host_->Initialize(options, error))
        {
            error = "Cannot start the .NET runtime: " + error;
            URHO3D_LOGERROR("{}", error);
            return false;
        }
        return true;
    }

    // Call it after EditorApplication::Start(). The registrar then subscribes to E_ENDFRAME after the editor did,
    // so its handler runs after UpdateProjectStatus.
    void OnEditorStarted()
    {
        if (!host_)
            return;
        registrar_ = MakeShared<ManagedPluginRegistrar>(context_, host_.Get(), assemblyPath_);
        registrar_->Poll();
    }

    // Call it after EditorApplication::Stop().
    void Stop()
    {
        registrar_.Reset();
        if (host_)
            host_->Stop();
    }

private:
    Context* context_{};
    SharedPtr<DotNetHost> host_;
    ea::string directory_;
    ea::string assemblyPath_;
    SharedPtr<ManagedPluginRegistrar> registrar_;
};

} // namespace Urho3D
