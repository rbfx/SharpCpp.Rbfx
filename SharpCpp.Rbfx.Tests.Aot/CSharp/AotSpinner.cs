using Urho3D.Core;
using Urho3D.IO;
using Urho3D.Scene;

namespace AotApp;

/// <summary>
/// The managed component of the NativeAOT head test. The generated type registry registers it, the engine attribute
/// "Speed" comes from the <see cref="SerializeFieldAttribute"/>, and the engine calls the <see cref="Update"/> override.
/// </summary>
public partial class AotSpinner : LogicComponent
{
    private bool _updateLogged;

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
            Log.Info("AotApp: override called");
        }
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

public partial class AotGenericSpinner : AotGenericBase<int>
{
    public AotGenericSpinner(Context context)
        : base(context)
    {
    }
}
