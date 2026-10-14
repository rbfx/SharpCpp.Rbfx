using System.Runtime.CompilerServices;

namespace Urho3D.Plugins;

/// <summary>
/// The marker of the assembly SharpCpp.Rbfx.NativeHost. A head touches the assembly before the native entry:
/// <c>RuntimeHelpers.RunModuleConstructor(typeof(NativeHostModule).Module.ModuleHandle)</c>.
/// The module initializer then attaches the managed function table to the host library.
/// Only an application that links <c>SharpCpp.Rbfx.Host</c> (a head on Android, iOS or the web) references this assembly.
/// </summary>
public static class NativeHostModule
{
    /// <summary>True after the host library accepted the managed function table.</summary>
    public static bool IsAttached => NativeHostShim.IsAttached;
}
