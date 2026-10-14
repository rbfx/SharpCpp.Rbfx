using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Rbfx.Head;

/// <summary>
/// The managed entry of the iOS head. The .NET runtime runs it first. It runs the module initializer of
/// SharpCpp.Rbfx.NativeHost. The initializer attaches the managed function table to the host library, so
/// DotNetHost::Initialize in the native application finds the runtime, as on desktop. Then Main calls the
/// native entry (heads/ios/RbfxIosMain.cpp). The native entry starts UIKit through SDL and does not return.
/// </summary>
internal static class Program
{
    // The application is static code in the executable of the app.
    [DllImport("__Internal", EntryPoint = "rbfx_application_start")]
    private static extern int ApplicationStart();

    private static int Main(string[] args)
    {
        RuntimeHelpers.RunModuleConstructor(typeof(Urho3D.Plugins.NativeHostModule).Module.ModuleHandle);
        return ApplicationStart();
    }
}
