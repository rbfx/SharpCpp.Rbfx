using System.Reflection;
using System.Runtime.CompilerServices;

namespace Rbfx.Head;

/// <summary>
/// The managed entry of the browser head. main.js runs it after the .NET runtime starts and before it calls the
/// native entry of the application. It runs the module initializer of the SharpCpp.Rbfx bindings. The initializer
/// attaches the managed function table to the host library, so DotNetHost::Initialize in the native application
/// finds the runtime, as on desktop. Main returns at once: the engine runs on the browser thread (see main.js).
/// </summary>
internal static class Program
{
    private static int Main()
    {
        var bindings = Assembly.Load(new AssemblyName("SharpCpp.Rbfx.Bindings"));
        RuntimeHelpers.RunModuleConstructor(bindings.ManifestModule.ModuleHandle);
        return 0;
    }
}
