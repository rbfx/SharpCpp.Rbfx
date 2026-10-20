using SharpCpp.Model.Ast;
using Xunit;

namespace SharpCpp.Rbfx.Generator.Tests;

/// <summary>Tests of the silent exclusions for declarations that rbfx leaves unbound on purpose.</summary>
public sealed class RbfxSilentExclusionPassIntentionalGapsTests
{
    private const string OutsideNamespaceSuffix = "is outside the configured namespace mappings for this compilation";

    private static string ExternalRecordReason(string recordName, string detail)
        => $"parameter 'value' is unsupported in this bootstrap surface: record '{recordName}' is ignored: {detail}";

    private static string DefinedOutside(string recordName)
        => $"record '{recordName}' is defined outside headers owned by this compilation (types: spelled='{recordName}')";

    [Theory]
    [InlineData("pugi::xml_node")]
    [InlineData("Diligent::RefCntAutoPtr")]
    [InlineData("btRigidBody")]
    [InlineData("b2Body")]
    [InlineData("rcContext")]
    [InlineData("dtNavMesh")]
    [InlineData("SDL_Window")]
    [InlineData("_SDL_Joystick")]
    [InlineData("embree3::RTCDevice")]
    [InlineData("CLI::App")]
    [InlineData("tracy::SourceLocationData")]
    [InlineData("ImDrawList")]
    [InlineData("std::initializer_list")]
    public void ApplyIntentionalGaps_SilencesFunctionBlamedOnThirdPartyType(string typeName)
    {
        var model = new FlagSetTestModel();
        var owner = model.AddRecord("Subject");
        var function = model.AddFunction(owner, "Use", BindingType.Void);
        var reason = ExternalRecordReason(typeName, DefinedOutside(typeName));
        function.SetIgnoreReason(reason);

        var count = RbfxSilentExclusionPass.ApplyIntentionalGaps(model.Compilation);

        Assert.Equal(1, count);
        Assert.True(function.IsSilentlyExcluded());
        Assert.Equal(reason, function.GetIgnoreReason());
    }

    [Theory]
    [InlineData("eastl::optional")]
    [InlineData("eastl::span")]
    [InlineData("Urho3D::TransformedSpan::Iterator")]
    [InlineData("Btree")]
    [InlineData("Image")]
    public void ApplyIntentionalGaps_KeepsFunctionBlamedOnContainerOrOwnType(string typeName)
    {
        var model = new FlagSetTestModel();
        var owner = model.AddRecord("Subject");
        var function = model.AddFunction(owner, "Use", BindingType.Void);
        function.SetIgnoreReason(ExternalRecordReason(typeName, DefinedOutside(typeName)));

        var count = RbfxSilentExclusionPass.ApplyIntentionalGaps(model.Compilation);

        Assert.Equal(0, count);
        Assert.False(function.IsSilentlyExcluded());
    }

    [Fact]
    public void ApplyIntentionalGaps_SilencesFieldWithGlobalDeclarationOutsideNamespaceMappings()
    {
        var model = new FlagSetTestModel();
        var owner = model.AddRecord("Subject");
        var field = new FieldBinding("field:Subject::font_", "font_", "Font", model.EnumType, "ImFont *", "Urho3D::Subject");
        owner.Members.Add(field);
        field.SetIgnoreReason(
            $"field type is unsupported in this bootstrap surface: record 'ImFont' is ignored: global declaration 'ImFont' {OutsideNamespaceSuffix}");

        var count = RbfxSilentExclusionPass.ApplyIntentionalGaps(model.Compilation);

        Assert.Equal(1, count);
        Assert.True(field.IsSilentlyExcluded());
    }

    [Fact]
    public void ApplyIntentionalGaps_SilencesFunctionWhoseOwnReasonNamesNamespaceRoot()
    {
        var model = new FlagSetTestModel();
        var owner = model.AddRecord("Subject");
        var function = model.AddFunction(owner, "Use", BindingType.Void);
        function.SetIgnoreReason($"declaration namespace root 'ImVec2' {OutsideNamespaceSuffix}");

        Assert.Equal(1, RbfxSilentExclusionPass.ApplyIntentionalGaps(model.Compilation));
        Assert.True(function.IsSilentlyExcluded());
    }

    [Fact]
    public void ApplyIntentionalGaps_KeepsOtherReasonsOfFunctionsThatUseThirdPartyTypes()
    {
        var model = new FlagSetTestModel();
        var owner = model.AddRecord("Subject");
        var function = model.AddFunction(owner, "Use", BindingType.Void);
        function.SetIgnoreReason("operator 'operator+' is not supported in this bootstrap surface");

        Assert.Equal(0, RbfxSilentExclusionPass.ApplyIntentionalGaps(model.Compilation));
        Assert.False(function.IsSilentlyExcluded());
    }

    [Fact]
    public void ApplyIntentionalGaps_SilencesRecordWithoutApiSurface()
    {
        var model = new FlagSetTestModel();
        var record = model.AddRecord("IdScopeGuard");
        const string reason = "record 'Urho3D::IdScopeGuard' has no user-defined API surface (it is likely a template utility type)";
        record.SetIgnoreReason(reason);
        var other = model.AddRecord("Other");
        other.SetIgnoreReason("generic record 'Other' has no known concrete instantiations");

        Assert.Equal(1, RbfxSilentExclusionPass.ApplyIntentionalGaps(model.Compilation));
        Assert.True(record.IsSilentlyExcluded());
        Assert.Equal(reason, record.GetIgnoreReason());
        Assert.False(other.IsSilentlyExcluded());
    }

    [Fact]
    public void ApplyIntentionalGaps_SilencesIgnoredMembersOfSilentRecord()
    {
        var model = new FlagSetTestModel();
        var owner = model.AddRecord("RmlRenderer");
        owner.SetSilentExclusionReason("declaration is in an internal rbfx detail namespace: 'Urho3D::Detail::RmlRenderer'");
        var ignored = model.AddFunction(owner, "Render", BindingType.Void);
        ignored.SetIgnoreReason("declaration is in an internal detail namespace");
        var kept = model.AddFunction(owner, "Keep", BindingType.Void);

        Assert.Equal(1, RbfxSilentExclusionPass.ApplyIntentionalGaps(model.Compilation));
        Assert.True(ignored.IsSilentlyExcluded());
        Assert.False(kept.IsIgnored());
    }

    [Fact]
    public void ApplyIntentionalGaps_KeepsIgnoredMembersOfVisibleRecord()
    {
        var model = new FlagSetTestModel();
        var owner = model.AddRecord("Visible");
        var ignored = model.AddFunction(owner, "Run", BindingType.Void);
        ignored.SetIgnoreReason("operator 'operator+' is not supported in this bootstrap surface");

        Assert.Equal(0, RbfxSilentExclusionPass.ApplyIntentionalGaps(model.Compilation));
        Assert.False(ignored.IsSilentlyExcluded());
    }

    [Fact]
    public void ApplyIntentionalGaps_SilencesDearImGuiDeclarationsOutsideSurface()
    {
        var model = new FlagSetTestModel();
        var internalRecord = model.AddRecord("ImGuiWindow");
        internalRecord.HeaderIncludePath = "ImGui/imgui_internal.h";
        internalRecord.SetIgnoreReason("record 'ImGuiWindow' is ignored");
        var publicRecord = new RecordBinding("record:ImGuiIO", "ImGuiIO", "ImGuiIO", "ImGuiIO")
        {
            HeaderIncludePath = "ImGui/imgui.h"
        };
        model.Compilation.Members.Add(publicRecord);
        publicRecord.SetIgnoreReason("record 'ImGuiIO' is ignored");
        var function = model.AddFunction(model.Compilation, "Helper", BindingType.Void);
        function.HeaderIncludePath = "ImGui/imgui_internal.h";
        function.SetIgnoreReason("callable is not supported");

        Assert.Equal(2, RbfxSilentExclusionPass.ApplyIntentionalGaps(model.Compilation));
        Assert.True(internalRecord.IsSilentlyExcluded());
        Assert.True(function.IsSilentlyExcluded());
        Assert.False(publicRecord.IsSilentlyExcluded());
    }

    [Fact]
    public void ApplyIntentionalGaps_SilencesRmlUiDeclarationsOutsideSurface()
    {
        var model = new FlagSetTestModel();
        var hidden = model.AddRecord("Hidden");
        hidden.HeaderIncludePath = "RmlUi/Core/Hidden.h";
        hidden.SetIgnoreReason("record is only forward-declared");
        var hiddenEnum = new EnumBinding("enum:Rml::Hidden", "Hidden", "Hidden", cppQualifiedName: "Rml::Hidden")
        {
            HeaderIncludePath = "RmlUi/Core/Hidden.h"
        };
        model.Compilation.Members.Add(hiddenEnum);
        hiddenEnum.SetIgnoreReason("enum is not supported");
        var function = model.AddFunction(model.Compilation, "Free", BindingType.Void);
        function.HeaderIncludePath = "RmlUi/Core/Free.h";
        function.SetIgnoreReason("callable is not supported");

        Assert.Equal(3, RbfxSilentExclusionPass.ApplyIntentionalGaps(model.Compilation));
        Assert.True(hidden.IsSilentlyExcluded());
        Assert.True(hiddenEnum.IsSilentlyExcluded());
        Assert.True(function.IsSilentlyExcluded());
    }

    [Fact]
    public void ApplyIntentionalGaps_DoesNothingTwice()
    {
        var model = new FlagSetTestModel();
        var owner = model.AddRecord("Subject");
        var function = model.AddFunction(owner, "Use", BindingType.Void);
        function.SetIgnoreReason(ExternalRecordReason("btRigidBody", DefinedOutside("btRigidBody")));

        Assert.Equal(1, RbfxSilentExclusionPass.ApplyIntentionalGaps(model.Compilation));
        Assert.Equal(0, RbfxSilentExclusionPass.ApplyIntentionalGaps(model.Compilation));
    }

    [Fact]
    public void Apply_DropsIntentionalBasesWithoutWarning()
    {
        var model = new FlagSetTestModel();
        var silentBase = model.AddRecord("NonCopyableLike");
        silentBase.SetSilentExclusionReason("declaration is excluded on purpose");
        var templateBase = model.AddRecord("TemplateLike");
        templateBase.SetIgnoreReason("generic record 'TemplateLike' has no known concrete instantiations");
        var utilityBase = model.AddRecord("UtilityLike");
        utilityBase.SetIgnoreReason("record 'UtilityLike' has no user-defined API surface (it is likely a template utility type)");
        var derived = model.AddRecord("Derived");
        foreach (var baseRecord in new[] { silentBase, templateBase, utilityBase })
        {
            derived.Bases.Add(new RecordBaseBinding(baseRecord.SymbolId, baseRecord.NativeName, baseRecord.ManagedName, baseRecord.CppQualifiedName, isVirtual: false));
        }

        RbfxSilentExclusionPass.Apply(model.Compilation);

        Assert.Empty(derived.Bases);
        Assert.Empty(derived.GetWarnings());
    }

    [Fact]
    public void Apply_WarnsWhenBaseIsIgnoredForAnotherReason()
    {
        var model = new FlagSetTestModel();
        var ignoredBase = model.AddRecord("BrokenBase");
        const string baseReason = "record is only forward-declared";
        ignoredBase.SetIgnoreReason(baseReason);
        var derived = model.AddRecord("Derived");
        derived.Bases.Add(new RecordBaseBinding(ignoredBase.SymbolId, "BrokenBase", "BrokenBase", "Urho3D::BrokenBase", isVirtual: false));

        RbfxSilentExclusionPass.Apply(model.Compilation);

        Assert.Empty(derived.Bases);
        var warning = Assert.Single(derived.GetWarnings());
        Assert.Contains("dropping base 'Urho3D::BrokenBase'", warning, StringComparison.Ordinal);
        Assert.Contains(baseReason, warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_DropsTemplateInstantiationBaseWithoutWarning()
    {
        var model = new FlagSetTestModel();
        var genericBase = model.AddRecord("Weak");
        genericBase.SetIgnoreReason("record is only forward-declared");
        var derived = model.AddRecord("Derived");
        derived.Bases.Add(new RecordBaseBinding(genericBase.SymbolId, "Weak", "Weak", "Urho3D::Weak<Urho3D::Derived>", isVirtual: false));

        RbfxSilentExclusionPass.Apply(model.Compilation);

        Assert.Empty(derived.GetWarnings());
    }

    private static void AddDropWarning(RecordBinding record, string baseName)
        => record.SetWarning($"dropping base '{baseName}' from record '{record.CppQualifiedName}' because the base record is not available for a coherent managed hierarchy; the derived record will still be generated without that base");

    [Fact]
    public void ApplyIntentionalGaps_RemovesDropWarningForSilentlyExcludedBase()
    {
        var model = new FlagSetTestModel();
        var silentBase = model.AddRecord("NonCopyable");
        silentBase.SetSilentExclusionReason("declaration is excluded on purpose");
        var derived = model.AddRecord("Derived");
        AddDropWarning(derived, "Urho3D::NonCopyable");

        RbfxSilentExclusionPass.ApplyIntentionalGaps(model.Compilation);

        Assert.Empty(derived.GetWarnings());
    }

    [Fact]
    public void ApplyIntentionalGaps_RemovesDropWarningForTemplateInstantiationBase()
    {
        var model = new FlagSetTestModel();
        var derived = model.AddRecord("Derived");
        AddDropWarning(derived, "Urho3D::IDFamily<Urho3D::Derived>");

        RbfxSilentExclusionPass.ApplyIntentionalGaps(model.Compilation);

        Assert.Empty(derived.GetWarnings());
    }

    [Fact]
    public void ApplyIntentionalGaps_RemovesDropWarningForNestedTypeOfTemplateBase()
    {
        var model = new FlagSetTestModel();
        var node = model.AddRecord("Add");
        AddDropWarning(node, "Urho3D::TemplateNode<Urho3D::AddInstance, float>");
        var instance = model.AddRecord("AddInstance");
        AddDropWarning(instance, "Add::InstanceBase");

        RbfxSilentExclusionPass.ApplyIntentionalGaps(model.Compilation);

        Assert.Empty(node.GetWarnings());
        Assert.Empty(instance.GetWarnings());
    }

    [Fact]
    public void ApplyIntentionalGaps_KeepsDropWarningForBaseIgnoredForAnotherReason()
    {
        var model = new FlagSetTestModel();
        var brokenBase = model.AddRecord("BrokenBase");
        brokenBase.SetIgnoreReason("record is only forward-declared");
        var derived = model.AddRecord("Derived");
        AddDropWarning(derived, "Urho3D::BrokenBase");
        AddDropWarning(derived, "Unknown::Base");
        derived.SetWarning("another warning");

        RbfxSilentExclusionPass.ApplyIntentionalGaps(model.Compilation);

        Assert.Equal(3, derived.GetWarnings().Count);
    }
}
