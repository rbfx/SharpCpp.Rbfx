using Urho3D.Core;
using Urho3D.Math;
using Urho3D.Plugins;
using Urho3D.Scene;

using SharpCpp.Rbfx.Bindings.Interop;

[assembly: PluginModule(typeof(SharpCpp.Rbfx.Tests.E2E.NoRegistryPlugin.NoRegistryPluginEntry), Name = "E2E.NoRegistryPlugin")]

namespace SharpCpp.Rbfx.Tests.E2E.NoRegistryPlugin;

// The project does not use the source generator, so the assembly has no managed type registry.
// ManagedPlugin logs a warning and scans the assembly. The RTTI members below are what the generator writes.
public class NoRegistryPluginEntry : ManagedPlugin
{
    public NoRegistryPluginEntry(Context context)
        : base(context)
    {
    }
}

public class NoRegistryComponent : Component, IManagedObjectFactory<NoRegistryComponent>
{
    [SerializeField]
    public float Speed = 4.5f;

    public NoRegistryComponent(Context context)
        : base(context)
    {
    }

    public override StringHash GetType() => GetTypeStatic();

    public override Ea.String GetTypeName() => GetTypeNameStatic();

    public override TypeInfo GetTypeInfo() => GetTypeInfoStatic();

    public override bool IsInstanceOf(StringHash type) => GetTypeInfoStatic().IsTypeOf(type);

    public new static StringHash GetTypeStatic() => GetTypeInfoStatic().GetType();

    public new static Ea.String GetTypeNameStatic() => GetTypeInfoStatic().GetTypeName();

    public new static TypeInfo GetTypeInfoStatic() => ManagedTypeInfoRegistry.GetOrCreate("NoRegistryComponent", Component.GetTypeInfoStatic());

    static NoRegistryComponent IManagedObjectFactory<NoRegistryComponent>.Create(Context context) => new(context);
}
