using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using SharpCpp.Support;

namespace Urho3D.Interop;

[StructLayout(LayoutKind.Sequential)]
public readonly struct SharedPtrAbi
{
    public SharedPtrAbi(nint pointer, nint sharedPtr)
    {
        Pointer = pointer;
        SharedPtr = sharedPtr;
    }

    public nint Pointer { get; }

    public nint SharedPtr { get; }
}

/// <summary>A native shared pointer that holds an interface and an external RefCounted owner.</summary>
/// <typeparam name="T">The native interface type.</typeparam>
/// <typeparam name="TRefCounted">The native reference-counted lifetime owner type.</typeparam>
public sealed class SharedPtr<T, TRefCounted> : SafeHandle
    where T : NativeObject, INativeObjectRegistration<T>
    where TRefCounted : NativeObject, INativeObjectRegistration<TRefCounted>
{
    private readonly nint _pointer;

    internal SharedPtr(nint pointer, nint sharedPtr)
        : base(IntPtr.Zero, ownsHandle: true)
    {
        _pointer = pointer;
        SetHandle(sharedPtr);
    }

    /// <inheritdoc />
    public override bool IsInvalid => _pointer == IntPtr.Zero || handle == IntPtr.Zero;

    /// <summary>Returns a non-owning wrapper for the pointed-to interface.</summary>
    public T Get()
    {
        var success = false;
        DangerousAddRef(ref success);
        try
        {
            ThrowIfDisposed();
            return (T)NativeObjectRegistry.Create<T>(_pointer, ownsHandle: false);
        }
        finally
        {
            if (success)
            {
                DangerousRelease();
            }
        }
    }

    internal SharedPtrAbi ToAbi()
    {
        return new SharedPtrAbi(_pointer, handle);
    }

    internal static SharedPtr<T, TRefCounted>? FromAbi(SharedPtrAbi value)
    {
        if (value.Pointer == IntPtr.Zero || value.SharedPtr == IntPtr.Zero)
        {
            return null;
        }

        // The native return marshaller transfers ownership of the native SharedPtr holder.
        return new SharedPtr<T, TRefCounted>(value.Pointer, value.SharedPtr);
    }

    /// <inheritdoc />
    protected override bool ReleaseHandle()
    {
        if (handle != IntPtr.Zero)
        {
            RefCountedRuntime.ReleaseExternalSharedPtr(handle);
            handle = IntPtr.Zero;
        }

        return true;
    }

    private void ThrowIfDisposed()
    {
        if (IsClosed || IsInvalid)
        {
            throw new ObjectDisposedException(nameof(SharedPtr<T, TRefCounted>));
        }
    }
}

[CustomMarshaller(typeof(SharedPtr<,>), MarshalMode.ManagedToUnmanagedIn, typeof(SharedPtrAbiInMarshaller<,>))]
[CustomMarshaller(typeof(SharedPtr<,>), MarshalMode.ManagedToUnmanagedOut, typeof(SharedPtrAbiOutMarshaller<,>))]
public static class SharedPtrAbiMarshaller<T, TRefCounted>
    where T : NativeObject, INativeObjectRegistration<T>
    where TRefCounted : NativeObject, INativeObjectRegistration<TRefCounted>
{
}

public ref struct SharedPtrAbiInMarshaller<T, TRefCounted>
    where T : NativeObject, INativeObjectRegistration<T>
    where TRefCounted : NativeObject, INativeObjectRegistration<TRefCounted>
{
    private SharedPtr<T, TRefCounted>? _managed;
    private bool _dangerousReferenceAdded;

    public void FromManaged(SharedPtr<T, TRefCounted>? managed)
    {
        _managed = managed;
        if (managed is not null)
        {
            var success = false;
            managed.DangerousAddRef(ref success);
            _dangerousReferenceAdded = success;
        }
    }

    public SharedPtrAbi ToUnmanaged()
        => _managed is null ? default : _managed.ToAbi();

    public void Free()
    {
        if (_dangerousReferenceAdded)
        {
            _managed!.DangerousRelease();
            _dangerousReferenceAdded = false;
        }

        _managed = null;
    }
}

public static class SharedPtrAbiOutMarshaller<T, TRefCounted>
    where T : NativeObject, INativeObjectRegistration<T>
    where TRefCounted : NativeObject, INativeObjectRegistration<TRefCounted>
{
    public static SharedPtr<T, TRefCounted>? ConvertToManaged(SharedPtrAbi unmanaged)
        => SharedPtr<T, TRefCounted>.FromAbi(unmanaged);
}
