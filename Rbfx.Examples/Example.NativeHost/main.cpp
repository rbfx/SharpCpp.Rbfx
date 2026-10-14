#include <Urho3D/CSharp/DotNetHost.h>

#include <CLI11/CLI11.hpp>
#include <Urho3D/Core/CoreEvents.h>
#include <Urho3D/Engine/Application.h>
#include <Urho3D/Engine/Engine.h>
#include <Urho3D/Engine/EngineDefs.h>
#include <Urho3D/Graphics/Material.h>
#include <Urho3D/Graphics/Model.h>
#include <Urho3D/Graphics/Octree.h>
#include <Urho3D/Graphics/StaticModel.h>
#include <Urho3D/IO/FileSystem.h>
#include <Urho3D/IO/Log.h>
#include <Urho3D/Plugins/PluginManager.h>
#include <Urho3D/Resource/ResourceCache.h>
#include <Urho3D/Scene/Node.h>
#include <Urho3D/Scene/Scene.h>

#include <filesystem>
#include <string>

/// Plain rbfx application. It starts the .NET runtime and uses a C# component as a script.
class NativeHostApp : public Urho3D::Application
{
    URHO3D_OBJECT(NativeHostApp, Application);

public:
    explicit NativeHostApp(Urho3D::Context* context)
        : Application(context)
    {
    }

    void Setup() override
    {
        engineParameters_[Urho3D::EP_WINDOW_TITLE] = "Rbfx.NativeHost";
        engineParameters_[Urho3D::EP_LOG_NAME] = "Rbfx.NativeHost.log";
        GetCommandLineParser().add_option("--frames", frames_, "Number of frames to run before exit");
    }

    void Start() override
    {
        const std::filesystem::path directory = std::filesystem::path(GetSubsystem<Urho3D::FileSystem>()->GetProgramDir().c_str());

        Urho3D::DotNetHostOptions options;
        options.runtimeConfigPath = directory / "Rbfx.NativeHost.Managed.runtimeconfig.json";
        std::string error;
        if (!host_.Initialize(options, error))
        {
            ErrorExit(("Cannot start the .NET runtime: " + error).c_str());
            return;
        }

        // The engine creates the plugin manager in Engine::Initialize, before Start().
        // The plugin registers the C# Spinner component, so the code below can create it by name.
        if (!host_.LoadPlugin(GetSubsystem<Urho3D::PluginManager>(), directory / "Rbfx.NativeHost.Managed.dll", error))
        {
            ErrorExit(("Cannot load the C# plugin: " + error).c_str());
            return;
        }

        scene_ = Urho3D::MakeShared<Urho3D::Scene>(context_);
        scene_->CreateComponent<Urho3D::Octree>();
        box_ = scene_->CreateChild("Box");
        auto* cache = GetSubsystem<Urho3D::ResourceCache>();
        auto* model = box_->CreateComponent<Urho3D::StaticModel>();
        model->SetModel(cache->GetResource<Urho3D::Model>("Models/Box.mdl"));
        model->SetMaterial(cache->GetResource<Urho3D::Material>("Materials/DefaultGrey.xml"));

        Urho3D::Component* spinner = box_->CreateComponent("Spinner");
        if (!spinner)
        {
            ErrorExit("Cannot create the C# Spinner component.");
            return;
        }
        spinner->SetAttribute("Speed", 90.0f);

        SubscribeToEvent(Urho3D::E_UPDATE, URHO3D_HANDLER(NativeHostApp, HandleUpdate));
    }

    void Stop() override
    {
        box_ = nullptr;
        scene_ = nullptr;
        host_.Close();
    }

private:
    void HandleUpdate(Urho3D::StringHash eventType, Urho3D::VariantMap& eventData)
    {
        if (++frameCount_ < frames_)
            return;

        const float yaw = box_->GetRotation().YawAngle();
        URHO3D_LOGINFO("Finished after {} frames, box yaw: {}", frameCount_, yaw);
        if (yaw == 0.0f)
            ErrorExit("The C# spinner did not rotate the box.");
        else
            engine_->Exit();
    }

    Urho3D::DotNetHost host_;
    Urho3D::SharedPtr<Urho3D::Scene> scene_;
    Urho3D::SharedPtr<Urho3D::Node> box_;
    int frames_{60};
    int frameCount_{};
};

URHO3D_DEFINE_APPLICATION_MAIN(NativeHostApp)
