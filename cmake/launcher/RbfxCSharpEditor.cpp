// The default editor of rbfx_csharp_add_editor. It is the rbfx editor that hosts the .NET runtime.
// The compile definition SHARPCPP_RBFX_EDITOR_NAME is the name of the editor target. The hosting code is in
// RbfxCSharpEditorHost.h, which rbfx_csharp_host_editor also gives to an editor of a project.

#include "RbfxCSharpEditorHost.h"

#include <Urho3D/Engine/EngineDefs.h>

// The installed SDK has the editor headers in Tools/Editor. An engine in source mode has them in Source/Editor.
#if __has_include(<Tools/Editor/EditorApplication.h>)
#include <Tools/Editor/EditorApplication.h>
#else
#include <Editor/EditorApplication.h>
#endif

using namespace Urho3D;

class RbfxCSharpEditor : public EditorApplication
{
public:
    using EditorApplication::EditorApplication;

    void Setup() override
    {
        EditorApplication::Setup();
        // The static plugins register here. The macro below names this class, so this function runs.
        LinkedPlugins::RegisterStaticPlugins();
        // The editor turns the lookup of ResourceRoot.ini off. A build tree turns it on again. The user puts
        // ResourceRoot.ini in the project or build folder. Without the file the engine uses its normal lookup.
        engineParameters_[EP_RESOURCE_ROOT_FILE] = "ResourceRoot.ini";
    }

    void Start() override
    {
        // The engine creates the first plugin manager before Start(). The bootstrap plugin SharpCpp.Rbfx.EditorHost
        // registers the managed plugins of the folder ManagedPlugins.
        ea::string error;
        if (!dotNet_.Start(context_, SHARPCPP_RBFX_EDITOR_HOST_NAME, error))
        {
            ErrorExit(error);
            return;
        }
        EditorApplication::Start();
        dotNet_.OnEditorStarted();
    }

    void Stop() override
    {
        EditorApplication::Stop();
        dotNet_.Stop();
    }

private:
    RbfxEditorDotNetHost dotNet_;
};

URHO3D_DEFINE_APPLICATION_MAIN(RbfxCSharpEditor);
