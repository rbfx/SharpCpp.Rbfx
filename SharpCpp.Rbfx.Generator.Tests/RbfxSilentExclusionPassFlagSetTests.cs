using SharpCpp.Model.Ast;
using Xunit;

namespace SharpCpp.Rbfx.Generator.Tests;

/// <summary>Tests of the silent exclusions that the rbfx pass applies for FlagSet.</summary>
public sealed class RbfxSilentExclusionPassFlagSetTests
{
    private const string TemplateReason = "rbfx FlagSet<TEnum> is lowered to the enum managed surface";

    [Fact]
    public void ApplyFlagSetTemplate_SilencesIgnoredRecordAndMembers()
    {
        var model = new FlagSetTestModel();
        model.FlagSet.SetIgnoreReason("generic record 'FlagSet' does not support operators in this slice");
        var member = model.AddFunction(model.FlagSet, "operator|=", BindingType.Void);
        member.SetIgnoreReason("ClangSharp sample parser does not support 'CXType_Unexposed' (FlagSet<E>).");

        var count = RbfxSilentExclusionPass.ApplyFlagSetTemplate(model.Compilation);

        Assert.Equal(2, count);
        Assert.True(model.FlagSet.IsSilentlyExcluded());
        Assert.Equal(TemplateReason, model.FlagSet.GetIgnoreReason());
        Assert.True(member.IsSilentlyExcluded());
        Assert.Equal(TemplateReason, member.GetIgnoreReason());
    }

    [Fact]
    public void ApplyFlagSetTemplate_DoesNotSilenceMembersThatAreNotIgnored()
    {
        var model = new FlagSetTestModel();
        var member = model.AddFunction(model.FlagSet, "Test", BindingType.Void);

        var count = RbfxSilentExclusionPass.ApplyFlagSetTemplate(model.Compilation);

        Assert.Equal(0, count);
        Assert.False(member.IsIgnored());
    }

    [Fact]
    public void ApplyFlagSetTemplate_KeepsReasonOfFunctionsThatUseFlagSet()
    {
        var model = new FlagSetTestModel();
        var owner = model.AddRecord("UIElement");
        var function = model.AddFunction(owner, "OnClickBegin", BindingType.Void, model.FlagSetType);
        const string reason = "parameter 'buttons' is unsupported in this bootstrap surface";
        function.SetIgnoreReason(reason);

        RbfxSilentExclusionPass.ApplyFlagSetTemplate(model.Compilation);

        Assert.False(function.IsSilentlyExcluded());
        Assert.Equal(reason, function.GetIgnoreReason());
    }

    [Theory]
    [InlineData("operator|")]
    [InlineData("operator&")]
    [InlineData("operator^")]
    [InlineData("operator~")]
    public void Apply_SilencesFreeFlagSetOperators(string name)
    {
        var model = new FlagSetTestModel();
        var function = model.AddFunction(model.Compilation, name, model.FlagSetType, model.EnumType, model.EnumType);
        function.OperatorToken = name["operator".Length..];

        RbfxSilentExclusionPass.Apply(model.Compilation);

        Assert.True(function.IsSilentlyExcluded());
        Assert.Contains("FlagSet operator", function.GetIgnoreReason());
    }

    [Fact]
    public void Apply_DoesNotSilenceOperatorThatIsNotFlagSet()
    {
        var model = new FlagSetTestModel();
        var vectorType = BindingType.CreateHandle("Urho3D::Vector3", "Vector3", recordId: "record:Urho3D::Vector3");
        var function = model.AddFunction(model.Compilation, "operator|", vectorType, vectorType, vectorType);
        function.OperatorToken = "|";

        RbfxSilentExclusionPass.Apply(model.Compilation);

        Assert.False(function.IsSilentlyExcluded());
    }

    [Fact]
    public void Apply_SilencesStaticTypeHierarchyArray()
    {
        var model = new FlagSetTestModel();
        var owner = model.AddRecord("Node");
        model.AddFunction(owner, "GetTypeInfoStatic", BindingType.Void);
        var field = AddTypeHierarchyField(owner, "TypeHierarchy", isStatic: true, BindingType.Void.WithFixedArray(BindingType.Void, 3));
        field.SetIgnoreReason("static fixed-size array fields are not yet supported in this bootstrap surface");

        RbfxSilentExclusionPass.Apply(model.Compilation);

        Assert.True(field.IsSilentlyExcluded());
        Assert.Contains("type hierarchy", field.GetIgnoreReason());
    }

    [Fact]
    public void Apply_KeepsTypeHierarchyArrayOfRecordWithoutTypeInfoStatics()
    {
        var model = new FlagSetTestModel();
        var owner = model.AddRecord("Plain");
        var field = AddTypeHierarchyField(owner, "TypeHierarchy", isStatic: true, BindingType.Void.WithFixedArray(BindingType.Void, 3));
        field.SetIgnoreReason("static fixed-size array fields are not yet supported in this bootstrap surface");

        RbfxSilentExclusionPass.Apply(model.Compilation);

        Assert.False(field.IsSilentlyExcluded());
    }

    [Fact]
    public void Apply_KeepsOtherStaticArrayFields()
    {
        var model = new FlagSetTestModel();
        var owner = model.AddRecord("Node");
        model.AddFunction(owner, "GetTypeInfoStatic", BindingType.Void);
        var field = AddTypeHierarchyField(owner, "Table", isStatic: true, BindingType.Void.WithFixedArray(BindingType.Void, 3));
        field.SetIgnoreReason("static fixed-size array fields are not yet supported in this bootstrap surface");

        RbfxSilentExclusionPass.Apply(model.Compilation);

        Assert.False(field.IsSilentlyExcluded());
    }

    [Fact]
    public void Apply_KeepsTypeHierarchyFieldThatIsNotStaticArray()
    {
        var model = new FlagSetTestModel();
        var owner = model.AddRecord("Node");
        model.AddFunction(owner, "GetTypeInfoStatic", BindingType.Void);
        var field = AddTypeHierarchyField(owner, "TypeHierarchy", isStatic: false, BindingType.Void.WithFixedArray(BindingType.Void, 3));
        var sceneOwner = model.AddRecord("Scene");
        model.AddFunction(sceneOwner, "GetTypeInfoStatic", BindingType.Void);
        var scalar = AddTypeHierarchyField(sceneOwner, "TypeHierarchy", isStatic: true, BindingType.Void);

        RbfxSilentExclusionPass.Apply(model.Compilation);

        Assert.False(field.IsSilentlyExcluded());
        Assert.False(scalar.IsSilentlyExcluded());
    }

    private static FieldBinding AddTypeHierarchyField(RecordBinding owner, string name, bool isStatic, BindingType type)
    {
        var field = new FieldBinding($"field:{owner.NativeName}:{name}", name, name, type, "int", owner.CppQualifiedName)
        {
            IsStatic = isStatic
        };
        owner.Members.Add(field);
        return field;
    }
}
