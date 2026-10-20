using SharpCpp.Model.Ast;
using SharpCpp.Pipeline;
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

    [Fact]
    public void Apply_KeepsIgnoreReasonOfLoweredDeclarations()
    {
        var model = new FlagSetTestModel();
        var owner = model.AddRecord("Input");
        var function = model.AddFunction(owner, "GetButtons", model.FlagSetType);
        function.SetIgnoreReason("other reason");
        var field = new FieldBinding("field:buttons", "buttons_", "Buttons", model.FlagSetType, "Urho3D::FlagSet<Urho3D::MouseButton>", "Urho3D::Input");
        field.SetIgnoreReason("other field reason");
        owner.Members.Add(field);

        RbfxFlagSetLoweringPass.Apply(model.Compilation);

        Assert.Equal("other reason", function.GetIgnoreReason());
        Assert.Equal("other field reason", field.GetIgnoreReason());
    }

    [Fact]
    public void Apply_LoweredFunctionPassesSurfaceValidation()
    {
        var model = ValidationReadyModel(out var function);

        RbfxFlagSetLoweringPass.Apply(model.Compilation);
        DegradeUnsupportedSurfacePass.Apply(model.Compilation);

        Assert.False(function.IsIgnored(), function.GetIgnoreReason());
    }

    [Fact]
    public void SurfaceValidation_IgnoresFunctionThatKeepsFlagSet()
    {
        var model = ValidationReadyModel(out var function);

        DegradeUnsupportedSurfacePass.Apply(model.Compilation);

        Assert.True(function.IsIgnored());
        Assert.Contains("FlagSet", function.GetIgnoreReason());
    }

    [Fact]
    public void Apply_LoweredFunctionStaysIgnoredForAnotherUnsupportedType()
    {
        var model = ValidationReadyModel(out var function);
        var missingType = BindingType.CreateHandle("Urho3D::Missing", "Missing", recordId: "record:Urho3D::Missing");
        function.Parameters.Add(new ParameterBinding("param:Scan:1", "missing", "missing", missingType));

        RbfxFlagSetLoweringPass.Apply(model.Compilation);
        DegradeUnsupportedSurfacePass.Apply(model.Compilation);

        Assert.True(function.IsIgnored());
        Assert.DoesNotContain("FlagSet", function.GetIgnoreReason());
    }

    [Theory]
    [InlineData("operator|")]
    [InlineData("operator&")]
    [InlineData("operator^")]
    public void Apply_SilencesFreeFlagEnumBinaryOperators(string name)
    {
        var model = new FlagSetTestModel();
        var function = model.AddFunction(model.Compilation, name, model.FlagSetType, model.EnumType, model.EnumType);

        RbfxFlagSetLoweringPass.Apply(model.Compilation);

        Assert.True(function.IsSilentlyExcluded());
        Assert.Contains("FlagSet operator", function.GetIgnoreReason());
    }

    [Fact]
    public void Apply_SilencesFreeFlagEnumComplementOperator()
    {
        var model = new FlagSetTestModel();
        var function = model.AddFunction(model.Compilation, "operator~", model.FlagSetType, model.EnumType);

        RbfxFlagSetLoweringPass.Apply(model.Compilation);

        Assert.True(function.IsSilentlyExcluded());
    }

    [Fact]
    public void Apply_KeepsOperatorThatDoesNotReturnFlagSet()
    {
        var model = new FlagSetTestModel();
        var function = model.AddFunction(model.Compilation, "operator|", model.EnumType, model.EnumType, model.EnumType);

        RbfxFlagSetLoweringPass.Apply(model.Compilation);

        Assert.False(function.IsIgnored());
    }

    [Fact]
    public void Apply_KeepsFlagSetOperatorWithAnotherParameterType()
    {
        var model = new FlagSetTestModel();
        var vectorType = BindingType.CreateHandle("Urho3D::Vector3", "Vector3", recordId: "record:Urho3D::Vector3");
        var function = model.AddFunction(model.Compilation, "operator|", model.FlagSetType, model.EnumType, vectorType);

        RbfxFlagSetLoweringPass.Apply(model.Compilation);

        Assert.False(function.IsIgnored());
    }

    [Fact]
    public void Apply_KeepsFlagSetFunctionThatIsNotAnOperator()
    {
        var model = new FlagSetTestModel();
        var function = model.AddFunction(model.Compilation, "Combine", model.FlagSetType, model.EnumType, model.EnumType);

        RbfxFlagSetLoweringPass.Apply(model.Compilation);

        Assert.False(function.IsIgnored());
    }

    [Fact]
    public void Apply_LowersConstReferenceParameter()
    {
        var model = new FlagSetTestModel();
        var function = model.AddFunction(model.Compilation, "Use", BindingType.Void, model.FlagSetType);
        var parameter = function.Parameters[0];
        parameter.CppTypeName = "const Urho3D::FlagSet<Urho3D::MouseButton> &";

        RbfxFlagSetLoweringPass.Apply(model.Compilation);

        Assert.Equal(BindingTypeKind.Enum, parameter.Type.Kind);
    }

    [Theory]
    [InlineData("const Urho3D::FlagSet<Urho3D::MouseButton> *", BindingParameterDirection.In)]
    [InlineData("Urho3D::FlagSet<Urho3D::MouseButton> *", BindingParameterDirection.Out)]
    [InlineData("Urho3D::FlagSet<Urho3D::MouseButton> &", BindingParameterDirection.Ref)]
    [InlineData("Urho3D::FlagSet<Urho3D::MouseButton> &&", BindingParameterDirection.In)]
    public void Apply_KeepsPointerAndNonConstReferenceParameters(string spelling, BindingParameterDirection direction)
    {
        var model = new FlagSetTestModel();
        var function = model.AddFunction(model.Compilation, "Use", BindingType.Void, model.FlagSetType);
        var parameter = function.Parameters[0];
        parameter.CppTypeName = spelling;
        parameter.Direction = direction;

        RbfxFlagSetLoweringPass.Apply(model.Compilation);

        Assert.Equal(BindingTypeKind.Handle, parameter.Type.Kind);
    }

    [Theory]
    [InlineData("Urho3D::FlagSet<Urho3D::MouseButton> *")]
    [InlineData("const Urho3D::FlagSet<Urho3D::MouseButton> &")]
    public void Apply_KeepsPointerAndReferenceReturnAndField(string spelling)
    {
        var model = new FlagSetTestModel();
        var owner = model.AddRecord("Input");
        var function = model.AddFunction(owner, "Get", model.FlagSetType);
        function.CppReturnTypeName = spelling;
        var field = new FieldBinding("field:buttons", "buttons_", "Buttons", model.FlagSetType, spelling, "Urho3D::Input");
        owner.Members.Add(field);

        RbfxFlagSetLoweringPass.Apply(model.Compilation);

        Assert.Equal(BindingTypeKind.Handle, function.ReturnType.Kind);
        Assert.Equal(BindingTypeKind.Handle, field.Type.Kind);
    }

    [Fact]
    public void Apply_KeepsPointerParameterIgnoredByValidation()
    {
        var model = ValidationReadyModel(out var function);
        function.Parameters[0].CppTypeName = "Urho3D::FlagSet<Urho3D::MouseButton> *";

        RbfxFlagSetLoweringPass.Apply(model.Compilation);
        DegradeUnsupportedSurfacePass.Apply(model.Compilation);

        Assert.True(function.IsIgnored());
        Assert.Contains("FlagSet", function.GetIgnoreReason());
    }

    [Theory]
    [InlineData("{}", "default")]
    [InlineData("0", "default")]
    [InlineData("Urho3D::MouseButton::Left", "global::Urho3D.MouseButton.Left")]
    [InlineData("MouseButton::None", "global::Urho3D.MouseButton.None")]
    [InlineData("Left", "global::Urho3D.MouseButton.Left")]
    [InlineData("MouseButton::Left | MouseButton::Right", "global::Urho3D.MouseButton.Left | global::Urho3D.MouseButton.Right")]
    [InlineData("(MouseButton::Left) | (MouseButton::Right)", "global::Urho3D.MouseButton.Left | global::Urho3D.MouseButton.Right")]
    [InlineData("{MouseButton::Left}", "global::Urho3D.MouseButton.Left")]
    public void Apply_ConvertsNativeDefaultToManagedEnumDefault(string nativeDefault, string expected)
    {
        var model = new FlagSetTestModel();
        var function = AddFunctionWithDefault(model, nativeDefault);

        RbfxFlagSetLoweringPass.Apply(model.Compilation);

        var parameter = function.Parameters[1];
        Assert.Equal(expected, parameter.ManagedDefaultValueExpression);
        Assert.False(parameter.HasMetadata(MetadataKeys.UnexpressibleDefault));
    }

    [Theory]
    [InlineData("DRAWABLE_ANY")]
    [InlineData("MouseButton::Missing")]
    [InlineData("static_cast<MouseButton>(3)")]
    [InlineData("7")]
    public void Apply_KeepsUnexpressibleDefaultThatCannotBeConverted(string nativeDefault)
    {
        var model = new FlagSetTestModel();
        var function = AddFunctionWithDefault(model, nativeDefault);

        RbfxFlagSetLoweringPass.Apply(model.Compilation);

        var parameter = function.Parameters[1];
        Assert.Null(parameter.ManagedDefaultValueExpression);
        Assert.True(parameter.HasMetadata(MetadataKeys.UnexpressibleDefault));
        Assert.Equal(nativeDefault, parameter.GetMetadata<string>(MetadataKeys.NativeDefaultValueExpression));
    }

    [Fact]
    public void Apply_LowersFlagSetOfSixtyFourBitEnum()
    {
        var model = new FlagSetTestModel();
        var bigEnum = new EnumBinding("enum:Urho3D::Big", "Big", "Big", "ulong", "Urho3D::Big", BindingPrimitiveKind.UInt64);
        model.Compilation.Members.Add(bigEnum);
        var bigEnumType = BindingType.CreateEnum("Urho3D::Big", "Big", enumId: bigEnum.SymbolId);
        var bigFlagSet = BindingType.CreateHandle(
            "Urho3D::FlagSet<Urho3D::Big>",
            "FlagSet<Big>",
            recordId: FlagSetTestModel.FlagSetId,
            genericArguments: new[] { bigEnumType });
        var function = model.AddFunction(model.Compilation, "UseBig", bigFlagSet, bigFlagSet);

        RbfxFlagSetLoweringPass.Apply(model.Compilation);

        Assert.Equal(BindingTypeKind.Enum, function.ReturnType.Kind);
        Assert.Equal("global::Urho3D.Big", function.Parameters[0].Type.ManagedTypeName);
        Assert.True(bigEnum.IsFlags);
        Assert.Equal("ulong", bigEnum.UnderlyingTypeName);
    }

    [Fact]
    public void Apply_KeepsCallbackAndNestedGenericWithFlagSetIgnored()
    {
        var model = ValidationReadyModel(out var function);
        var callbackType = BindingType.CreateCallback(
            "ea::function<void(Urho3D::FlagSet<Urho3D::MouseButton>)>",
            "global::System.Action<{0}>",
            null,
            BindingType.Void,
            new[] { model.FlagSetType });
        var vectorType = BindingType.CreateHandle(
            "ea::vector<Urho3D::FlagSet<Urho3D::MouseButton>>",
            "Vector<FlagSet<MouseButton>>",
            recordId: "record:ea::vector",
            genericArguments: new[] { model.FlagSetType });
        function.Parameters.Clear();
        function.Parameters.Add(new ParameterBinding("param:callback", "callback", "callback", callbackType));
        var vectorFunction = model.AddFunction(model.Compilation, "UseVector", BindingType.Void, vectorType);

        RbfxFlagSetLoweringPass.Apply(model.Compilation);
        DegradeUnsupportedSurfacePass.Apply(model.Compilation);

        Assert.Equal(BindingTypeKind.Handle, callbackType.CallbackParameterTypes[0].Kind);
        Assert.Equal(BindingTypeKind.Handle, vectorType.GenericArguments[0].Kind);
        Assert.True(function.IsIgnored());
        Assert.True(vectorFunction.IsIgnored());
    }

    private static FunctionBinding AddFunctionWithDefault(FlagSetTestModel model, string nativeDefault)
    {
        var function = model.AddFunction(model.Compilation, "Use", BindingType.Void, model.EnumType, model.FlagSetType);
        var parameter = function.Parameters[1];
        parameter.SetMetadata(MetadataKeys.NativeDefaultValueExpression, nativeDefault);
        parameter.SetMetadata(MetadataKeys.UnexpressibleDefault, true);
        return function;
    }

    private static FlagSetTestModel ValidationReadyModel(out FunctionBinding function)
    {
        var model = new FlagSetTestModel();
        model.Compilation.NamespaceMappings = new Dictionary<string, string> { ["Urho3D"] = "Urho3D" };
        model.FlagSet.SetIgnoreReason("generic record 'FlagSet' does not support operators in this slice");
        var owner = model.AddRecord("Input");
        function = model.AddFunction(owner, "Scan", model.FlagSetType, model.FlagSetType);
        return model;
    }
}
