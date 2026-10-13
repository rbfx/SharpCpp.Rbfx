using System.Runtime.InteropServices;
using Urho3D.Scene;

namespace RbfxNativeHost;

/// <summary>Managed functions that the C++ application calls through DotNetHost.</summary>
public static class Scripting
{
    /// <summary>Creates a <see cref="Spinner"/> and adds it to the node.</summary>
    [UnmanagedCallersOnly]
    public static void AttachSpinner(nint nodeHandle, float speed)
    {
        try
        {
            var node = Node.CreateManagedWrapper(nodeHandle, ownsHandle: false);
            var spinner = new Spinner(node.GetContext())
            {
                Speed = speed
            };
            node.AddComponent(spinner, 0);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"AttachSpinner failed: {exception}");
        }
    }
}
