using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SharpCpp.Support;

namespace SharpCpp.Rbfx.Bindings.Interop;

/// <summary>
/// Implemented by managed object types that the engine can create by type name.
/// The RTTI source generator implements this interface for partial types with a public constructor that takes a context.
/// </summary>
public interface IManagedObjectFactory<T>
    where T : global::Urho3D.Core.Object, IManagedObjectFactory<T>
{
    static abstract T Create(global::Urho3D.Core.Context context);

    static abstract global::Urho3D.Core.TypeInfo GetTypeInfoStatic();
}

public static partial class ManagedObjectFactory
{
    private static readonly ConcurrentDictionary<nint, Func<global::Urho3D.Core.Context, global::Urho3D.Core.Object>> Creators = new();
    private static readonly object CallbackSync = new();
    private static bool _callbackInstalled;

    /// <summary>
    /// Registers a factory for <typeparamref name="T"/> in the context. The engine can then create the type by the hash of its name.
    /// The native object owns the managed instance after creation.
    /// </summary>
    public static unsafe void RegisterFactory<T>(this global::Urho3D.Core.Context context, string category = "")
        where T : global::Urho3D.Core.Object, IManagedObjectFactory<T>
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(category);

        var typeInfo = T.GetTypeInfoStatic();
        var typeInfoHandle = NativeObject.GetHandle(typeInfo);
        Creators[typeInfoHandle] = static ctx => T.Create(ctx);

        lock (CallbackSync)
        {
            if (!_callbackInstalled)
            {
                __PInvoke.SetManagedFactoryCallback((nint)(delegate* unmanaged<nint, nint, nint>)&CreateFromNative);
                _callbackInstalled = true;
            }
        }

        __PInvoke.RegisterManagedFactory(NativeObject.GetHandle(context), typeInfoHandle, category);
        GC.KeepAlive(context);
        GC.KeepAlive(typeInfo);
    }

    /// <summary>Returns true when the type is a concrete Object that implements <see cref="IManagedObjectFactory{T}"/> for itself.</summary>
    public static bool IsFactoryType([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.IsClass && !type.IsAbstract && !type.ContainsGenericParameters &&
            typeof(global::Urho3D.Core.Object).IsAssignableFrom(type) &&
            Array.Exists(type.GetInterfaces(), i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IManagedObjectFactory<>) && i.GenericTypeArguments[0] == type);
    }

    /// <summary>
    /// Registers a factory for <typeparamref name="T"/> and returns its type info. The generated managed type registry
    /// (<see cref="global::Urho3D.Plugins.ManagedTypeRegistryAttribute"/>) calls it. Application code calls <c>RegisterFactory&lt;T&gt;</c>.
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static global::Urho3D.Core.TypeInfo RegisterFactoryAndGetTypeInfo<T>(global::Urho3D.Core.Context context, string category)
        where T : global::Urho3D.Core.Object, IManagedObjectFactory<T>
    {
        context.RegisterFactory<T>(category);
        return T.GetTypeInfoStatic();
    }

    // Returns a native object that carries one counted reference of the managed wrapper.
    // The wrapper becomes non-owning here. Its strong GC handle stays in the instance cache until the native object dies.
    // The native trampoline then releases the counted reference after the engine holds its own reference.
    [UnmanagedCallersOnly]
    private static nint CreateFromNative(nint typeInfo, nint context)
    {
        global::Urho3D.Core.Object? instance = null;
        try
        {
            if (!Creators.TryGetValue(typeInfo, out var creator))
            {
                return 0;
            }

            var contextWrapper = global::Urho3D.Core.Context.CreateManagedWrapper(context, ownsHandle: false);
            instance = creator(contextWrapper);
            return NativeObject.TransferHandleOwnership(instance);
        }
        catch
        {
            try
            {
                instance?.Dispose();
            }
            catch
            {
                // Nothing more to do. The native side receives null.
            }

            return 0;
        }
    }

    private static partial class __PInvoke
    {
        [LibraryImport(SharpCpp.Rbfx.Interop.NativeLibrary.Name, EntryPoint = "sharpcpp_rbfx_set_managed_factory_callback")]
        internal static partial void SetManagedFactoryCallback(nint callback);

        [LibraryImport(SharpCpp.Rbfx.Interop.NativeLibrary.Name, EntryPoint = "sharpcpp_rbfx_register_managed_factory", StringMarshalling = StringMarshalling.Utf8)]
        internal static partial void RegisterManagedFactory(nint context, nint typeInfo, string category);
    }
}
