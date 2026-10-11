using System.Globalization;
using Urho3D.Core;
using Urho3D.Engine;
using Urho3D.Graphics;
using Urho3D.Input;
using Urho3D.Math;
using Urho3D.Resource;
using Urho3D.Scene;

namespace Rbfx.Examples.RotatingCube;

internal static class Program
{
    private static int Main()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        Environment.CurrentDirectory = AppContext.BaseDirectory;

        using var context = new Context();
        using var app = new CubeApplication(context);
        return app.Run();
    }
}

internal partial class CubeApplication : Application
{
    private Scene? _scene;
    private Node? _cube;
    private Viewport? _viewport;
    private float _angle;

    public CubeApplication(Context context) : base(context)
    {
    }

    public override void Setup()
    {
        EngineParameters[EngineDefs.WindowTitle] = "rbfx NuGet sample — Rotating Cube";
        EngineParameters[EngineDefs.ApplicationName] = "Rbfx.Examples.RotatingCube";
        EngineParameters[EngineDefs.WindowWidth] = 960;
        EngineParameters[EngineDefs.WindowHeight] = 640;
        EngineParameters[EngineDefs.FullScreen] = false;
        EngineParameters[EngineDefs.Borderless] = false;
        EngineParameters[EngineDefs.EngineAutoLoadScripts] = false;
        EngineParameters[EngineDefs.ResourcePrefixPaths] = AppContext.BaseDirectory;
        EngineParameters[EngineDefs.ResourcePaths] = "CoreData;Data";
        EngineParameters[EngineDefs.LogName] = System.IO.Path.Combine(AppContext.BaseDirectory, "RotatingCube.log");
    }

    public override void Start()
    {
        _scene = new Scene(GetContext());
        _cube = Require(_scene.CreateChild("Cube"), "cube node");

        _scene.CreateComponent<Octree>();
        var cache = Require(GetContext().GetSubsystem<ResourceCache>(), "resource cache");
        var model = Require(_cube.CreateComponent<StaticModel>(), "cube model component");
        model.SetModel(Require(cache.GetResource<Model>("Models/Box.mdl"), "box model"));
        model.SetMaterial(0, Require(cache.GetResource<Material>("Materials/StoneTiled.xml"), "stone material"));

        var camera = Require(_scene.CreateChild("Camera"), "camera node");
        camera.Position = new Vector3(0, 1.5f, -4);
        camera.LookAt(Vector3.Zero);
        _viewport = new Viewport(GetContext()) { Scene = _scene, Camera = Require(camera.CreateComponent<Camera>(), "camera component") };
        var renderer = Require(GetContext().GetSubsystem<Renderer>(), "renderer");
        renderer.SetViewport(0, _viewport);
        renderer.DefaultZone.FogColor = new Color(0.15f, 0.2f, 0.3f);

        var light = Require(_scene.CreateChild("Light"), "light node");
        light.Position = new Vector3(0, 3, -3);
        light.LookAt(Vector3.Zero);
        light.CreateComponent<Light>();
        Require(GetContext().GetSubsystem<Input>(), "input").SetMouseVisible(true);

        SubscribeToEvent(CoreEvents.Update, OnUpdate);
    }

    private void OnUpdate(Urho3D.Core.Object sender, StringHash eventType, Ea.UnorderedMap<StringHash, Variant> data)
    {
        var timeStep = data.TryGetValue(CoreEvents.Update.TimeStep, out var value) ? value.Float : 0f;
        _angle += 45f * timeStep;
        _cube!.Rotation = new Quaternion(0, _angle, 0);
    }

    public override void Stop()
    {
        _viewport?.Dispose();
        _scene?.Dispose();
    }

    private static T Require<T>(T? value, string name) where T : class
        => value ?? throw new InvalidOperationException($"Could not create or load {name}.");
}
