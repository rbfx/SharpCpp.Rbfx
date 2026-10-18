using Urho3D.Core;
using Urho3D.IO;
using Urho3D.Plugins;
using Urho3D.Scene;

namespace AotApp;

/// <summary>
/// The managed component of the NativeAOT head test. The generated type registry registers it, the engine attribute
/// "Speed" comes from the <see cref="SerializeFieldAttribute"/>, and the engine calls the <see cref="Update"/> override.
/// </summary>
[FactoryReflection]
public partial class AotSpinner : LogicComponent
{
    private bool _updateLogged;

    /// <summary>The number of instances that native code called through the <see cref="Update"/> override.</summary>
    public static int OverrideCalls;

    /// <summary>The engine attribute "Speed".</summary>
    [SerializeField]
    public float Speed = 1.0f;

    /// <summary>A private auto-property: the registry reaches it with get_ and set_ accessors.</summary>
    [SerializeField(Name = "Hidden Gain")]
    private float HiddenGain { get; set; } = 0.5f;

    public AotSpinner(Context context)
        : base(context)
    {
    }

    public float Gain => HiddenGain;

    public override void Update(float timeStep)
    {
        base.Update(timeStep);
        if (!_updateLogged)
        {
            _updateLogged = true;
            System.Threading.Interlocked.Increment(ref OverrideCalls);
            Log.Info("AotApp: override called");
        }
    }
}

/// <summary>
/// A component without an override of a native virtual method. Its <c>Update</c> hides the virtual method and does not
/// override it, so the generated <c>IsManagedOverride</c> is false and native code never calls it.
/// </summary>
[FactoryReflection]
public partial class AotPlainSpinner : LogicComponent
{
    /// <summary>The number of calls of the hiding <c>Update</c> method.</summary>
    public static int HidingCalls;

    public AotPlainSpinner(Context context)
        : base(context)
    {
    }

    public new void Update(float timeStep)
    {
        System.Threading.Interlocked.Increment(ref HidingCalls);
    }
}

/// <summary>A generic base type with a private field: the registry uses the generic form of UnsafeAccessor.</summary>
public abstract partial class AotGenericBase<T> : LogicComponent
{
    [SerializeField(Name = "Generic Count")]
    private int _genericCount = 3;

    protected AotGenericBase(Context context)
        : base(context)
    {
    }

    public int GenericCount => _genericCount;
}

[FactoryReflection]
public partial class AotGenericSpinner : AotGenericBase<int>
{
    public AotGenericSpinner(Context context)
        : base(context)
    {
    }
}
