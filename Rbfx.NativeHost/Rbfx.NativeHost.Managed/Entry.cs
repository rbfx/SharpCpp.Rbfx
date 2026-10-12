using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Urho3D.Core;
using Urho3D.Engine;
using Urho3D.IO;
using Urho3D.Math;
using Urho3D.Scene;

namespace RbfxNativeHost;

/// <summary>Managed entry point that the C++ executable calls through DotNetHost.</summary>
public static class Entry
{
    /// <summary>Runs the application. The C++ host calls this method with its own argc and argv.</summary>
    [UnmanagedCallersOnly]
    public static unsafe int Run(int argc, byte** argv)
    {
        try
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

            var headless = false;
            var frames = 60;
            for (var i = 1; i < argc; i++)
            {
                var argument = Marshal.PtrToStringUTF8((nint)argv[i]);
                if (argument == "--headless")
                {
                    headless = true;
                }
                else if (argument == "--frames" && i + 1 < argc
                    && int.TryParse(Marshal.PtrToStringUTF8((nint)argv[++i]), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                {
                    frames = value;
                }
            }

            using var context = new Context();
            using var app = new HostedApplication(context, headless, frames);
            return app.Run();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Rbfx.NativeHost.Managed failed: {exception}");
            return 1;
        }
    }
}

internal sealed partial class HostedApplication : Application
{
    private readonly Context _context;
    private readonly bool _headless;
    private readonly int _frames;
    private Scene _scene;
    private Node _node;
    private Spinner _spinner;
    private int _frameCount;

    public HostedApplication(Context context, bool headless, int frames)
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
        EngineParameters[EngineDefs.WindowTitle] = "Rbfx.NativeHost";
    }

    public override void Start()
    {
        _scene = new Scene(_context);
        _node = _scene.CreateChild("Spinning node");
        _spinner = NativeMethods.AddSpinner(_node, 90.0f);

        using var info = new HostInfo();
        Log($"{info.Describe().ToString()} (frames: {_frames}, headless: {_headless})");

        SubscribeToEvent(CoreEvents.Update, OnUpdate);
    }

    private void OnUpdate(Urho3D.Core.Object sender, StringHash eventType, Ea.UnorderedMap<StringHash, Variant> eventData)
    {
        if (++_frameCount < _frames)
        {
            return;
        }

        Log($"Finished after {_frameCount} frames, spinner updates: {_spinner.GetUpdateCount()}");
        Engine.Exit();
    }

    private static void Log(string message)
    {
        Urho3D.IO.Log.GetLogger().Write(LogLevel.LogInfo, Encoding.UTF8.GetBytes(message));
    }
}

internal static partial class NativeMethods
{
    [LibraryImport("SharpCpp.Rbfx.NativeHost.Native", EntryPoint = "RbfxNativeHost_AddSpinner")]
    private static partial nint AddSpinner(nint node, float degreesPerSecond);

    /// <summary>Creates a Spinner component on the node through the native helper of the example.</summary>
    public static Spinner AddSpinner(Node node, float degreesPerSecond)
    {
        var handle = AddSpinner(SharpCpp.Support.NativeObject.GetHandle(node), degreesPerSecond);
        GC.KeepAlive(node);
        return handle == 0 ? null : Spinner.CreateManagedWrapper(handle, ownsHandle: false);
    }
}
