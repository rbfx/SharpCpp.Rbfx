using SharpCpp.Model.Ast;

namespace SharpCpp.Rbfx.Generator.Tests;

/// <summary>Builds a small compilation with the MouseButton enum and the FlagSet class template.</summary>
internal sealed class FlagSetTestModel
{
    public const string EnumId = "enum:Urho3D::MouseButton";
    public const string FlagSetId = "record:Urho3D::FlagSet";
    public const string LoweredEnumName = "global::Urho3D.MouseButton";

    public FlagSetTestModel()
    {
        Compilation = new BindingCompilation("Test", "Urho3D", "TestBindings", "TestNative", Array.Empty<string>());

        Enum = new EnumBinding(EnumId, "MouseButton", "MouseButton", cppQualifiedName: "Urho3D::MouseButton");
        Compilation.Members.Add(Enum);

        FlagSet = new RecordBinding(FlagSetId, "FlagSet", "FlagSet", "Urho3D::FlagSet");
        FlagSet.HeaderIncludePath = "Urho3D/Container/FlagSet.h";
        Compilation.Members.Add(FlagSet);
    }

    public BindingCompilation Compilation { get; }

    public EnumBinding Enum { get; }

    public RecordBinding FlagSet { get; }

    public BindingType EnumType => BindingType.CreateEnum("Urho3D::MouseButton", "MouseButton", enumId: EnumId);

    public BindingType FlagSetType => BindingType.CreateHandle(
        "Urho3D::FlagSet<Urho3D::MouseButton>",
        "FlagSet<MouseButton>",
        recordId: FlagSetId,
        genericArguments: new[] { EnumType });

    public RecordBinding AddRecord(string name)
    {
        var record = new RecordBinding($"record:Urho3D::{name}", name, name, $"Urho3D::{name}");
        Compilation.Members.Add(record);
        return record;
    }

    public FunctionBinding AddFunction(
        BindingNode parent,
        string name,
        BindingType returnType,
        params BindingType[] parameterTypes)
    {
        var function = new FunctionBinding($"function:{name}", name, name, returnType, cppQualifiedName: $"Urho3D::{name}");
        for (var i = 0; i < parameterTypes.Length; i++)
        {
            function.Parameters.Add(new ParameterBinding($"param:{name}:{i}", $"arg{i}", $"arg{i}", parameterTypes[i]));
        }

        ((BindingContainerNode)parent).Members.Add(function);
        return function;
    }
}
