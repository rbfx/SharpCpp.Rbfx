namespace Urho3D.Plugins;

/// <summary>
/// Explicit attach entry for NativeAOT desktop heads. The module initializer of <see cref="NativeHostShim"/> attaches only on
/// Android, iOS and browser-wasm. A desktop head that links the native library statically calls <see cref="Attach"/> once at start.
/// </summary>
public static class NativeHostBootstrap
{
    /// <summary>
    /// Gives the managed function table to the native library. A second call does nothing.
    /// Returns true when the native library holds the table. Returns false when the host library has no shim.
    /// </summary>
    public static bool Attach()
    {
        NativeHostShim.AttachCore();
        return NativeHostShim.IsAttached;
    }
}
