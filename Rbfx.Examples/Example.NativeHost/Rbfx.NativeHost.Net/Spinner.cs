using Urho3D.Core;
using Urho3D.IO;
using Urho3D.Plugins;
using Urho3D.Scene;

namespace RbfxNativeHost;

/// <summary>C# component that rotates its node around the Y axis.</summary>
[FactoryReflection]
public partial class Spinner : LogicComponent
{
    private const int LogInterval = 5;

    private int _updates;

    /// <summary>Rotation speed in degrees per second. It is the engine attribute "Speed".</summary>
    [SerializeField]
    public float Speed { get; set; } = 45.0f;

    public Spinner(Context context)
        : base(context)
    {
    }

    public override void Update(float timeStep)
    {
        base.Update(timeStep);
        _updates++;
        GetNode().Yaw(Speed * timeStep, TransformSpace.TsLocal);
        if (_updates % LogInterval == 0)
        {
            Log.Info($"C# spinner: {_updates} updates");
        }
    }
}
