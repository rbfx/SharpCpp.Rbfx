using System.Runtime.CompilerServices;

using SharpCpp.Rbfx.Bindings.Interop;

using Urho3D.Actions;
using Urho3D.Core;
using Urho3D.Graphics;
using Urho3D.Math;
using Urho3D.Scene;
using Urho3D.Utility;

using Xunit;

namespace SharpCpp.Rbfx.Tests.E2E;

// Reference counts across the SharedPtr and raw pointer marshallers.
// A wrapper from a SharedPtr return or from a C# constructor owns one native reference.
// A wrapper from a raw pointer return is borrowed and owns no reference.
// The same native object always maps to the same wrapper.
public sealed class SharedPtrMarshallingTests
{
    [Fact]
    public void By_value_return_of_native_type_gives_the_wrapper_the_only_reference()
    {
        using var context = new Context();
        // A bare Context has no Node factory. The Engine registers it.
        using var engine = new Urho3D.Engine.Engine(context);
        var created = context.CreateObject(new StringHash("Node"));

        var node = Assert.IsType<Node>(created);
        Assert.Equal(1, node.Refs());
        Assert.Equal(0u, node.GetNumChildren());

        node.Dispose();
        Assert.True(node.IsClosed);
    }

    [Fact]
    public void By_value_return_of_managed_type_gives_the_wrapper_the_only_reference()
    {
        FactorySpinner.Reset();
        using var context = new Context();
        context.RegisterFactory<FactorySpinner>("E2E");
        var created = context.CreateObject(new StringHash("FactorySpinner"));

        var spinner = Assert.IsType<FactorySpinner>(created);
        Assert.Equal(1, spinner.Refs());
        Assert.Equal(1, FactorySpinner.Created);
        Assert.Equal(0, FactorySpinner.Disposed);

        // The release of the last reference destroys the native object.
        spinner.Dispose();
        Assert.True(spinner.IsClosed);
        Assert.Equal(1, FactorySpinner.Created);
        Assert.Equal(1, FactorySpinner.Disposed);
    }

    [Fact]
    public void By_value_return_of_shared_object_keeps_the_native_reference()
    {
        using var context = new Context();
        using var action = new DelayTime(context);
        Assert.Equal(1, action.Refs());

        using (var builder = new ActionBuilder(context))
        {
            // The argument is a const SharedPtr reference. The builder keeps its own reference.
            builder.Then(action);
            Assert.False(action.IsClosed);
            Assert.Equal(2, action.Refs());

            // Build returns a copy of the SharedPtr of the builder.
            var built = builder.Build();
            Assert.Same(action, built);
            Assert.Equal(2, action.Refs());
        }

        Assert.Equal(1, action.Refs());
    }

    [Fact]
    public void Shared_pointer_field_read_does_not_take_the_native_reference()
    {
        using var context = new Context();
        using var animation = new Animation(context);
        using var task = new RetargetAnimationTask();

        // The setter takes a SharedPtr by value. The field keeps a reference.
        task.SourceAnimation = animation;
        Assert.Equal(2, animation.Refs());

        Assert.Same(animation, task.SourceAnimation);
        Assert.Same(animation, task.SourceAnimation);
        Assert.Equal(2, animation.Refs());

        task.SourceAnimation = null!;
        Assert.Equal(1, animation.Refs());
        var empty = task.SourceAnimation;
        Assert.True(empty is null || empty.IsInvalid);
    }

    [Fact]
    public void Raw_pointer_returns_give_borrowed_wrappers()
    {
        using var context = new Context();
        using var engine = new Urho3D.Engine.Engine(context);
        using var scene = new Scene(context);
        using var child = scene.CreateChild("child")!;
        using var path = child.CreateComponent<SplinePath>()!;

        // Only the parent holds a reference.
        Assert.Equal(1, child.Refs());
        Assert.Equal(1, path.Refs());

        Assert.Same(child, scene.GetChild(new StringHash("child")));
        Assert.Same(child, scene.GetChild(0u));
        Assert.Same(path, child.GetComponent(SplinePath.GetTypeStatic()));
        Assert.Same(scene, child.Parent);
        Assert.Equal(1, child.Refs());
        Assert.Equal(1, path.Refs());
    }

    [Fact]
    public void Raw_pointer_arguments_keep_the_wrapper_valid()
    {
        using var context = new Context();
        using var scene = new Scene(context);
        using var child = new Node(context);
        using var path = new SplinePath(context);
        Assert.Equal(1, child.Refs());
        Assert.Equal(1, path.Refs());

        scene.AddChild(child);
        child.AddComponent(path, 0);
        Assert.Equal(2, child.Refs());
        Assert.Equal(2, path.Refs());
        Assert.Same(child, scene.GetChild(0u));
        Assert.Same(path, child.GetComponent(SplinePath.GetTypeStatic()));

        child.RemoveComponent(path);
        scene.RemoveChild(child);
        Assert.False(child.IsClosed);
        Assert.False(path.IsClosed);
        Assert.Equal(1, child.Refs());
        Assert.Equal(1, path.Refs());
        Assert.Equal(0u, scene.GetNumChildren());
    }

    [Fact]
    public void Wrapper_and_native_owner_release_in_any_order()
    {
        for (var i = 0; i < 3; i++)
        {
            using var context = new Context();
            ReleaseWrapperFirst(context);
            ForceGc();
            ReleaseOwnerFirst(context);
            ForceGc();
            ReleaseOwnerBeforeBorrowedWrapper(context);
            ForceGc();
            ReleaseFieldOwnerFirst(context);
            ForceGc();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ReleaseWrapperFirst(Context context)
    {
        var scene = new Scene(context);
        var child = scene.CreateChild("child")!;
        child.Dispose();
        Assert.Equal(1u, scene.GetNumChildren());
        scene.Dispose();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ReleaseOwnerFirst(Context context)
    {
        var scene = new Scene(context);
        var child = new Node(context);
        scene.AddChild(child);
        Assert.Equal(2, child.Refs());

        // The owning wrapper keeps the node alive after the scene is gone.
        scene.Dispose();
        Assert.False(child.IsClosed);
        Assert.Equal(1, child.Refs());
        Assert.True(child.Parent is null || child.Parent.IsInvalid);
        Assert.Equal(0u, child.GetNumChildren());
        child.Dispose();
    }

    // A borrowed wrapper must not touch the native object when it is finalized after the object is deleted.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ReleaseOwnerBeforeBorrowedWrapper(Context context)
    {
        var scene = new Scene(context);
        var child = scene.CreateChild("child")!;
        Assert.Equal(1, child.Refs());
        scene.Dispose();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ReleaseFieldOwnerFirst(Context context)
    {
        var animation = new Animation(context);
        var task = new RetargetAnimationTask();
        task.SourceAnimation = animation;
        Assert.Same(animation, task.SourceAnimation);

        task.Dispose();
        Assert.Equal(1, animation.Refs());
        animation.Dispose();
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
