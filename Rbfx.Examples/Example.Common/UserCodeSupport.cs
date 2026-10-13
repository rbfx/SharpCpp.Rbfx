using Urho3D.Scene;
#if RBFX_USERCODE
using RbfxExamples;
#endif

namespace Rbfx.Examples.Common;

/// <summary>
/// Adds the native RotatingComponent of Example.UserCode to a node.
/// Without the RBFX_USERCODE constant the user code is not available and every method does nothing.
/// </summary>
public static class UserCodeSupport
{
#if RBFX_USERCODE
    public const bool IsAvailable = true;

    /// <summary>
    /// Registers the factory of RotatingComponent and creates the component on the node.
    /// Returns a description of the component, or null if the creation fails.
    /// </summary>
    public static string? AddRotatingComponent(Node node, float degreesPerSecond = 45.0f)
    {
        RotatingComponent.RegisterObject(node.GetContext());
        var component = node.CreateComponent<RotatingComponent>();
        if (component is null)
        {
            return null;
        }

        component.SetRotationSpeed(degreesPerSecond);
        return $"RotatingComponent speed {component.GetRotationSpeed()} degrees per second";
    }
#else
    public const bool IsAvailable = false;

    /// <summary>Does nothing and returns null. The user code is not available.</summary>
    public static string? AddRotatingComponent(Node node, float degreesPerSecond = 45.0f) => null;
#endif
}
