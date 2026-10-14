using System.Runtime.CompilerServices;

namespace Rbfx.Head;

/// <summary>
/// The managed entry of the browser head. main.js runs it after the .NET runtime starts and before it calls the
/// native entry of the application. It runs the module initializer of SharpCpp.Rbfx.NativeHost. The initializer
/// attaches the managed function table to the host library, so DotNetHost::Initialize in the native application
/// finds the runtime, as on desktop. Main returns at once: the engine runs on the browser thread (see main.js).
/// </summary>
internal static class Program
{
    private static int Main()
    {
        RuntimeHelpers.RunModuleConstructor(typeof(Urho3D.Plugins.NativeHostModule).Module.ModuleHandle);
        return 0;
    }
}
