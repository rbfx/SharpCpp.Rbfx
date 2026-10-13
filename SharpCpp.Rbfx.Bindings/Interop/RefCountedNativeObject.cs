using SharpCpp.Support;

namespace Urho3D.Interop;

public abstract class RefCountedNativeObject : NativeObject
{
    protected RefCountedNativeObject(nint nativeHandle, bool ownsHandle)
        : base(nativeHandle, ownsHandle)
    {
    }

    protected override INativeInstanceCache? InstanceCache => RefCountedInstanceCache.Shared;

    protected override bool ManagesInstanceCacheLifecycle => true;

    // The native owning return marshallers detach the pointer without a counted reference.
    // Each owning wrapper takes its own reference in AcquireOwningHandleCore.
    public override bool OwningReturnHandsOverReference => false;

    private bool _finalizingBorrowedWrapper;

    // A non-owning wrapper can be finalized after its native object is deleted, for example when the native object
    // dies while the finalizer is queued. The finalizer then must not read the native object. The cache handle
    // that stays behind has no target, and the next lookup, registration or native disposal frees it.
    internal bool SkipsNativeUnregister => _finalizingBorrowedWrapper;

    protected override void Dispose(bool disposing)
    {
        if (!disposing && !OwnsNativeHandle)
        {
            _finalizingBorrowedWrapper = true;
        }

        base.Dispose(disposing);
    }

    protected sealed override bool ReleaseNativeHandle(nint handle)
    {
        InstanceCache?.Unregister(this);

        if (OwnsNativeHandle)
        {
            RefCountedRuntime.ReleaseRef(handle);
        }

        return true;
    }

    protected sealed override void AcquireOwningHandleCore()
    {
        RefCountedRuntime.AddRef(DangerousGetHandle());
    }

    /// <summary>
    /// Gives the managed reference of this owning wrapper to the native object. The wrapper becomes non-owning.
    /// The native object then keeps the wrapper alive and disposes it when the last native reference is released.
    /// Use this after native code takes its own reference, for example after <c>Node.AddComponent</c>.
    /// </summary>
    public void TransferOwnershipToNative()
    {
        var handle = TransferHandleOwnership(this);
        RefCountedRuntime.ReleaseRef(handle);
    }

    // The engine calls this through the ScriptRuntimeApi when the last native reference is released. The native object
    // is still intact. As in the legacy bindings, the wrapper runs its Dispose(true) cleanup once.
    internal void DisposeFromNativeRuntime()
    {
        if (IsClosed || IsInvalid)
        {
            return;
        }

        // The refcount is already 0. An owning wrapper must not release a reference again.
        if (OwnsNativeHandle)
        {
            TransferHandleOwnership(this);
        }

        Dispose();
    }

    // The native object is already deleted. The handle closes first, so the wrapper does not read the freed object.
    internal void DetachFromDestroyedNativeObject()
    {
        SetHandleAsInvalid();
        DisposeFromNativeRuntimeCore();
    }
}
