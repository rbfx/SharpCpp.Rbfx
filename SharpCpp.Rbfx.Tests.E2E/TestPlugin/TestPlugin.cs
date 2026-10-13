using SharpCpp.Rbfx.Bindings.Interop;

using Urho3D.Core;
using Urho3D.Math;
using Urho3D.Plugins;
using Urho3D.Scene;

[assembly: PluginModule(typeof(SharpCpp.Rbfx.Tests.E2E.TestPlugin.TestPluginEntry), Name = "E2E.TestPlugin")]

namespace SharpCpp.Rbfx.Tests.E2E.TestPlugin;

// The test reads this state by reflection. It does not reference the plugin assembly.
public static class TestPluginState
{
    public static List<string> Events { get; } = new();

    public static int Disposed { get; set; }
}

public partial class TestPluginEntry : Plugin
{
    public TestPluginEntry(Context context)
        : base(context)
    {
    }

    protected override void Load()
    {
        Context.RegisterFactory<TestPluginComponent>("E2E.TestPlugin");
        TestPluginState.Events.Add("Load");
    }

    protected override void Unload()
    {
        Context.RemoveReflection(new StringHash(nameof(TestPluginComponent)));
        TestPluginState.Events.Add("Unload");
    }

    protected override void Start(bool isMain) => TestPluginState.Events.Add($"Start({isMain})");

    protected override void Stop() => TestPluginState.Events.Add("Stop");

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            TestPluginState.Disposed++;
        }

        base.Dispose(disposing);
    }
}

public partial class TestPluginComponent : Component
{
    public TestPluginComponent(Context context)
        : base(context)
    {
    }
}
