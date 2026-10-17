using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using SharpCpp.Support;

namespace Urho3D.Core;

public partial class WorkQueue
{
    private static unsafe readonly nint s_managedTaskCallbackPointer =
        (nint)(delegate* unmanaged<nint, uint, nint, void>)&DispatchManagedTask;
    private static unsafe readonly nint s_managedTaskReleasePointer =
        (nint)(delegate* unmanaged<nint, void>)&ReleaseManagedTask;

    public void PostTaskForMainThread(Action<uint, WorkQueue> taskFunction, TaskPriority priority = TaskPriority.Medium)
    {
        ArgumentNullException.ThrowIfNull(taskFunction);

        var callbackHandle = GCHandle.ToIntPtr(GCHandle.Alloc(taskFunction));
        __WorkQueuePInvoke.PostTaskForMainThread(
            NativeObject.GetHandle(this),
            s_managedTaskCallbackPointer,
            s_managedTaskReleasePointer,
            callbackHandle,
            priority);
    }

    public void PostDelayedTaskForMainThread(Action<uint, WorkQueue> taskFunction)
    {
        ArgumentNullException.ThrowIfNull(taskFunction);

        var callbackHandle = GCHandle.ToIntPtr(GCHandle.Alloc(taskFunction));
        __WorkQueuePInvoke.PostDelayedTaskForMainThread(
            NativeObject.GetHandle(this),
            s_managedTaskCallbackPointer,
            s_managedTaskReleasePointer,
            callbackHandle);
    }

    [UnmanagedCallersOnly]
    private static void DispatchManagedTask(nint callbackHandle, uint threadIndex, nint workQueue)
    {
        var taskFunction = (Action<uint, WorkQueue>)GCHandle.FromIntPtr(callbackHandle).Target!;
        taskFunction(threadIndex, CreateManagedWrapper(workQueue, ownsHandle: false));
    }

    [UnmanagedCallersOnly]
    private static void ReleaseManagedTask(nint callbackHandle)
    {
        if (callbackHandle == IntPtr.Zero)
        {
            return;
        }

        var handle = GCHandle.FromIntPtr(callbackHandle);
        if (handle.IsAllocated)
        {
            handle.Free();
        }
    }
}

internal static partial class __WorkQueuePInvoke
{
    [LibraryImport(SharpCpp.Rbfx.Interop.NativeLibrary.Name, EntryPoint = "SharpCpp_Rbfx_WorkQueue_PostTaskForMainThread")]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial void PostTaskForMainThread(nint receiver, nint callback, nint release, nint callbackHandle, TaskPriority priority);

    [LibraryImport(SharpCpp.Rbfx.Interop.NativeLibrary.Name, EntryPoint = "SharpCpp_Rbfx_WorkQueue_PostDelayedTaskForMainThread")]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static partial void PostDelayedTaskForMainThread(nint receiver, nint callback, nint release, nint callbackHandle);
}
