using Urho3D.Core;
using Urho3D.IO;
using Urho3D.Math;
using Urho3D.Scene;

namespace DesktopAot;

/// <summary>
/// A managed component that rotates its node around the Y axis. The generated type registry registers it,
/// and the engine attribute "Speed" comes from the <see cref="SerializeFieldAttribute"/>.
/// </summary>
public partial class Spinner : LogicComponent
{
    private bool _logged;

    /// <summary>The rotation speed in degrees per second. It is the engine attribute "Speed".</summary>
    [SerializeField]
    public float Speed = 90.0f;

    public Spinner(Context context)
        : base(context)
    {
    }

    public override void Update(float timeStep)
    {
        base.Update(timeStep);
        GetNode().Yaw(Speed * timeStep, TransformSpace.TsLocal);
        if (!_logged)
        {
            _logged = true;
            Log.Info($"DesktopAot: spinner turns at {Speed} degrees per second");
        }
    }
}
