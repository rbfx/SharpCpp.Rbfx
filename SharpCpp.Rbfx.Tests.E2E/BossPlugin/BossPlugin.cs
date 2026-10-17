using SharpCpp.Rbfx.Tests.E2E.AttributePlugin;

using Urho3D.Core;
using Urho3D.Plugins;
using Urho3D.Scene;

[assembly: PluginModule(typeof(SharpCpp.Rbfx.Tests.E2E.BossPlugin.BossPluginEntry), Name = "E2E.BossPlugin")]

namespace SharpCpp.Rbfx.Tests.E2E.BossPlugin;

public partial class BossPluginEntry : ManagedPlugin
{
    public BossPluginEntry(Context context)
        : base(context)
    {
    }
}

// The base type AttributeComponent is in another assembly with its own registry. The registry of this assembly lists
// only the attribute of this type. The engine reflection of AttributeComponent has the others.
public partial class AttributeBoss : AttributeComponent
{
    [SerializeField]
    public int Rage = 4;

    public AttributeBoss(Context context)
        : base(context)
    {
    }
}
