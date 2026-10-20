using Urho3D.IO;
using Urho3D.Math;
using Xunit;

namespace SharpCpp.Rbfx.Tests.E2E;

/// <summary>
/// An eastl::span&lt;T&gt; parameter crosses the boundary as a Span&lt;T&gt; or a ReadOnlySpan&lt;T&gt;.
/// The managed memory is pinned for the call, and the native code reads it in place, with no copy.
/// </summary>
public sealed class SpanMarshallingTests
{
    [Fact]
    public void Memory_buffer_reads_the_bytes_of_pinned_memory_through_the_pointer_and_size_constructor()
    {
        // MemoryBuffer keeps the pointer after the constructor returns, so the array must not move.
        var bytes = GC.AllocateArray<byte>(4, pinned: true);
        bytes[0] = 10;
        bytes[1] = 20;
        bytes[2] = 30;
        bytes[3] = 40;

        using var buffer = new MemoryBuffer(global::System.Runtime.InteropServices.Marshal.UnsafeAddrOfPinnedArrayElement(bytes, 0), 4u);

        Assert.Equal(4u, buffer.Size);
        Assert.Equal((byte)10, buffer.ReadUByte());
        Assert.Equal((byte)20, buffer.ReadUByte());
        Assert.Equal(2u, buffer.Position);
    }

    [Fact]
    public void Memory_buffer_keeps_the_pointer_and_size_constructor_and_has_no_span_constructor()
    {
        // A span is pinned only for the call, and MemoryBuffer keeps the pointer after the call.
        Assert.NotNull(typeof(MemoryBuffer).GetConstructor(new[] { typeof(nint), typeof(uint) }));
        Assert.Null(typeof(MemoryBuffer).GetConstructor(new[] { typeof(Span<byte>) }));
        Assert.DoesNotContain(
            typeof(MemoryBuffer).GetConstructors(),
            constructor => constructor.GetParameters() is [{ Name: "data", ParameterType: var type }] && type == typeof(ReadOnlySpan<byte>));
    }

    [Fact]
    public void Tetrahedral_mesh_defines_tetrahedrons_from_a_span_of_vectors()
    {
        ReadOnlySpan<Vector3> positions =
        [
            new Vector3(0, 0, 0),
            new Vector3(1, 0, 0),
            new Vector3(0, 1, 0),
            new Vector3(0, 0, 1),
            new Vector3(0.2f, 0.2f, 0.2f),
        ];

        using var mesh = new TetrahedralMesh();
        mesh.Define(positions);

        Assert.True(mesh.NumInnerTetrahedrons > 0);
    }
}
