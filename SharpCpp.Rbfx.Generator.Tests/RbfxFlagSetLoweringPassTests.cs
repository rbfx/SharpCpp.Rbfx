using SharpCpp.Model.Ast;
using Xunit;

namespace SharpCpp.Rbfx.Generator.Tests;

/// <summary>Tests of the pass that lowers FlagSet&lt;TEnum&gt; to the enum managed surface.</summary>
public sealed class RbfxFlagSetLoweringPassTests
{
    [Fact]
    public void Apply_LowersFunctionReturnAndParameterTypesToEnum()
    {
        var model = new FlagSetTestModel();
        var owner = model.AddRecord("Input");
        var function = model.AddFunction(owner, "GetButtons", model.FlagSetType, model.FlagSetType);

        RbfxFlagSetLoweringPass.Apply(model.Compilation);

        Assert.Equal(BindingTypeKind.Enum, function.ReturnType.Kind);
        Assert.Equal(FlagSetTestModel.LoweredEnumName, function.ReturnType.ManagedTypeName);
        Assert.Equal(FlagSetTestModel.EnumId, function.ReturnType.EnumId);
        Assert.Equal(BindingTypeKind.Enum, function.Parameters[0].Type.Kind);
        Assert.Equal(FlagSetTestModel.LoweredEnumName, function.Parameters[0].Type.ManagedTypeName);
    }

    [Fact]
    public void Apply_LowersFieldTypeToEnum()
    {
        var model = new FlagSetTestModel();
        var owner = model.AddRecord("Input");
        var field = new FieldBinding("field:buttons", "buttons_", "Buttons", model.FlagSetType, "Urho3D::FlagSet<Urho3D::MouseButton>", "Urho3D::Input");
        owner.Members.Add(field);

        RbfxFlagSetLoweringPass.Apply(model.Compilation);

        Assert.Equal(BindingTypeKind.Enum, field.Type.Kind);
        Assert.Equal(FlagSetTestModel.LoweredEnumName, field.Type.ManagedTypeName);
    }

    [Fact]
    public void Apply_LowersTypeAliasTargetToEnum()
    {
        var model = new FlagSetTestModel();
        model.Compilation.TypeAliases.Add(new BindingTypeAlias("Urho3D::MouseButtonFlags", "MouseButtonFlags", "MouseButtonFlags", model.FlagSetType));

        RbfxFlagSetLoweringPass.Apply(model.Compilation);

        var alias = Assert.Single(model.Compilation.TypeAliases);
        Assert.Equal(BindingTypeKind.Enum, alias.TargetType.Kind);
        Assert.Equal(FlagSetTestModel.LoweredEnumName, alias.TargetType.ManagedTypeName);
    }

    [Fact]
    public void Apply_MarksLoweredEnumAsFlags()
    {
        var model = new FlagSetTestModel();
        var owner = model.AddRecord("Input");
        model.AddFunction(owner, "GetButtons", model.FlagSetType);

        RbfxFlagSetLoweringPass.Apply(model.Compilation);

        Assert.True(model.Enum.IsFlags);
    }

    [Fact]
    public void Apply_IgnoresFlagSetRecordWithLoweringReason()
    {
        var model = new FlagSetTestModel();

        RbfxFlagSetLoweringPass.Apply(model.Compilation);

        Assert.True(model.FlagSet.IsIgnored());
        Assert.Equal("rbfx FlagSet<TEnum> is lowered to the enum managed surface", model.FlagSet.GetIgnoreReason());
        Assert.True(model.FlagSet.GetMetadata<bool>(MetadataKeys.SuppressEmission));
    }

    [Fact]
    public void Apply_KeepsTypesThatAreNotFlagSet()
    {
        var model = new FlagSetTestModel();
        var owner = model.AddRecord("Input");
        var function = model.AddFunction(owner, "GetButton", model.EnumType);

        RbfxFlagSetLoweringPass.Apply(model.Compilation);

        Assert.Equal("MouseButton", function.ReturnType.ManagedTypeName);
    }
}
