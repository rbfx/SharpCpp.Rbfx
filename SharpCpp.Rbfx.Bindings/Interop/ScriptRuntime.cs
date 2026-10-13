using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Urho3D.Interop;

// Managed half of the engine ScriptRuntimeApi. The native half is in ScriptRuntime.cpp.
// The engine calls these callbacks on any thread that changes a refcount or drops the last reference.
// The callbacks only touch GC handles and wrapper state. They never throw into native code.
internal static unsafe partial class ScriptRuntime
{
    private static readonly object Sync = new();
    private static bool _installed;

    [StructLayout(LayoutKind.Sequential)]
    private struct Callbacks
    {
        public delegate* unmanaged[Cdecl]<nint, void> Dispose;
        public delegate* unmanaged[Cdecl]<nint, void> FreeHandle;
        public delegate* unmanaged[Cdecl]<nint, nint> CloneHandle;
        public delegate* unmanaged[Cdecl]<nint, byte, nint> RecreateHandle;
        public delegate* unmanaged[Cdecl]<void> FullGc;
    }

    // The instance cache calls this before it stores the first wrapper handle in a native object.
    internal static void EnsureInstalled()
    {
        lock (Sync)
        {
            if (_installed)
            {
                return;
            }

            var callbacks = new Callbacks
            {
                Dispose = &Dispose,
                FreeHandle = &FreeHandle,
                CloneHandle = &CloneHandle,
                RecreateHandle = &RecreateHandle,
                FullGc = &FullGc,
            };
            __PInvoke.Install(&callbacks);
            AppDomain.CurrentDomain.ProcessExit += static (_, _) => __PInvoke.Uninstall();
            _installed = true;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Dispose(nint handle)
    {
        try
        {
            if (GCHandle.FromIntPtr(handle).Target is RefCountedNativeObject instance)
            {
                instance.DisposeFromNativeRuntime();
            }
        }
        catch
        {
            // The native side frees the handle if the wrapper did not.
        }
    }

    // Outside of Dispose the engine frees a wrapper handle only in the RefCounted destructor.
    // The native object is then already destroyed, so the wrapper must not touch it again.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void FreeHandle(nint handle)
    {
        try
        {
            var gcHandle = GCHandle.FromIntPtr(handle);
            if (gcHandle.Target is RefCountedNativeObject instance)
            {
                instance.DetachFromDestroyedNativeObject();
            }

            gcHandle.Free();
        }
        catch
        {
            // Nothing more to do.
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint CloneHandle(nint handle)
    {
        try
        {
            var target = GCHandle.FromIntPtr(handle).Target;
            return target is null ? 0 : GCHandle.ToIntPtr(GCHandle.Alloc(target));
        }
        catch
        {
            return 0;
        }
    }

    // The engine asks for a strong handle when a native reference is added to an object with a weak wrapper handle.
    // The native object then keeps the wrapper alive until it dies and disposes it. The engine calls this only while
    // RefCounted marks the handle as weak, so one handle is never recreated twice.
    // The new handle is allocated before the old one is freed. When a step fails, the result is the new handle or 0.
    // The old handle is never returned after a failure, because it can be invalid. A failure can leak it, but never frees it twice.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint RecreateHandle(nint handle, byte strong)
    {
        nint replacement = 0;
        try
        {
            var gcHandle = GCHandle.FromIntPtr(handle);
            var target = gcHandle.Target;
            if (target is not null)
            {
                replacement = GCHandle.ToIntPtr(GCHandle.Alloc(target, strong != 0 ? GCHandleType.Normal : GCHandleType.Weak));
            }

            gcHandle.Free();
            return replacement;
        }
        catch
        {
            return replacement;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void FullGc()
    {
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        catch
        {
            // Nothing more to do.
        }
    }

    private static partial class __PInvoke
    {
        [LibraryImport(SharpCpp.Rbfx.Interop.NativeLibrary.Name, EntryPoint = "sharpcpp_rbfx_install_script_runtime")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static partial bool Install(Callbacks* callbacks);

        [LibraryImport(SharpCpp.Rbfx.Interop.NativeLibrary.Name, EntryPoint = "sharpcpp_rbfx_uninstall_script_runtime")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial void Uninstall();
    }
}
