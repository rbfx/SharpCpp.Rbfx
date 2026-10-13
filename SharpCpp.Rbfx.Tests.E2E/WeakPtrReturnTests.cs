using System.Runtime.CompilerServices;

using Urho3D.Core;
using Urho3D.Engine;
using Urho3D.Scene;

using Xunit;

namespace SharpCpp.Rbfx.Tests.E2E;

public sealed class WeakPtrReturnTests
{
    [Fact]
    public void Weak_ptr_field_getter_returns_a_correctly_counted_reference()
    {
        using var context = new Context();
        using var app = new ContextReadingApplication(context);

        for (var index = 0; index < 8; ++index)
        {
            var first = app.ReadContext();
            var second = app.ReadContext();
            Assert.True(first is not null);
            Assert.True(ReferenceEquals(first, second));
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        using var node = new Node(context);
        Assert.True(node is not null);
        Assert.True(ReferenceEquals(context, app.ReadContext()));

        for (var index = 0; index < 8; ++index)
        {
            _ = app.ReadContext();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        using var otherNode = new Node(context);
        otherNode.Name = "after-gc";
        Assert.Equal("after-gc", otherNode.Name);
    }
}

public sealed class WeakPtrFieldTests
{
    // A read of the protected WeakPtr<Context> field gives a borrowed wrapper.
    // An owning wrapper would keep the Context alive and block the engine shutdown.
    [Fact]
    public void Weak_ptr_field_read_does_not_keep_the_context_alive()
    {
        var context = CreateNativeOwnedContext();
        Assert.False(context.OwnsNativeHandle);
        Assert.Equal(1, context.Refs());

        ReadContextThroughComponent(context);
        ForceGc();

        Assert.False(context.OwnsNativeHandle);
        Assert.Equal(1, context.Refs());

        // Release the native reference. The Context is deleted.
        context.ReleaseRef();
        Assert.True(context.IsClosed || context.IsInvalid);
    }

    // The wrapper of the returned Context is borrowed. The extra native reference keeps the Context alive.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Context CreateNativeOwnedContext()
    {
        var context = new Context();
        context.AddRef();
        context.TransferOwnershipToNative();
        return context;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ReadContextThroughComponent(Context context)
    {
        using var scene = new Scene(context);
        using var node = scene.CreateChild("holder")!;
        var reader = new ContextFieldReader(context);
        node.AddComponent(reader, 0);
        reader.TransferOwnershipToNative();

        var before = context.Refs();
        for (var index = 0; index < 4; ++index)
        {
            Assert.Same(context, reader.ReadContext());
        }

        Assert.False(context.OwnsNativeHandle);
        Assert.Equal(before, context.Refs());

        node.Remove();
        scene.Dispose();
    }

    private static void ForceGc()
    {
        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }
}

public partial class ContextFieldReader : LogicComponent
{
    public ContextFieldReader(Context context)
        : base(context)
    {
    }

    // Context is the protected context_ field of Object.
    public Context ReadContext() => Context;
}

public partial class ContextReadingApplication : Application
{
    public ContextReadingApplication(Context context)
        : base(context)
    {
    }

    public Context ReadContext() => Context;
}
