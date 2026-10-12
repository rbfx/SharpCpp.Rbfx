using Urho3D.Scene;
#if RBFX_USERCODE
using System.Runtime.InteropServices;
using RbfxExamples;
using SharpCpp.Support;
#endif

namespace Rbfx.Examples.Common;

/// <summary>
/// Adds the native RotatingComponent of Example.UserCode to a node.
/// Without the RBFX_USERCODE constant the user code is not available and every method does nothing.
/// </summary>
public static partial class UserCodeSupport
{
#if RBFX_USERCODE
    public const bool IsAvailable = true;

    [LibraryImport("Example.UserCode.Native", EntryPoint = "RbfxExamples_AddRotatingComponent")]
    private static partial nint AddRotatingComponent(nint node, float degreesPerSecond);

    /// <summary>
    /// Creates a RotatingComponent on the node.
    /// Returns a description of the component, or null if the native call fails.
    /// </summary>
    public static string? AddRotatingComponent(Node node, float degreesPerSecond = 45.0f)
    {
        var handle = AddRotatingComponent(NativeObject.GetHandle(node), degreesPerSecond);
        GC.KeepAlive(node);
        if (handle == 0)
        {
            return null;
        }

        var component = RotatingComponent.CreateManagedWrapper(handle, ownsHandle: false);
        return $"RotatingComponent speed {component.GetRotationSpeed()} degrees per second";
    }
#else
    public const bool IsAvailable = false;

    /// <summary>Does nothing and returns null. The user code is not available.</summary>
    public static string? AddRotatingComponent(Node node, float degreesPerSecond = 45.0f) => null;
#endif
}
