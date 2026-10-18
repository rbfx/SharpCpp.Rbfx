using Urho3D.Core;
using Urho3D.IO;
using Urho3D.Plugins;
using Urho3D.Scene;

namespace Rbfx.Head;

/// <summary>
/// The hooks of the NativeAOT desktop head (HEAD_SOURCES in the CMakeLists.txt of the test). The hook registers the
/// types of the head assembly. The plugin of the test cannot reference the head assembly, so it creates
/// <see cref="AotHeadSpinner"/> by its type name.
/// </summary>
internal static partial class Program
{
    static partial void OnManagedStartup()
    {
        // The engine Context does not exist yet. The hook only registers the assembly.
        ManagedTypeRegistries.Add(typeof(Program).Assembly);
    }
}

/// <summary>
/// A managed component in the head assembly. The generated type registry of the head registers it, the engine attribute
/// "HeadSpeed" comes from the <see cref="SerializeFieldAttribute"/>, and the engine calls the <see cref="Update"/> override.
/// </summary>
[FactoryReflection]
public partial class AotHeadSpinner : LogicComponent
{
    private bool _updateLogged;

    /// <summary>The engine attribute "HeadSpeed".</summary>
    [SerializeField]
    public float HeadSpeed = 1.0f;

    public AotHeadSpinner(Context context)
        : base(context)
    {
    }

    public override void Update(float timeStep)
    {
        base.Update(timeStep);
        if (!_updateLogged)
        {
            _updateLogged = true;
            Log.Info("AotApp: head override called");
        }
    }
}
