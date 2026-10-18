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
[FactoryReflection]
public partial class AttributeBoss : AttributeComponent
{
    [SerializeField]
    public int Rage = 4;

    public AttributeBoss(Context context)
        : base(context)
    {
    }
}

// An abstract type of the other assembly is between this type and the listed type AttributeComponent.
[FactoryReflection]
public partial class MiddleBoss : AttributeMiddle
{
    [SerializeField]
    public int Fury = 3;

    public MiddleBoss(Context context)
        : base(context)
    {
    }
}

// A generic type has no engine reflection, so the engine type of the derived type has AttributeComponent as its base.
public abstract partial class GenericMiddle<T> : AttributeComponent
{
    [SerializeField]
    public int Level = 5;

    protected GenericMiddle(Context context)
        : base(context)
    {
    }
}

[FactoryReflection]
public partial class GenericBoss : GenericMiddle<int>
{
    [SerializeField]
    public int Power = 6;

    public GenericBoss(Context context)
        : base(context)
    {
    }
}
