using System.Runtime.CompilerServices;

using SharpCpp.Rbfx.Bindings.Interop;

using Urho3D.Core;
using Urho3D.Math;
using Urho3D.Plugins;
using Urho3D.Scene;

using Xunit;

namespace SharpCpp.Rbfx.Tests.E2E;

public sealed class ManagedObjectFactoryTests
{
    [Fact]
    public void Managed_type_is_created_by_type_name_hash_and_released_once()
    {
        FactorySpinner.Reset();
        var weak = CreateAndRemove(out var context, out var scene);

        using (context)
        using (scene)
        {
            Assert.Equal(1, FactorySpinner.Created);
            ForceGc();

            Assert.False(weak.TryGetTarget(out _), "The managed instance stays alive after the native object is gone.");
            Assert.Equal(1, FactorySpinner.Created);
            Assert.Equal(1, FactorySpinner.Disposed);
        }
    }

    [Fact]
    public void Managed_type_can_be_created_by_name_twice()
    {
        FactorySpinner.Reset();
        using var context = new Context();
        using var scene = new Scene(context);
        context.RegisterFactory<FactorySpinner>("E2E");

        var hash = new StringHash("FactorySpinner");
        using var node = scene.CreateChild("holder")!;
        var first = node.CreateComponent(hash);
        var second = node.CreateComponent(hash);

        Assert.IsType<FactorySpinner>(first);
        Assert.IsType<FactorySpinner>(second);
        Assert.NotSame(first, second);
        Assert.Equal(2, FactorySpinner.Created);

        ForceGc();
        Assert.Same(first, node.GetComponent(hash));

        node.Remove();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<FactorySpinner> CreateAndRemove(out Context context, out Scene scene)
    {
        context = new Context();
        scene = new Scene(context);
        context.RegisterFactory<FactorySpinner>("E2E");

        var node = scene.CreateChild("holder")!;
        var component = node.CreateComponent(new StringHash("FactorySpinner"));
        var spinner = Assert.IsType<FactorySpinner>(component);
        var weak = new WeakReference<FactorySpinner>(spinner);

        Assert.True(spinner.IsInstanceOf(LogicComponent.GetTypeStatic()));
        Assert.Same(spinner, node.GetComponent(new StringHash("FactorySpinner")));

        node.Remove();
        Assert.Equal(1, FactorySpinner.Disposed);
        Assert.True(spinner.IsClosed);
        node.Dispose();
        return weak;
    }

    [Fact]
    public void Constructed_component_given_to_native_owner_is_released_with_it()
    {
        FactorySpinner.Reset();
        var weak = ConstructAddAndRemove(out var context, out var scene);

        using (context)
        using (scene)
        {
            ForceGc();

            Assert.False(weak.TryGetTarget(out _), "The managed instance stays alive after the native object is gone.");
            Assert.Equal(1, FactorySpinner.Created);
            Assert.Equal(1, FactorySpinner.Disposed);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<FactorySpinner> ConstructAddAndRemove(out Context context, out Scene scene)
    {
        context = new Context();
        scene = new Scene(context);

        var node = scene.CreateChild("holder")!;
        var spinner = new FactorySpinner(context);
        node.AddComponent(spinner, 0);
        spinner.TransferOwnershipToNative();
        var weak = new WeakReference<FactorySpinner>(spinner);
        spinner = null;

        // The native node keeps the managed state alive. A lookup returns the same instance after a collection.
        ForceGc();
        var found = Assert.IsType<FactorySpinner>(node.GetComponent(new StringHash("FactorySpinner")));
        Assert.True(weak.TryGetTarget(out var target));
        Assert.Same(target, found);
        target = null;
        found = null;
        Assert.Equal(0, FactorySpinner.Disposed);

        node.Remove();
        Assert.Equal(1, FactorySpinner.Disposed);
        node.Dispose();
        return weak;
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

[FactoryReflection]
public partial class FactorySpinner : LogicComponent
{
    private static int _created;
    private static int _disposed;

    public FactorySpinner(Context context)
        : base(context)
    {
        Interlocked.Increment(ref _created);
    }

    public static int Created => Volatile.Read(ref _created);

    public static int Disposed => Volatile.Read(ref _disposed);

    public static void Reset()
    {
        Volatile.Write(ref _created, 0);
        Volatile.Write(ref _disposed, 0);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Interlocked.Increment(ref _disposed);
        }

        base.Dispose(disposing);
    }
}
