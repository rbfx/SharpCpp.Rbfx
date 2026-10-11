namespace SharpCpp.Rbfx.Interop;

internal static class NativeLibrary
{
#if SHARPCPP_STATIC_RUNTIME
    public const string Name = "__Internal";
#else
    public const string Name = "SharpCpp.Rbfx.Native";
#endif
}
