#include <Urho3D/CSharp/DotNetHost.h>

#include <CLI11/CLI11.hpp>
#include <Urho3D/Core/CoreEvents.h>
#include <Urho3D/Engine/Application.h>
#include <Urho3D/Engine/Engine.h>
#include <Urho3D/Engine/EngineDefs.h>
#include <Urho3D/Graphics/Camera.h>
#include <Urho3D/Graphics/Light.h>
#include <Urho3D/Graphics/Material.h>
#include <Urho3D/Graphics/Model.h>
#include <Urho3D/Graphics/Octree.h>
#include <Urho3D/Graphics/Renderer.h>
#include <Urho3D/Graphics/Viewport.h>
#include <Urho3D/Graphics/Zone.h>
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
        GetCommandLineParser().add_option("--frames", frames_, "Number of frames to run before exit (0 runs until the window closes)");
    }

    void Start() override
    {
        const std::filesystem::path directory = std::filesystem::path(GetSubsystem<Urho3D::FileSystem>()->GetProgramDir().c_str());

        Urho3D::DotNetHostOptions options;
        options.runtimeConfigPath = directory / "Rbfx.NativeHost.Managed.runtimeconfig.json";
        std::string error;
        if (!host_.Initialize(options, error))
        {
            Fail(("Cannot start the .NET runtime: " + error).c_str());
            return;
        }

        // The engine creates the plugin manager in Engine::Initialize, before Start().
        // The plugin registers the C# Spinner component, so the code below can create it by name.
        if (!host_.LoadPlugin(GetSubsystem<Urho3D::PluginManager>(), directory / "Rbfx.NativeHost.Managed.dll", error))
        {
            Fail(("Cannot load the C# plugin: " + error).c_str());
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
            Fail("Cannot create the C# Spinner component.");
            return;
        }
        spinner->SetAttribute("Speed", 90.0f);

        auto* zone = scene_->CreateComponent<Urho3D::Zone>();
        zone->SetBoundingBox(Urho3D::BoundingBox(-1000.0f, 1000.0f));
        zone->SetAmbientColor(Urho3D::Color(0.3f, 0.3f, 0.3f));

        Urho3D::Node* lightNode = scene_->CreateChild("Light");
        lightNode->SetDirection(Urho3D::Vector3(0.6f, -1.0f, 0.8f));
        lightNode->CreateComponent<Urho3D::Light>()->SetLightType(Urho3D::LIGHT_DIRECTIONAL);

        Urho3D::Node* cameraNode = scene_->CreateChild("Camera");
        cameraNode->SetPosition(Urho3D::Vector3(0.0f, 1.0f, -4.0f));
        cameraNode->LookAt(Urho3D::Vector3::ZERO);
        auto* camera = cameraNode->CreateComponent<Urho3D::Camera>();
        auto* renderer = GetSubsystem<Urho3D::Renderer>();
        // A headless run has no Renderer subsystem.
        if (renderer)
        {
            renderer->SetNumViewports(1);
            renderer->SetViewport(0, Urho3D::MakeShared<Urho3D::Viewport>(context_, scene_, camera));
        }

        SubscribeToEvent(Urho3D::E_UPDATE, URHO3D_HANDLER(NativeHostApp, HandleUpdate));
    }

    void Stop() override
    {
        box_ = nullptr;
        scene_ = nullptr;
        host_.Close();
    }

private:
    void Fail(const ea::string& message)
    {
        URHO3D_LOGERROR("{}", message);
        ErrorExit(message);
    }

    void HandleUpdate(Urho3D::StringHash eventType, Urho3D::VariantMap& eventData)
    {
        if (frames_ <= 0 || ++frameCount_ < frames_)
            return;

        const float yaw = box_->GetRotation().YawAngle();
        URHO3D_LOGINFO("Finished after {} frames, box yaw: {}", frameCount_, yaw);
        if (yaw == 0.0f)
            Fail("The C# spinner did not rotate the box.");
        else
            engine_->Exit();
    }

    Urho3D::DotNetHost host_;
    Urho3D::SharedPtr<Urho3D::Scene> scene_;
    Urho3D::SharedPtr<Urho3D::Node> box_;
    int frames_{0};
    int frameCount_{};
};

URHO3D_DEFINE_APPLICATION_MAIN(NativeHostApp)
