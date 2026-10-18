using SharpCpp.Rbfx.Bindings.Interop;

using Urho3D.Core;
using Urho3D.Math;
using Urho3D.Plugins;
using Urho3D.Scene;

[assembly: PluginModule(typeof(SharpCpp.Rbfx.Tests.E2E.RetryPlugin.RetryPluginEntry), Name = "E2E.RetryPlugin")]

namespace SharpCpp.Rbfx.Tests.E2E.RetryPlugin;

// A plain plugin for the load-failure tests. The test injects the failure outside of the plugin,
// because an exception in a plugin callback ends the process.
public partial class RetryPluginEntry : Plugin
{
    public RetryPluginEntry(Context context)
        : base(context)
    {
    }

    protected override void Load() => Context.RegisterFactory<RetryPluginComponent>("E2E.RetryPlugin");

    protected override void Unload() => Context.RemoveReflection(new StringHash(nameof(RetryPluginComponent)));
}

[FactoryReflection]
public partial class RetryPluginComponent : Component
{
    public RetryPluginComponent(Context context)
        : base(context)
    {
    }
}
