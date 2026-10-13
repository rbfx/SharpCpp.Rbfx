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

    internal void DisposeFromNativeRuntime()
    {
        DisposeFromNativeRuntimeCore();
    }
}
