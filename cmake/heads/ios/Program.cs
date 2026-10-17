using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Rbfx.Head;

/// <summary>
/// The managed entry of the iOS head. The .NET runtime runs it first. It runs the module initializer of
/// SharpCpp.Rbfx.NativeHost. The initializer attaches the managed function table to the host library, so
/// DotNetHost::Initialize in the native application finds the runtime, as on desktop. Then Main calls the
/// native entry (heads/ios/RbfxIosMain.cpp). The native entry starts UIKit through SDL and does not return.
/// </summary>
internal static partial class Program
{
    /// <summary>
    /// Runs in Main after the module initializer of the native host and before the native start. The engine Context does
    /// not exist yet: do not use engine objects. A partial class of the head can implement it (HEAD_SOURCES).
    /// </summary>
    static partial void OnManagedStartup();

    /// <summary>Declared for the same class on every platform. This head never calls it: the native entry does not return.</summary>
    static partial void OnManagedShutdown();

    // The application is static code in the executable of the app.
    [DllImport("__Internal", EntryPoint = "rbfx_application_start")]
    private static extern int ApplicationStart();

    private static int Main(string[] args)
    {
        RuntimeHelpers.RunModuleConstructor(typeof(Urho3D.Plugins.NativeHostModule).Module.ModuleHandle);
        OnManagedStartup();
        return ApplicationStart();
    }
}
