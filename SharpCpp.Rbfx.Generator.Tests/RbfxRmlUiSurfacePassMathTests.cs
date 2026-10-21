using SharpCpp.Model.Ast;
using Xunit;

namespace SharpCpp.Rbfx.Generator.Tests;

/// <summary>
/// Tests of the RmlUi math types in the RmlUi surface. RbfxRmlMathMarshallers.hpp maps
/// Rml::Vector2f, Rml::Vector2i, Rml::Vector3f, Rml::Vector4f, Rml::Colourf and Rml::Colourb to Urho3D math types.
/// The surface allows only these instantiations.
/// </summary>
public sealed class RbfxRmlUiSurfacePassMathTests
{
    private const string RmlUiHeader = "Urho3D/RmlUI/RmlUI.h";

    public static TheoryData<string, string> MappedTypes => new()
    {
        { "Rml::Vector2f", "global::Urho3D.Math.Vector2" },
        { "const Rml::Vector2f &", "global::Urho3D.Math.Vector2" },
        { "Rml::Vector2<float>", "global::Urho3D.Math.Vector2" },
        { "Rml::Vector2i", "global::Urho3D.Math.IntVector2" },
        { "Rml::Vector2<int>", "global::Urho3D.Math.IntVector2" },
        { "const Rml::Vector3<float> &", "global::Urho3D.Math.Vector3" },
        { "Rml::Vector4f", "global::Urho3D.Math.Vector4" },
        { "Rml::Colourf", "global::Urho3D.Math.Color" },
        { "Rml::Colour<float, 1>", "global::Urho3D.Math.Color" },
        { "const Rml::Colourb &", "global::Urho3D.Math.Color" },
        { "Rml::Colour<unsigned char, 255>", "global::Urho3D.Math.Color" },
        { "const Rml::Colour<Rml::byte, 255> &", "global::Urho3D.Math.Color" },
    };

    public static TheoryData<string> UnmappedTypes => new()
    {
        "Rml::Vector2<double>",
        "Rml::Vector3i",
        "Rml::Vector3<int>",
        "Rml::Vector4i",
        "Rml::Colour<float, 255>",
        "Rml::ColourbPremultiplied",
        "Rml::Matrix4f",
        "Rml::Vector2fx",
    };

    [Theory]
    [MemberData(nameof(MappedTypes))]
    public void Apply_KeepsCallable_WithMappedMathParameter(string cppTypeName, string managedTypeName)
    {
        var model = new FlagSetTestModel();
        var function = AddFreeFunction(model, "Convert", BindingType.Void, Value(cppTypeName, managedTypeName));

        RbfxRmlUiSurfacePass.Apply(model.Compilation);

        Assert.False(function.IsIgnored());
    }

    [Theory]
    [MemberData(nameof(MappedTypes))]
    public void Apply_KeepsCallable_WithMappedMathReturn(string cppTypeName, string managedTypeName)
    {
        var model = new FlagSetTestModel();
        var function = AddFreeFunction(model, "Make", Value(cppTypeName, managedTypeName));

        RbfxRmlUiSurfacePass.Apply(model.Compilation);

        Assert.False(function.IsIgnored());
    }

    [Theory]
    [MemberData(nameof(UnmappedTypes))]
    public void Apply_ExcludesCallable_WithUnmappedMathParameter(string cppTypeName)
    {
        var model = new FlagSetTestModel();
        var function = AddFreeFunction(model, "Convert", BindingType.Void, Value(cppTypeName, "global::System.IntPtr"));

        RbfxRmlUiSurfacePass.Apply(model.Compilation);

        Assert.True(function.IsSilentlyExcluded());
    }

    [Fact]
    public void Apply_ExcludesColourbOverload_WhenColourfOverloadHasTheSameManagedSignature()
    {
        var model = new FlagSetTestModel();
        var colourf = AddFreeFunction(model, "ToColor", Color, Value("const Rml::Colourf &", "Colourf"));
        var colourb = AddFreeFunction(model, "ToColor", Color, Value("const Rml::Colourb &", "Colourb"));

        RbfxRmlUiSurfacePass.Apply(model.Compilation);

        Assert.False(colourf.IsIgnored());
        Assert.True(colourb.IsSilentlyExcluded());
        Assert.Equal(RbfxRmlUiSurfacePass.RmlMathOverloadCollisionReason, colourb.GetIgnoreReason());
    }

    [Fact]
    public void Apply_ExcludesColourbOverload_WhenUrhoColorOverloadHasTheSameManagedSignature()
    {
        var model = new FlagSetTestModel();
        var color = AddFreeFunction(model, "SetColor", BindingType.Void, Value("Urho3D::Color", "global::Urho3D.Math.Color"));
        var colourb = AddFreeFunction(model, "SetColor", BindingType.Void, Value("Rml::Colour<unsigned char, 255>", "Colourb"));

        RbfxRmlUiSurfacePass.Apply(model.Compilation);

        Assert.False(color.IsIgnored());
        Assert.True(colourb.IsSilentlyExcluded());
    }

    [Fact]
    public void Apply_KeepsColourbOverload_WithoutCollision()
    {
        var model = new FlagSetTestModel();
        var colourb = AddFreeFunction(model, "ToColor", Color, Value("const Rml::Colourb &", "Colourb"));
        var other = AddFreeFunction(model, "ToColor", Color, Value("const Rml::Colourb &", "Colourb"), BindingType.Boolean);
        var otherName = AddFreeFunction(model, "ToColorf", Color, Value("const Rml::Colourf &", "Colourf"));

        RbfxRmlUiSurfacePass.Apply(model.Compilation);

        Assert.False(colourb.IsIgnored());
        Assert.False(other.IsIgnored());
        Assert.False(otherName.IsIgnored());
    }

    [Fact]
    public void Apply_ExcludesRmlOverload_WhenFreeFunctionInAnotherHeaderHasTheSameManagedSignature()
    {
        // Free functions of one namespace go to one managed Bindings class, for example ImGui.h and RmlUI.h.
        var model = new FlagSetTestModel();
        var vector2 = Value("global::Urho3D.Math.Vector2", "global::Urho3D.Math.Vector2");
        var imgui = AddFreeFunction(model, "ToVector2", vector2, Value("const ImVec2 &", "global::Urho3D.Math.Vector2"));
        imgui.HeaderIncludePath = "Urho3D/SystemUI/ImGui.h";
        var rml = AddFreeFunction(model, "ToVector2", vector2, Value("const Rml::Vector2f &", "Vector2f"));

        RbfxRmlUiSurfacePass.Apply(model.Compilation);

        Assert.False(imgui.IsIgnored());
        Assert.True(rml.IsSilentlyExcluded());
        Assert.Equal(RbfxRmlUiSurfacePass.RmlMathOverloadCollisionReason, rml.GetIgnoreReason());
    }

    [Fact]
    public void Apply_KeepsRmlOverloads_WithDifferentManagedTypes()
    {
        var model = new FlagSetTestModel();
        var vector2 = AddFreeFunction(model, "ToVector", BindingType.Void, Value("const Rml::Vector2f &", "Vector2f"));
        var intVector2 = AddFreeFunction(model, "ToVector", BindingType.Void, Value("const Rml::Vector2i &", "Vector2i"));
        var vector3 = AddFreeFunction(model, "ToVector", BindingType.Void, Value("Urho3D::Vector3", "global::Urho3D.Math.Vector3"));

        RbfxRmlUiSurfacePass.Apply(model.Compilation);

        Assert.False(vector2.IsIgnored());
        Assert.False(intVector2.IsIgnored());
        Assert.False(vector3.IsIgnored());
    }

    [Theory]
    [InlineData("Rml::Colourb", true)]
    [InlineData("const Rml::Colourb &", true)]
    [InlineData("const Rml::Colour<Rml::byte, 255>&", true)]
    [InlineData("Colour<byte, 255>", true)]
    [InlineData("Rml::Colour<unsigned char, 255> const &", true)]
    [InlineData("Rml::Colourf", false)]
    [InlineData("Rml::Colour<unsigned char, 1>", false)]
    [InlineData("Rml::ColourbPremultiplied", false)]
    [InlineData("", false)]
    public void IsRmlColourType_MatchesColourbSpellings(string cppTypeName, bool expected)
    {
        Assert.Equal(expected, RbfxRmlUiSurfacePass.IsRmlColourType(
            cppTypeName, ["Colourb", "Colour<unsignedchar,255>", "Colour<byte,255>"]));
    }

    [Theory]
    [InlineData("Rml::Vector2", true)]
    [InlineData("Rml::Colour", true)]
    [InlineData("Rml::Vector2", false)]
    public void ApplyIntentionalGaps_SilencesIgnoredMembersOfMappedMathTemplate(string cppQualifiedName, bool suppressEmission)
    {
        var model = new FlagSetTestModel();
        var record = new RecordBinding($"record:{cppQualifiedName}", "Template", "Template", cppQualifiedName)
        {
            HeaderIncludePath = "RmlUi/Core/Vector2.h"
        };
        if (suppressEmission)
        {
            record.SetMetadata(MetadataKeys.SuppressEmission, true);
        }

        model.Compilation.Members.Add(record);
        var member = model.AddFunction(record, "Normalise", BindingType.Void);
        member.SetIgnoreReason("open generic parameter usage is not renderer-ready in a non-generic record", overwriteExisting: true);

        RbfxSilentExclusionPass.ApplyIntentionalGaps(model.Compilation);

        Assert.True(member.IsSilentlyExcluded());
    }

    [Fact]
    public void ApplyIntentionalGaps_KeepsDiagnosticForMembersOfUnmappedRmlTemplate()
    {
        var model = new FlagSetTestModel();
        var record = new RecordBinding("record:Rml::Matrix4", "Matrix4", "Matrix4", "Rml::Matrix4")
        {
            HeaderIncludePath = "RmlUi/Core/Matrix4.h"
        };
        model.Compilation.Members.Add(record);
        var member = model.AddFunction(record, "Transpose", BindingType.Void);
        member.SetIgnoreReason("open generic parameter usage is not renderer-ready in a non-generic record", overwriteExisting: true);

        RbfxSilentExclusionPass.ApplyIntentionalGaps(model.Compilation);

        Assert.False(member.IsSilentlyExcluded());
    }

    private static readonly BindingType Color = Value("Urho3D::Color", "global::Urho3D.Math.Color");

    private static BindingType Value(string cppTypeName, string managedTypeName) =>
        BindingType.CreateConfigured(cppTypeName, managedTypeName, null, null);

    private static FunctionBinding AddFreeFunction(
        FlagSetTestModel model,
        string name,
        BindingType returnType,
        params BindingType[] parameterTypes)
    {
        var function = new FunctionBinding(
            $"function:{name}:{model.Compilation.Functions.Count()}",
            name,
            name,
            returnType,
            cppReturnTypeName: returnType.CppTypeName,
            cppQualifiedName: $"Urho3D::{name}")
        {
            HeaderIncludePath = RmlUiHeader
        };
        for (var i = 0; i < parameterTypes.Length; i++)
        {
            function.Parameters.Add(new ParameterBinding(
                $"param:{name}:{i}", $"arg{i}", $"arg{i}", parameterTypes[i], cppTypeName: parameterTypes[i].CppTypeName));
        }

        model.Compilation.Members.Add(function);
        return function;
    }
}
