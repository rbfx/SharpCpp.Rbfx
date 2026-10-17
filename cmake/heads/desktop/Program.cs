using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Rbfx.Head;

/// <summary>
/// The exports of the NativeAOT desktop head. The native launcher keeps main. DotNetHost::Initialize loads this
/// shared library and calls sharpcpp_rbfx_head_attach. The export gives the managed function table to the host
/// library (SharpCpp.Rbfx.Host). DotNetHost::Close calls sharpcpp_rbfx_head_detach. The host does not unload the head.
/// </summary>
internal static partial class Program
{
    /// <summary>Runs in sharpcpp_rbfx_head_attach before the attach. A partial class of the head can implement it.</summary>
    static partial void OnManagedStartup();

    /// <summary>Runs in sharpcpp_rbfx_head_detach. A partial class of the head can implement it.</summary>
    static partial void OnManagedShutdown();

    // Returns 1 when the host library holds the table, else 0. An exception must not leave an UnmanagedCallersOnly method.
    // The generated trimmer roots (SharpCpp.Rbfx.SourceGenerator): the plugin class, the entry points that native code
    // finds by name, and the roots of the referenced assemblies. The head has no TrimmerRootAssembly items.
    [DynamicDependency(nameof(global::SharpCpp.Rbfx.Generated.TrimRoots.Keep), typeof(global::SharpCpp.Rbfx.Generated.TrimRoots))]
    [UnmanagedCallersOnly(EntryPoint = "sharpcpp_rbfx_head_attach")]
    private static int HeadAttach()
    {
        try
        {
            OnManagedStartup();
            return Urho3D.Plugins.NativeHostBootstrap.Attach() ? 1 : 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"sharpcpp_rbfx_head_attach: {exception}");
            return 0;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "sharpcpp_rbfx_head_detach")]
    private static void HeadDetach()
    {
        try
        {
            OnManagedShutdown();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"sharpcpp_rbfx_head_detach: {exception}");
        }
    }
}
