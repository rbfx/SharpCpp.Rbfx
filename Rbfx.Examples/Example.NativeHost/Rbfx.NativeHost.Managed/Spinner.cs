using System.Text;
using Urho3D.Core;
using Urho3D.IO;
using Urho3D.Scene;

namespace RbfxNativeHost;

/// <summary>C# component that rotates its node around the Y axis.</summary>
public partial class Spinner : LogicComponent
{
    private const int LogInterval = 5;

    private int _updates;

    /// <summary>Rotation speed in degrees per second.</summary>
    public float Speed { get; set; }

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
            Urho3D.IO.Log.GetLogger().Write(LogLevel.LogInfo, Encoding.UTF8.GetBytes($"C# spinner: {_updates} updates"));
        }
    }
}
