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

[FactoryReflection]
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

    // The generated registry reaches a private member through UnsafeAccessor.
    [SerializeField(Name = "Secret Value")]
    private int _secret = 42;

    // A private auto-property: the registry reaches it through get_ and set_ accessors.
    [SerializeField(Name = "Hidden Gain")]
    private float HiddenGain { get; set; } = 1.5f;

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

    public float Gain => HiddenGain;
}

// Private members of a generic base type use the generic form of UnsafeAccessor.
#pragma warning disable CS8618, CS0649
public abstract partial class AttributeGenericBase<T> : Component
{
    [SerializeField(Name = "Generic Count")]
    private int _genericCount = 3;

    [SerializeField(Name = "Generic Gain")]
    private float _genericGain = 0.25f;

    // A type parameter field is not described: the runtime does not find a private field of that type through UnsafeAccessor.
    private T _item;

    protected AttributeGenericBase(Context context)
        : base(context)
    {
    }

    public int GenericCount => _genericCount;

    public float GenericGainValue => _genericGain;

    public T Item => _item;
}
#pragma warning restore CS8618, CS0649

[FactoryReflection]
public partial class AttributeGenericComponent : AttributeGenericBase<int>
{
    public AttributeGenericComponent(Context context)
        : base(context)
    {
    }
}

// The derived type copies the attributes of AttributeComponent from its reflection.
[FactoryReflection]
public partial class DerivedAttributeComponent : AttributeComponent
{
    [SerializeField]
    public double Extra = 0.5;

    public DerivedAttributeComponent(Context context)
        : base(context)
    {
    }
}

// The registry does not list this abstract type. A type of another plugin can derive from it.
public abstract partial class AttributeMiddle : AttributeComponent
{
    protected AttributeMiddle(Context context)
        : base(context)
    {
    }
}
