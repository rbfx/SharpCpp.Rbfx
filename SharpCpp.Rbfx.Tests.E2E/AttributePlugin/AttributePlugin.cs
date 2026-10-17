using Urho3D.Core;
using Urho3D.Math;
using Urho3D.Plugins;
using Urho3D.Scene;

[assembly: PluginModule(typeof(SharpCpp.Rbfx.Tests.E2E.AttributePlugin.AttributePluginEntry), Name = "E2E.AttributePlugin")]

namespace SharpCpp.Rbfx.Tests.E2E.AttributePlugin;

// The test reads this state by reflection. It does not reference the plugin assembly.
public static class AttributePluginState
{
    public static List<string> Events { get; } = new();
}

public enum AttributeMood
{
    Calm,
    Happy,
    Angry,
}

// The base class registers the types of this assembly. The overrides call the base methods.
public partial class AttributePluginEntry : ManagedPlugin
{
    public AttributePluginEntry(Context context)
        : base(context)
    {
    }

    protected override void Load()
    {
        base.Load();
        AttributePluginState.Events.Add($"Load({RegisteredTypes.Count})");
    }

    protected override void Unload()
    {
        AttributePluginState.Events.Add("Unload");
        base.Unload();
    }
}

public partial class AttributeComponent : Component
{
    [SerializeField]
    public float Speed = 2.5f;

    [SerializeField]
    public int Count = 7;

    [SerializeField]
    public string Label = "hello";

    [SerializeField]
    public Vector3 Offset = new(1, 2, 3);

    [SerializeField]
    public AttributeMood Mood = AttributeMood.Happy;

    // A private member cannot be listed in the generated registry. The test checks the reflection fallback of this type.
#pragma warning disable RBFXSG004
    [SerializeField(Name = "Secret Value")]
    private int _secret = 42;
#pragma warning restore RBFXSG004

    [SerializeField, NonSerialized]
    public int Skipped = 5;

    public int NotMarked = 9;

    public AttributeComponent(Context context)
        : base(context)
    {
    }

    [SerializeField]
    public bool Flag { get; set; } = true;

    public int Secret => _secret;
}

// The derived type copies the attributes of AttributeComponent from its reflection.
public partial class DerivedAttributeComponent : AttributeComponent
{
    [SerializeField]
    public double Extra = 0.5;

    public DerivedAttributeComponent(Context context)
        : base(context)
    {
    }
}
