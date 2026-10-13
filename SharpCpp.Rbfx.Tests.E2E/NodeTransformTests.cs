using Urho3D.Graphics;
using Urho3D.Math;
using Urho3D.Navigation;
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

    [Fact]
    public void Node_world_rotation_returns_quaternion_by_value()
    {
        using var context = new global::Urho3D.Core.Context();
        using var parent = new Node(context);
        var rotation = new Quaternion(0.5f, 0.5f, 0.5f, 0.5f);
        parent.Rotation = rotation;
        var child = parent.CreateChild("child");
        Assert.NotNull(child);

        for (var index = 0; index < 100; index++)
        {
            var value = child.WorldRotation;
            Assert.Equal(rotation.W, value.W, 4);
            Assert.Equal(rotation.X, value.X, 4);
            Assert.Equal(rotation.Y, value.Y, 4);
            Assert.Equal(rotation.Z, value.Z, 4);
        }
    }

    [Fact]
    public void Camera_gpu_projection_returns_matrix4_by_value()
    {
        using var context = new global::Urho3D.Core.Context();
        using var camera = new Camera(context);

        var projection = camera.GetProjection();
        var gpu = camera.GetGPUProjection();

        // A perspective projection has distinct nonzero entries, so a wrong layout shows up.
        Assert.NotEqual(0f, gpu.M00);
        Assert.NotEqual(0f, gpu.M11);
        Assert.NotEqual(0f, gpu.M22);
        Assert.NotEqual(0f, gpu.M23);
        Assert.NotEqual(0f, gpu.M32);
        Assert.NotEqual(gpu.M00, gpu.M22);
        Assert.Equal(1f, gpu.M32, 4);
        Assert.Equal(0f, gpu.M33, 4);
        Assert.Equal(0f, gpu.M01, 4);
        Assert.Equal(0f, gpu.M10, 4);
        Assert.Equal(projection.M00, gpu.M00, 4);
        Assert.Equal(projection.M01, gpu.M01, 4);
        Assert.Equal(projection.M02, gpu.M02, 4);
        Assert.Equal(projection.M03, gpu.M03, 4);
        Assert.Equal(projection.M10, gpu.M10, 4);
        Assert.Equal(projection.M11, gpu.M11, 4);
        Assert.Equal(projection.M12, gpu.M12, 4);
        Assert.Equal(projection.M13, gpu.M13, 4);
        Assert.Equal(projection.M20, gpu.M20, 4);
        Assert.Equal(projection.M21, gpu.M21, 4);
        Assert.Equal(projection.M22, gpu.M22, 4);
        Assert.Equal(projection.M23, gpu.M23, 4);
        Assert.Equal(projection.M30, gpu.M30, 4);
        Assert.Equal(projection.M31, gpu.M31, 4);
        Assert.Equal(projection.M32, gpu.M32, 4);
        Assert.Equal(projection.M33, gpu.M33, 4);
    }

    [Fact]
    public void NavArea_bounding_box_roundtrips()
    {
        using var context = new global::Urho3D.Core.Context();
        using var area = new NavArea(context);
        var expected = new BoundingBox(new Vector3(-1f, -2f, -3f), new Vector3(4f, 5f, 6f));

        area.BoundingBox = expected;
        var value = area.BoundingBox;

        Assert.Equal(-1f, value.Min.X, 4);
        Assert.Equal(-2f, value.Min.Y, 4);
        Assert.Equal(-3f, value.Min.Z, 4);
        Assert.Equal(4f, value.Max.X, 4);
        Assert.Equal(5f, value.Max.Y, 4);
        Assert.Equal(6f, value.Max.Z, 4);
    }
}
