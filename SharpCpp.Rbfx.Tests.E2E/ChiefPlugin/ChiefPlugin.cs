using SharpCpp.Rbfx.Tests.E2E.BossPlugin;

using Urho3D.Core;
using Urho3D.Plugins;
using Urho3D.Scene;

[assembly: PluginModule(typeof(SharpCpp.Rbfx.Tests.E2E.ChiefPlugin.ChiefPluginEntry), Name = "E2E.ChiefPlugin")]

namespace SharpCpp.Rbfx.Tests.E2E.ChiefPlugin;

public partial class ChiefPluginEntry : ManagedPlugin
{
    public ChiefPluginEntry(Context context)
        : base(context)
    {
    }
}

// The base type AttributeBoss is in plugin B, and its base type AttributeComponent is in plugin A.
// The registry of this assembly lists only "Rank". The engine reflection of AttributeBoss has the others.
public partial class ChiefBoss : AttributeBoss
{
    [SerializeField]
    public int Rank = 9;

    public ChiefBoss(Context context)
        : base(context)
    {
    }
}
