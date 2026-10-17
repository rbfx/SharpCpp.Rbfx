using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Urho3D.Interop;

internal static partial class RefCountedRuntime
{
    [LibraryImport(SharpCpp.Rbfx.Interop.NativeLibrary.Name, EntryPoint = "sharpcpp_rbfx_refcounted_take_expired_script_object")]
    internal static partial nint TakeExpiredScriptObjectHandle();

    [LibraryImport(SharpCpp.Rbfx.Interop.NativeLibrary.Name, EntryPoint = "sharpcpp_rbfx_refcounted_get_script_object")]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial nint GetScriptObjectHandle(nint self);

    [LibraryImport(SharpCpp.Rbfx.Interop.NativeLibrary.Name, EntryPoint = "sharpcpp_rbfx_refcounted_set_script_object")]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial void SetScriptObjectHandle(nint self, nint gcHandle, [MarshalAs(UnmanagedType.I1)] bool isStrong);

    [LibraryImport(SharpCpp.Rbfx.Interop.NativeLibrary.Name, EntryPoint = "sharpcpp_rbfx_refcounted_reset_script_object")]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial void ResetScriptObjectHandle(nint self);

    [LibraryImport(SharpCpp.Rbfx.Interop.NativeLibrary.Name, EntryPoint = "sharpcpp_rbfx_refcounted_add_ref")]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial void AddRef(nint self);

    [LibraryImport(SharpCpp.Rbfx.Interop.NativeLibrary.Name, EntryPoint = "sharpcpp_rbfx_refcounted_release_ref")]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial void ReleaseRef(nint self);

    [LibraryImport(SharpCpp.Rbfx.Interop.NativeLibrary.Name, EntryPoint = "sharpcpp_rbfx_release_shared_ptr_holder")]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial void ReleaseExternalSharedPtr(nint sharedPtr);
}
