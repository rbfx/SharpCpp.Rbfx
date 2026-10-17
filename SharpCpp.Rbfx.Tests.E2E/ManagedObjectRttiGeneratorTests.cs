using Urho3D.Core;
using Urho3D.Engine;
using Urho3D.Scene;

using Xunit;

namespace SharpCpp.Rbfx.Tests.E2E;

public sealed class ManagedObjectRttiGeneratorTests
{
    [Fact]
    public void Managed_object_subclasses_receive_most_derived_rtti_surface()
    {
        using var context = new Context();
        using var app = new GeneratedManagedApplication(context);
        var generatedTypeInfo = GeneratedManagedApplication.GetTypeInfoStatic();

        Assert.Equal(GeneratedManagedApplication.GetTypeStatic(), app.GetType());
        Assert.Equal(new Urho3D.Math.StringHash(nameof(GeneratedManagedApplication)), GeneratedManagedApplication.GetTypeStatic());
        Assert.Same(generatedTypeInfo, app.GetTypeInfo());
        Assert.True(generatedTypeInfo.IsTypeOf(Application.GetTypeStatic()));
        Assert.True(app.IsInstanceOf(GeneratedManagedApplication.GetTypeStatic()));
        Assert.True(app.IsInstanceOf(Application.GetTypeStatic()));
    }

    [Fact]
    public void Native_code_sees_the_generated_rtti_of_a_type_without_own_overrides()
    {
        // The type declares no override, so only the RTTI generator overrides the native virtual methods.
        // Node::GetComponent calls the native GetType, which must reach the generated override.
        using var context = new Context();
        using var node = new Node(context);
        using var component = new GeneratedRttiOnlyComponent(context);
        node.AddComponent(component, 0);

        using var found = node.GetComponent(GeneratedRttiOnlyComponent.GetTypeStatic());
        Assert.NotNull(found);
        Assert.Equal(component.DangerousGetHandle(), found.DangerousGetHandle());
        Assert.Null(node.GetComponent(Component.GetTypeStatic()));
    }
}

public partial class GeneratedRttiOnlyComponent : Component
{
    public GeneratedRttiOnlyComponent(Context context)
        : base(context)
    {
    }
}

public partial class GeneratedManagedApplication : Application
{
    public GeneratedManagedApplication(Context context)
        : base(context)
    {
    }
}
