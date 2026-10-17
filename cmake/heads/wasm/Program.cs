using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Rbfx.Head;

/// <summary>
/// The managed entry of the browser head. main.js runs it after the .NET runtime starts and before it calls the
/// native entry of the application. It runs the module initializer of SharpCpp.Rbfx.NativeHost. The initializer
/// attaches the managed function table to the host library, so DotNetHost::Initialize in the native application
/// finds the runtime, as on desktop. Main returns at once: the engine runs on the browser thread (see main.js).
/// </summary>
internal static partial class Program
{
    /// <summary>
    /// Runs in Main after the module initializer of the native host and before the native entry. The engine Context does
    /// not exist yet: do not use engine objects. A partial class of the head can implement it (HEAD_SOURCES).
    /// </summary>
    static partial void OnManagedStartup();

    /// <summary>Declared for the same class on every platform. This head never calls it: Main returns at once.</summary>
    static partial void OnManagedShutdown();

    // The generated trimmer roots (SharpCpp.Rbfx.SourceGenerator): the plugin class, the entry points that native code
    // finds by name, and the roots of the referenced assemblies. The head has no TrimmerRootAssembly items.
    [DynamicDependency(nameof(global::SharpCpp.Rbfx.Generated.TrimRoots.Keep), typeof(global::SharpCpp.Rbfx.Generated.TrimRoots))]
    private static int Main()
    {
        RuntimeHelpers.RunModuleConstructor(typeof(Urho3D.Plugins.NativeHostModule).Module.ModuleHandle);
        OnManagedStartup();
        return 0;
    }
}
