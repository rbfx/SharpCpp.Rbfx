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

    public AotSpinner(Context context)
        : base(context)
    {
    }

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
