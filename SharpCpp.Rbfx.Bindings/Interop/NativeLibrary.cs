namespace SharpCpp.Rbfx.Interop;

internal static class NativeLibrary
{
    // The wrapper library. It has the generated bindings.
#if SHARPCPP_BROWSER_WASM
    public const string Name = "libSharpCpp.Rbfx.Native";
#elif SHARPCPP_STATIC_RUNTIME
    public const string Name = "__Internal";
#else
    public const string Name = "SharpCpp.Rbfx.Native";
#endif
}
