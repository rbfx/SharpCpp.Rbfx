using Urho3D.Core;
using Urho3D.Math;
using Urho3D.Plugins;
using Urho3D.Scene;

[assembly: PluginModule(typeof(SharpCpp.Rbfx.Tests.E2E.RegistryPlugin.RegistryPluginEntry), Name = "E2E.RegistryPlugin")]

namespace SharpCpp.Rbfx.Tests.E2E.RegistryPlugin;

// Every [SerializeField] member is public and has a default in its declaration. The generated registry describes it fully,
// so the registration needs no member scan and no Activator.
public partial class RegistryPluginEntry : ManagedPlugin
{
    public RegistryPluginEntry(Context context)
        : base(context)
    {
    }
}

public partial class RegistryFixtureComponent : Component
{
    [SerializeField]
    public float Speed = 3.5f;

    [SerializeField]
    public int Count = 11;

    [SerializeField]
    public string Label = "registry";

    public RegistryFixtureComponent(Context context)
        : base(context)
    {
    }
}

public partial class RegistryFixtureDerivedComponent : RegistryFixtureComponent
{
    [SerializeField]
    public double Extra = 0.25;

    public RegistryFixtureDerivedComponent(Context context)
        : base(context)
    {
    }
}
