using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using SharpCpp.Support;

namespace SharpCpp.Rbfx.Bindings.Interop;

/// <summary>
/// The managed side of a native attribute accessor. The native accessor holds a GC handle to this object
/// and frees the handle when the last <c>AttributeInfo</c> that uses the accessor is destroyed.
/// </summary>
internal abstract class ManagedAttributeBinding
{
    /// <summary>Writes the attribute value of the native serializable into the native variant.</summary>
    public abstract void Get(nint serializable, nint dest);

    /// <summary>Reads the native variant into the attribute of the native serializable.</summary>
    public abstract void Set(nint serializable, nint src);

}

/// <summary>
/// An attribute accessor that forwards the engine calls to a <see cref="ManagedAttributeBinding"/>.
/// C# cannot subclass <see cref="global::Urho3D.Core.AttributeAccessor"/> as a director, so the native class
/// <c>ManagedAttributeAccessor</c> calls back into managed code through function pointers.
/// </summary>
internal sealed partial class ManagedAttributeAccessor : global::Urho3D.Core.AttributeAccessor
{
    private static int s_liveAccessors;
    private static int s_exitHooked;

    /// <summary>The number of native accessors that are not destroyed. The tests read it to check the reference balance.</summary>
    internal static int LiveAccessorCount => Volatile.Read(ref s_liveAccessors);

    private ManagedAttributeAccessor(nint handle)
        : base(handle, ownsHandle: true)
    {
    }

    public override void Get(global::Urho3D.Scene.Serializable ptr, global::Urho3D.Core.Variant dest)
    {
        global::Urho3D.Core.AttributeAccessor.__PInvoke.Get(this, ptr, dest);
        GC.KeepAlive(ptr);
        GC.KeepAlive(dest);
    }

    public override void Set(global::Urho3D.Scene.Serializable ptr, global::Urho3D.Core.Variant src)
    {
        global::Urho3D.Core.AttributeAccessor.__PInvoke.Set(this, ptr, src);
        GC.KeepAlive(ptr);
        GC.KeepAlive(src);
    }

    /// <summary>
    /// Creates a native accessor for the binding. The returned wrapper owns one counted reference.
    /// An <c>AttributeInfo</c> takes its own reference, so dispose the wrapper after the attribute is added.
    /// </summary>
    public static unsafe ManagedAttributeAccessor Create(ManagedAttributeBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        if (Interlocked.Exchange(ref s_exitHooked, 1) == 0)
        {
            AppDomain.CurrentDomain.ProcessExit += static (_, _) => __PInvoke.UninstallCallbacks();
        }

        var gcHandle = GCHandle.Alloc(binding);
        nint native;
        try
        {
            native = __PInvoke.CreateManagedAttributeAccessor(
                GCHandle.ToIntPtr(gcHandle),
                (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&GetFromNative,
                (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&SetFromNative,
                (nint)(delegate* unmanaged[Cdecl]<nint, void>)&FreeFromNative);
        }
        catch
        {
            gcHandle.Free();
            throw;
        }

        Interlocked.Increment(ref s_liveAccessors);

        // The native accessor now owns the GC handle and carries one counted reference.
        // The owning wrapper takes its own reference on registration. Then the creation reference is released.
        try
        {
            var wrapper = new ManagedAttributeAccessor(native);
            NativeObjectRegistry.RegisterInstance<global::Urho3D.Core.AttributeAccessor>(wrapper);
            return wrapper;
        }
        finally
        {
            global::Urho3D.Interop.RefCountedRuntime.ReleaseRef(native);
        }
    }

    /// <summary>
    /// Adds an attribute that uses this accessor to the reflection. The reflection takes its own accessor reference.
    /// Returns false when the reflection does not accept the attribute.
    /// </summary>
    public unsafe bool AddTo(
        global::Urho3D.Core.ObjectReflection reflection,
        global::Urho3D.Core.VariantType type,
        string name,
        IReadOnlyList<string>? enumNames,
        global::Urho3D.Core.Variant defaultValue,
        global::Urho3D.Core.AttributeMode mode)
    {
        ArgumentNullException.ThrowIfNull(reflection);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(defaultValue);

        var count = enumNames?.Count ?? 0;
        var names = new nint[count];
        try
        {
            for (var i = 0; i < count; i++)
            {
                names[i] = Marshal.StringToCoTaskMemUTF8(enumNames![i]);
            }

            fixed (nint* namesPointer = names)
            {
                var added = __PInvoke.AddManagedAttribute(
                    NativeObject.GetHandle(reflection), (uint)type, name, NativeObject.GetHandle(this),
                    namesPointer, count, NativeObject.GetHandle(defaultValue), (uint)(int)mode);
                GC.KeepAlive(reflection);
                GC.KeepAlive(defaultValue);
                GC.KeepAlive(this);
                return added != 0;
            }
        }
        finally
        {
            foreach (var pointer in names)
            {
                Marshal.FreeCoTaskMem(pointer);
            }
        }
    }

    /// <summary>Copies the source variant into the native destination variant.</summary>
    internal static void AssignVariant(nint dest, global::Urho3D.Core.Variant src)
    {
        __PInvoke.VariantAssign(dest, NativeObject.GetHandle(src));
        GC.KeepAlive(src);
    }

    /// <summary>Receives the warnings and the errors of the calling thread, before they go to the engine log. Tests only.</summary>
    [ThreadStatic]
    internal static Action<string>? MessageObserver;

    /// <summary>Writes a warning to the engine log. Exceptions are ignored.</summary>
    internal static void LogWarning(string message)
    {
        MessageObserver?.Invoke(message);
        try
        {
            __PInvoke.LogWarning(message);
        }
        catch
        {
            // Logging must not fail the caller.
        }
    }

    /// <summary>Writes an error to the engine log. Exceptions are ignored.</summary>
    internal static void LogError(string message)
    {
        MessageObserver?.Invoke(message);
        try
        {
            global::Urho3D.IO.Log.Error(message);
        }
        catch
        {
            // Logging must not fail the caller.
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void GetFromNative(nint handle, nint serializable, nint dest)
    {
        try
        {
            ((ManagedAttributeBinding)GCHandle.FromIntPtr(handle).Target!).Get(serializable, dest);
        }
        catch (Exception exception)
        {
            LogWarning($"A managed attribute getter failed: {exception}");
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void SetFromNative(nint handle, nint serializable, nint src)
    {
        try
        {
            ((ManagedAttributeBinding)GCHandle.FromIntPtr(handle).Target!).Set(serializable, src);
        }
        catch (Exception exception)
        {
            LogWarning($"A managed attribute setter failed: {exception}");
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void FreeFromNative(nint handle)
    {
        try
        {
            GCHandle.FromIntPtr(handle).Free();
            Interlocked.Decrement(ref s_liveAccessors);
        }
        catch
        {
            // The handle is already free. Nothing more to do.
        }
    }

    private static partial class __PInvoke
    {
        [LibraryImport(SharpCpp.Rbfx.Interop.NativeLibrary.Name, EntryPoint = "sharpcpp_rbfx_create_managed_attribute_accessor")]
        internal static partial nint CreateManagedAttributeAccessor(nint handle, nint get, nint set, nint free);

        [LibraryImport(SharpCpp.Rbfx.Interop.NativeLibrary.Name, EntryPoint = "sharpcpp_rbfx_add_managed_attribute", StringMarshalling = StringMarshalling.Utf8)]
        internal static unsafe partial byte AddManagedAttribute(
            nint reflection, uint type, string name, nint accessor, nint* enumNames, int enumNameCount, nint defaultValue, uint mode);

        [LibraryImport(SharpCpp.Rbfx.Interop.NativeLibrary.Name, EntryPoint = "sharpcpp_rbfx_uninstall_managed_attribute_callbacks")]
        internal static partial void UninstallCallbacks();

        [LibraryImport(SharpCpp.Rbfx.Interop.NativeLibrary.Name, EntryPoint = "sharpcpp_rbfx_variant_assign")]
        internal static partial void VariantAssign(nint dest, nint src);

        [LibraryImport(SharpCpp.Rbfx.Interop.NativeLibrary.Name, EntryPoint = "sharpcpp_rbfx_log_warning", StringMarshalling = StringMarshalling.Utf8)]
        internal static partial void LogWarning(string message);
    }
}
