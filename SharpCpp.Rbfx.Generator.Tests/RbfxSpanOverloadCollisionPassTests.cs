using SharpCpp.Model.Ast;
using Xunit;

namespace SharpCpp.Rbfx.Generator.Tests;

/// <summary>Tests of the exclusion of overloads that a span overload replaces or collides with.</summary>
public sealed class RbfxSpanOverloadCollisionPassTests
{
    private static readonly BindingType Vector3 = BindingType.CreateHandle(
        "Urho3D::Vector3", "global::Urho3D.Vector3", recordId: "record:Urho3D::Vector3");

    private static readonly BindingType ByteSpan = BindingType.CreateConfigured(
        "eastl::span<const unsigned char>",
        "global::System.ReadOnlySpan<{0}>",
        marshallerTypeName: "SharpCpp::Marshaller<eastl::span<const unsigned char>>",
        managedMarshaller: "global::SharpCpp.Support.ReadOnlySpanAbiMarshaller<>",
        genericArguments: new[] { BindingType.UInt8 });

    private static readonly BindingType StringView = BindingType.CreateConfigured(
        "eastl::string_view",
        "global::System.ReadOnlySpan<byte>",
        marshallerTypeName: "SharpCpp::Marshaller<eastl::string_view>",
        managedMarshaller: "global::SharpCpp.Support.Utf8SpanAbiMarshaller");

    private static readonly BindingType Vector3Span = BindingType.CreateConfigured(
        "eastl::span<const Urho3D::Vector3>",
        "global::System.ReadOnlySpan<{0}>",
        marshallerTypeName: "SharpCpp::Marshaller<eastl::span<const Urho3D::Vector3>>",
        managedMarshaller: "global::SharpCpp.Support.ReadOnlySpanAbiMarshaller<>",
        genericArguments: new[] { Vector3 });

    private static readonly BindingType VoidPointer = Pointer("const void *");

    private static readonly BindingType MutableVoidPointer = Pointer("void *");

    private static readonly BindingType Vector3Pointer = Pointer("const Urho3D::Vector3 *");

    private static BindingType Pointer(string cppTypeName) => BindingType.CreateConfigured(
        cppTypeName, "global::System.IntPtr", null, null, BindingPrimitiveKind.IntPtr);

    [Fact]
    public void Apply_ExcludesPointerSizeOverload_WhenSpanOverloadExists()
    {
        var model = new FlagSetTestModel();
        var record = model.AddRecord("MemoryBuffer");
        var span = model.AddFunction(record, "SetData", BindingType.Void, ByteSpan);
        var pointerSize = model.AddFunction(record, "SetData", BindingType.Void, VoidPointer, BindingType.UInt32);
        MakeOverloads(span, pointerSize);

        var count = RbfxSpanOverloadCollisionPass.Apply(model.Compilation);

        Assert.Equal(1, count);
        Assert.False(span.IsIgnored());
        Assert.True(pointerSize.IsSilentlyExcluded());
        Assert.Equal(RbfxSpanOverloadCollisionPass.PointerSizeReason, pointerSize.GetIgnoreReason());
    }

    [Fact]
    public void Apply_ExcludesPointerSizeOverload_WithLeadingAndTrailingParameters()
    {
        var model = new FlagSetTestModel();
        var record = model.AddRecord("Mesh");
        var span = model.AddFunction(record, "Define", BindingType.Void, BindingType.Int32, Vector3Span, BindingType.Boolean);
        var pointerSize = model.AddFunction(
            record, "Define", BindingType.Void, BindingType.Int32, Vector3Pointer, BindingType.NativeUInt, BindingType.Boolean);
        MakeOverloads(span, pointerSize);

        Assert.Equal(1, RbfxSpanOverloadCollisionPass.Apply(model.Compilation));
        Assert.True(pointerSize.IsSilentlyExcluded());
        Assert.False(span.IsIgnored());
    }

    [Fact]
    public void Apply_KeepsPointerOverload_WhenNoSizeFollowsThePointer()
    {
        var model = new FlagSetTestModel();
        var record = model.AddRecord("Mesh");
        var span = model.AddFunction(record, "Define", BindingType.Void, Vector3Span);
        var pointerFlag = model.AddFunction(record, "Define", BindingType.Void, Vector3Pointer, BindingType.Boolean);
        MakeOverloads(span, pointerFlag);

        Assert.Equal(0, RbfxSpanOverloadCollisionPass.Apply(model.Compilation));
        Assert.False(pointerFlag.IsIgnored());
        Assert.False(span.IsIgnored());
    }

    [Fact]
    public void Apply_ExcludesSpanOverload_WhenStringViewOverloadHasTheSameManagedSignature()
    {
        var model = new FlagSetTestModel();
        var record = model.AddRecord("Serializer");
        var span = model.AddFunction(record, "Write", BindingType.Void, ByteSpan);
        var stringView = model.AddFunction(record, "Write", BindingType.Void, StringView);
        MakeOverloads(span, stringView);

        var count = RbfxSpanOverloadCollisionPass.Apply(model.Compilation);

        Assert.Equal(1, count);
        Assert.True(span.IsSilentlyExcluded());
        Assert.Equal(RbfxSpanOverloadCollisionPass.ManagedSignatureReason, span.GetIgnoreReason());
        Assert.False(stringView.IsIgnored());
    }

    [Fact]
    public void Apply_KeepsBothOverloads_WhenManagedSignaturesDiffer()
    {
        var model = new FlagSetTestModel();
        var record = model.AddRecord("Mesh");
        var span = model.AddFunction(record, "Define", BindingType.Void, Vector3Span);
        var stringView = model.AddFunction(record, "Define", BindingType.Void, StringView);
        MakeOverloads(span, stringView);

        Assert.Equal(0, RbfxSpanOverloadCollisionPass.Apply(model.Compilation));
        Assert.False(span.IsIgnored());
        Assert.False(stringView.IsIgnored());
    }

    [Fact]
    public void Apply_IgnoresOverloadsInOtherScopes()
    {
        var model = new FlagSetTestModel();
        var first = model.AddRecord("First");
        var second = model.AddRecord("Second");
        var span = model.AddFunction(first, "Write", BindingType.Void, ByteSpan);
        var stringView = model.AddFunction(second, "Write", BindingType.Void, StringView);
        span.CppQualifiedName = "Urho3D::First::Write";
        stringView.CppQualifiedName = "Urho3D::Second::Write";

        Assert.Equal(0, RbfxSpanOverloadCollisionPass.Apply(model.Compilation));
        Assert.False(span.IsIgnored());
        Assert.False(stringView.IsIgnored());
    }

    [Fact]
    public void Apply_KeepsPointerSizeOverload_WhenPointerConstnessDiffers()
    {
        var model = new FlagSetTestModel();
        var record = model.AddRecord("Buffer");
        var span = model.AddFunction(record, "SetData", BindingType.Void, ByteSpan);
        var pointerSize = model.AddFunction(record, "SetData", BindingType.Void, MutableVoidPointer, BindingType.UInt32);
        MakeOverloads(span, pointerSize);

        Assert.Equal(0, RbfxSpanOverloadCollisionPass.Apply(model.Compilation));
        Assert.False(pointerSize.IsIgnored());
        Assert.False(span.IsIgnored());
    }

    [Fact]
    public void Apply_KeepsPointerSizeOverload_WhenElementTypeDiffers()
    {
        var model = new FlagSetTestModel();
        var record = model.AddRecord("Mesh");
        var span = model.AddFunction(record, "Define", BindingType.Void, Vector3Span);
        var pointerSize = model.AddFunction(record, "Define", BindingType.Void, VoidPointer, BindingType.UInt32);
        MakeOverloads(span, pointerSize);

        Assert.Equal(0, RbfxSpanOverloadCollisionPass.Apply(model.Compilation));
        Assert.False(pointerSize.IsIgnored());
        Assert.False(span.IsIgnored());
    }

    [Fact]
    public void Apply_KeepsPointerSizeOverload_WhenItsSpanReplacementLosesToAStringViewOverload()
    {
        var model = new FlagSetTestModel();
        var record = model.AddRecord("Buffer");
        var pointerSize = model.AddFunction(record, "Buffer", BindingType.Void, VoidPointer, BindingType.UInt32);
        var stringView = model.AddFunction(record, "Buffer", BindingType.Void, StringView);
        var span = model.AddFunction(record, "Buffer", BindingType.Void, ByteSpan);
        MakeOverloads(pointerSize, stringView);
        MakeOverloads(pointerSize, span);

        Assert.Equal(1, RbfxSpanOverloadCollisionPass.Apply(model.Compilation));
        Assert.True(span.IsSilentlyExcluded());
        Assert.Equal(RbfxSpanOverloadCollisionPass.ManagedSignatureReason, span.GetIgnoreReason());
        Assert.False(pointerSize.IsIgnored());
        Assert.False(stringView.IsIgnored());
    }

    [Fact]
    public void Apply_ExcludesSpanOverload_OfAFunctionThatRetainsThePointer()
    {
        var model = new FlagSetTestModel();
        var record = model.AddRecord("MemoryBuffer");
        var span = model.AddFunction(record, "MemoryBuffer", BindingType.Void, ByteSpan);
        var pointerSize = model.AddFunction(record, "MemoryBuffer", BindingType.Void, VoidPointer, BindingType.UInt32);
        span.CppQualifiedName = "Urho3D::MemoryBuffer::MemoryBuffer";
        MakeOverloads(span, pointerSize);

        Assert.Equal(1, RbfxSpanOverloadCollisionPass.Apply(model.Compilation));
        Assert.True(span.IsSilentlyExcluded());
        Assert.Equal(RbfxSpanOverloadCollisionPass.RetainsPointerReason, span.GetIgnoreReason());
        Assert.False(pointerSize.IsIgnored());
    }

    // The test model gives each function a unique symbol id and qualified name. Overloads share
    // the qualified name.
    private static void MakeOverloads(FunctionBinding first, FunctionBinding second)
    {
        second.CppQualifiedName = first.CppQualifiedName;
    }
}
