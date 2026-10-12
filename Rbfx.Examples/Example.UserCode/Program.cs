using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Rbfx.Examples.Common;
using RbfxExamples;
using Urho3D.Core;
using Urho3D.Engine;
using Urho3D.IO;
using Urho3D.Math;
using Urho3D.Scene;

namespace Example.UserCode;

internal static class Program
{
    private static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

        var headless = false;
        var frames = 0;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--headless")
            {
                headless = true;
            }
            else if (args[i] == "--frames" && i + 1 < args.Length
                && int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                frames = value;
            }
        }

        // A headless run without a frame count would never end.
        if (headless && frames <= 0)
        {
            frames = 60;
        }

        using var context = new Context();
        using var app = new UserCodeApplication(context, headless, frames);
        return app.Run();
    }
}

/// <summary>Application that adds the native RotatingComponent of the example to a node.</summary>
internal sealed partial class UserCodeApplication : Application
{
    private readonly Context _context;
    private readonly bool _headless;
    private readonly int _frames;
    private SceneSample _sample;
    private RotatingComponent _component;
    private int _frameCount;

    public UserCodeApplication(Context context, bool headless, int frames)
        : base(context)
    {
        _context = context;
        _headless = headless;
        _frames = frames;
    }

    public override void Setup()
    {
        EngineParameters[EngineDefs.Headless] = _headless;
        EngineParameters[EngineDefs.EngineAutoLoadScripts] = false;
        EngineParameters[EngineDefs.WindowTitle] = "Example.UserCode";
    }

    public override void Start()
    {
        _sample = new SceneSample(_context);
        _component = NativeMethods.AddRotatingComponent(_sample.PrimaryNode, 45.0f);
        Log($"Example.UserCode: RotatingComponent speed {_component.GetRotationSpeed()} (frames: {_frames}, headless: {_headless})");

        SubscribeToEvent(CoreEvents.Update, OnUpdate);
    }

    public override void Stop()
    {
        _sample?.Dispose();
    }

    private void OnUpdate(Urho3D.Core.Object sender, StringHash eventType, Ea.UnorderedMap<StringHash, Variant> eventData)
    {
        if (_frames <= 0 || ++_frameCount < _frames)
        {
            return;
        }

        Log($"Finished after {_frameCount} frames, component updates: {_component.GetUpdateCount()}");
        Engine.Exit();
    }

    private static void Log(string message)
    {
        Urho3D.IO.Log.GetLogger().Write(LogLevel.LogInfo, Encoding.UTF8.GetBytes(message));
    }
}

internal static partial class NativeMethods
{
    [LibraryImport("Example.UserCode.Native", EntryPoint = "RbfxExamples_AddRotatingComponent")]
    private static partial nint AddRotatingComponent(nint node, float degreesPerSecond);

    /// <summary>Creates a RotatingComponent on the node through the native helper of the example.</summary>
    public static RotatingComponent AddRotatingComponent(Node node, float degreesPerSecond)
    {
        var handle = AddRotatingComponent(SharpCpp.Support.NativeObject.GetHandle(node), degreesPerSecond);
        GC.KeepAlive(node);
        return handle == 0 ? null : RotatingComponent.CreateManagedWrapper(handle, ownsHandle: false);
    }
}
