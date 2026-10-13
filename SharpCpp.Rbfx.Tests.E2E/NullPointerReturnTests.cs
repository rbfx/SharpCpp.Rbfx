using Urho3D.Core;
using Urho3D.Engine;
using Urho3D.Plugins;
using Urho3D.Scene;

using Xunit;

namespace SharpCpp.Rbfx.Tests.E2E;

// A null native pointer is a null managed reference. No wrapper with handle 0 exists.
public sealed class NullPointerReturnTests
{
    [Fact]
    public void Polymorphic_null_pointer_return_is_null()
    {
        using var context = new Context();
        using var scene = new Scene(context);
        using var node = scene.CreateChild("holder")!;
        using var component = new ContextFieldReader(context);

        node.AddComponent(component, 0);
        Assert.Same(node, component.GetNode());

        node.RemoveComponent(component);
        Assert.Null(component.GetNode());
    }

    [Fact]
    public void Null_plugin_pointer_return_is_null()
    {
        using var context = new Context();
        using var engine = new Engine(context);
        using var manager = new PluginManager(context);
        context.RegisterSubsystem(manager);

        Assert.Null(manager.GetPlugin(new Ea.String("MissingPlugin"), true));
        Assert.Null(manager.GetDynamicPlugin(new Ea.String("MissingPlugin"), true));

        context.RemoveSubsystem(PluginManager.GetTypeStatic());
    }
}
