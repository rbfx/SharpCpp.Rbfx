using Urho3D.Math;
using Urho3D.Scene;
using Xunit;

namespace SharpCpp.Rbfx.Tests.E2E;

public sealed class NodeTransformTests
{
    [Fact]
    public void Node_position_roundtrips()
    {
        using var context = new global::Urho3D.Core.Context();
        using var node = new Node(context);

        node.Position = new Vector3(1.5f, -2.25f, 3.75f);
        var value = node.Position;

        Assert.Equal(1.5f, value.X);
        Assert.Equal(-2.25f, value.Y);
        Assert.Equal(3.75f, value.Z);
    }

    [Fact]
    public void Node_scale_roundtrips()
    {
        using var context = new global::Urho3D.Core.Context();
        using var node = new Node(context);

        node.SetScale(new Vector3(2f, 3f, 4f));
        var value = node.GetScale();

        Assert.Equal(2f, value.X);
        Assert.Equal(3f, value.Y);
        Assert.Equal(4f, value.Z);
    }

    [Fact]
    public void Node_rotation_roundtrips()
    {
        using var context = new global::Urho3D.Core.Context();
        using var node = new Node(context);

        node.Rotation = new Quaternion(0f, 1f, 0f, 0f);
        var value = node.Rotation;

        Assert.Equal(0f, value.W, 4);
        Assert.Equal(1f, value.X, 4);
        Assert.Equal(0f, value.Y, 4);
        Assert.Equal(0f, value.Z, 4);
    }

    [Fact]
    public void Node_rotation_roundtrips_generic_quaternion()
    {
        using var context = new global::Urho3D.Core.Context();
        using var node = new Node(context);
        var expected = new Quaternion(0.5f, 0.5f, 0.5f, 0.5f);

        node.Rotation = expected;
        var value = node.Rotation;

        Assert.Equal(expected.W, value.W, 4);
        Assert.Equal(expected.X, value.X, 4);
        Assert.Equal(expected.Y, value.Y, 4);
        Assert.Equal(expected.Z, value.Z, 4);
    }
}
