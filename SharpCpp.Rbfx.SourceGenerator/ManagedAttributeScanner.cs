using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpCpp.Rbfx.SourceGenerator;

/// <summary>One engine attribute of a managed type: the data for one ManagedAttributeDescriptor.Create call.</summary>
internal sealed class AttributeEntry
{
    public AttributeEntry(string name, string memberName, string memberIdentifier, string declaringType, string valueType, string bindingType, string mode, ValueKind kind, string? defaultExpression = null)
    {
        Name = name;
        MemberName = memberName;
        MemberIdentifier = memberIdentifier;
        DeclaringType = declaringType;
        ValueType = valueType;
        BindingType = bindingType;
        Mode = mode;
        Kind = kind;
        DefaultExpression = defaultExpression;
    }

    public string Name { get; }

    public string MemberName { get; }

    public string MemberIdentifier { get; }

    public string DeclaringType { get; }

    public string ValueType { get; }

    public string BindingType { get; }

    public string Mode { get; }

    public ValueKind Kind { get; }

    /// <summary>The body of the default delegate, from the declaration of the member. It is null when the default is not emittable.</summary>
    public string? DefaultExpression { get; }
}

internal enum ValueKind
{
    Plain,
    String,
    Enum,
}

/// <summary>
/// Finds the SerializeField members of a managed type with the rules of ManagedAttributeRegistrar.CollectMembers, so that the
/// generated registry can list the attributes without reflection. A type that the scanner cannot describe gets no list, and the
/// run time falls back to reflection for that type.
/// </summary>
internal sealed class ManagedAttributeScanner
{
    internal static readonly DiagnosticDescriptor MemberDiagnostic = new(
        id: "RBFXSG004",
        title: "Managed rbfx attribute members must be describable without reflection",
        messageFormat: "Attribute member '{0}' of type '{1}' cannot be listed in the managed type registry: {2}. The type reads its attributes by reflection, which is not AOT safe.",
        category: "SharpCpp.Rbfx.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private readonly INamedTypeSymbol _serializeField;
    private readonly INamedTypeSymbol _serializable;
    private readonly INamedTypeSymbol? _nonSerialized;
    private readonly IAssemblySymbol _bindingsAssembly;
    private readonly IAssemblySymbol _assembly;
    private readonly AttributeDefaultResolver _defaults;

    // The constructors of the type that Scan works on. The scanner runs in one thread.
    private List<(SemanticModel Model, SyntaxNode Node)> _constructors = new();

    private ManagedAttributeScanner(
        INamedTypeSymbol serializeField,
        INamedTypeSymbol serializable,
        INamedTypeSymbol? nonSerialized,
        IAssemblySymbol bindingsAssembly,
        IAssemblySymbol assembly,
        AttributeDefaultResolver defaults)
    {
        _serializeField = serializeField;
        _serializable = serializable;
        _nonSerialized = nonSerialized;
        _bindingsAssembly = bindingsAssembly;
        _assembly = assembly;
        _defaults = defaults;
    }

    /// <summary>Returns null when the bindings have no SerializeFieldAttribute, so that no type gets a list.</summary>
    public static ManagedAttributeScanner? Create(Compilation compilation, IAssemblySymbol bindingsAssembly)
    {
        var serializeField = compilation.GetTypeByMetadataName("Urho3D.Scene.SerializeFieldAttribute");
        var serializable = compilation.GetTypeByMetadataName("Urho3D.Scene.Serializable");
        if (serializeField is null || serializable is null)
        {
            return null;
        }

        return new ManagedAttributeScanner(
            serializeField,
            serializable,
            compilation.GetTypeByMetadataName("System.NonSerializedAttribute"),
            bindingsAssembly,
            compilation.Assembly,
            new AttributeDefaultResolver(compilation));
    }

    /// <summary>
    /// Returns the attributes in the order of the reflection path, or null when the type must use reflection.
    /// <paramref name="registered"/> holds the types of the registry. The reflection path stops at the first base type that
    /// the engine reflects, and a registered base type is reflected before the derived type.
    /// </summary>
    public List<AttributeEntry>? Scan(SourceProductionContext context, INamedTypeSymbol type, HashSet<INamedTypeSymbol> registered)
    {
        // The members of the type, and of the base types up to the first reflected one or a native binding type.
        var chain = new List<INamedTypeSymbol>();
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (!SymbolEqualityComparer.Default.Equals(current, type) &&
                (registered.Contains(current) || SymbolEqualityComparer.Default.Equals(current.ContainingAssembly, _bindingsAssembly)))
            {
                break;
            }

            chain.Add(current);
        }

        // Base members first, as in the engine order of attributes.
        chain.Reverse();
        _constructors = _defaults.CollectConstructors(chain);
        var entries = new List<AttributeEntry>();
        var describable = true;
        foreach (var current in chain)
        {
            var isLocal = SymbolEqualityComparer.Default.Equals(current.ContainingAssembly, _assembly);
            foreach (var member in current.GetMembers())
            {
                if (member is IFieldSymbol field && !field.IsStatic)
                {
                    describable &= ScanField(context, type, current, isLocal, field, entries);
                }
            }

            foreach (var member in current.GetMembers())
            {
                if (member is IPropertySymbol property && !property.IsStatic)
                {
                    describable &= ScanProperty(context, type, current, isLocal, property, entries);
                }
            }
        }

        if (entries.Count > 0 && !DerivesFrom(type, _serializable))
        {
            Report(context, type.Locations, type.Name, type, "the type is not a Serializable");
            describable = false;
        }

        return describable ? entries : null;
    }

    private bool ScanField(
        SourceProductionContext context, INamedTypeSymbol type, INamedTypeSymbol declaring, bool isLocal, IFieldSymbol field, List<AttributeEntry> entries)
    {
        var marker = FindAttribute(field, _serializeField);
        if (marker is null)
        {
            return true;
        }

        // The reflection path skips a NonSerialized field silently, so the check comes before the check of the assembly.
        if (IsNotSerialized(field))
        {
            return true;
        }

        if (!isLocal)
        {
            return ReportForeign(context, type, declaring, field);
        }

        if (GetObsoleteError(field, declaring) is { } obsolete)
        {
            return Fail(context, type, field, obsolete);
        }

        if (field.AssociatedSymbol is not null)
        {
            return Fail(context, type, field, "the attribute is on the backing field of a property");
        }

        if (field.IsReadOnly)
        {
            return Fail(context, type, field, "it is readonly");
        }

        if (!IsAccessible(field.DeclaredAccessibility) || !IsTypeAccessible(declaring))
        {
            return Fail(context, type, field, "it is private or protected");
        }

        return AddEntry(context, type, declaring, field, field.Type, marker, entries);
    }

    private bool ScanProperty(
        SourceProductionContext context, INamedTypeSymbol type, INamedTypeSymbol declaring, bool isLocal, IPropertySymbol property, List<AttributeEntry> entries)
    {
        var marker = FindAttribute(property, _serializeField);
        if (marker is null)
        {
            return true;
        }

        if (!isLocal)
        {
            return ReportForeign(context, type, declaring, property);
        }

        if (GetObsoleteError(property, declaring) is { } obsolete)
        {
            return Fail(context, type, property, obsolete);
        }

        if (property.GetMethod is null || property.SetMethod is null || property.IsIndexer || property.RefKind != RefKind.None ||
            property.ExplicitInterfaceImplementations.Length > 0)
        {
            return Fail(context, type, property, "it must be a property with a getter and a setter");
        }

        if (property.SetMethod.IsInitOnly)
        {
            return Fail(context, type, property, "its setter is init-only");
        }

        if (!IsAccessible(property.DeclaredAccessibility) || !IsAccessible(property.GetMethod.DeclaredAccessibility) ||
            !IsAccessible(property.SetMethod.DeclaredAccessibility) || !IsTypeAccessible(declaring))
        {
            return Fail(context, type, property, "it, its getter or its setter is private or protected");
        }

        return AddEntry(context, type, declaring, property, property.Type, marker, entries);
    }

    private bool AddEntry(
        SourceProductionContext context,
        INamedTypeSymbol type,
        INamedTypeSymbol declaring,
        ISymbol member,
        ITypeSymbol valueType,
        AttributeData marker,
        List<AttributeEntry> entries)
    {
        if (!TryGetKind(valueType, out var kind))
        {
            return Fail(context, type, member, "its type '" + valueType.ToDisplayString() + "' is not a supported attribute type");
        }

        if (kind == ValueKind.Enum && valueType is INamedTypeSymbol enumType && !IsTypeAccessible(enumType))
        {
            return Fail(context, type, member, "its enum type is private or protected");
        }

        string? name = null;
        var mode = "global::Urho3D.Core.AttributeMode.AmDefault";
        foreach (var argument in marker.NamedArguments)
        {
            if (argument.Key == "Name" && argument.Value.Kind == TypedConstantKind.Primitive)
            {
                name = argument.Value.Value as string;
            }
            else if (argument.Key == "Mode" && argument.Value.Kind == TypedConstantKind.Enum && argument.Value.Value is { } raw)
            {
                mode = "unchecked((global::Urho3D.Core.AttributeMode)" + Convert.ToInt64(raw, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) + "L)";
            }
        }

        var display = valueType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        entries.Add(new AttributeEntry(
            SymbolDisplay.FormatLiteral(name ?? member.Name, quote: true),
            SymbolDisplay.FormatLiteral(member.Name, quote: true),
            SyntaxFacts.GetKeywordKind(member.Name) != SyntaxKind.None ? "@" + member.Name : member.Name,
            declaring.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            display,
            kind == ValueKind.Enum ? "object" : display,
            mode,
            kind,
            _defaults.TryGetDefault(member, valueType, kind, _constructors)));
        return true;
    }

    // Value types of VariantConverters. An enum with a 32-bit underlying type is listed as object.
    private static bool TryGetKind(ITypeSymbol type, out ValueKind kind)
    {
        kind = ValueKind.Plain;
        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean:
            case SpecialType.System_Int32:
            case SpecialType.System_Int64:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
                return true;
            case SpecialType.System_String:
                kind = ValueKind.String;
                return true;
        }

        if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol { EnumUnderlyingType: { } underlying } &&
            underlying.SpecialType is SpecialType.System_Int32 or SpecialType.System_UInt32)
        {
            kind = ValueKind.Enum;
            return true;
        }

        if (type.TypeKind == TypeKind.Struct && type.ContainingNamespace.ToDisplayString() == "Urho3D.Math")
        {
            switch (type.Name)
            {
                case "Vector2":
                case "Vector3":
                case "Vector4":
                case "Quaternion":
                case "Color":
                case "IntVector2":
                case "IntVector3":
                    return true;
            }
        }

        return false;
    }

    // NonSerialized is a pseudo attribute: a field from metadata has the flag, and no attribute data. The public API of Roslyn
    // does not show the flag, so the lookup reads the internal property. When it is not there, the field counts as serialized.
    private bool IsNotSerialized(IFieldSymbol field)
    {
        if (_nonSerialized is not null && FindAttribute(field, _nonSerialized) is not null)
        {
            return true;
        }

        try
        {
            var underlying = field.GetType().GetProperty("UnderlyingSymbol", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(field);
            var flag = underlying?.GetType().GetProperty("IsNotSerialized", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(underlying);
            return flag is true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // Generated code that uses a member marked Obsolete(error: true) fails with CS0619, and the pragma of the generated code covers only CS0618.
    internal static string? GetObsoleteError(ISymbol member, INamedTypeSymbol declaring)
    {
        if (IsObsoleteError(member))
        {
            return "it is marked Obsolete with error: true";
        }

        for (var current = declaring; current is not null; current = current.ContainingType)
        {
            if (IsObsoleteError(current))
            {
                return "its declaring type '" + current.Name + "' is marked Obsolete with error: true";
            }
        }

        return null;
    }

    internal static bool IsObsoleteError(ISymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass is { Name: "ObsoleteAttribute" } attributeClass &&
                attributeClass.ContainingNamespace.ToDisplayString() == "System" &&
                attribute.ConstructorArguments.Length == 2 &&
                attribute.ConstructorArguments[1].Value is true)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ReportForeign(SourceProductionContext context, INamedTypeSymbol type, INamedTypeSymbol declaring, ISymbol member)
    {
        Report(context, type.Locations, member.Name, type, "its base type '" + declaring.ToDisplayString() + "' is in another assembly");
        return false;
    }

    private static bool Fail(SourceProductionContext context, INamedTypeSymbol type, ISymbol member, string reason)
    {
        Report(context, member.Locations, member.Name, type, reason);
        return false;
    }

    private static void Report(SourceProductionContext context, IReadOnlyList<Location> locations, string memberName, INamedTypeSymbol type, string reason)
    {
        context.ReportDiagnostic(Diagnostic.Create(
            MemberDiagnostic,
            locations.Count > 0 ? locations[0] : Location.None,
            memberName,
            type.ToDisplayString(),
            reason));
    }

    private static AttributeData? FindAttribute(ISymbol symbol, INamedTypeSymbol attributeType)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType))
            {
                return attribute;
            }
        }

        return null;
    }

    private static bool IsAccessible(Accessibility accessibility) =>
        accessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal;

    private static bool IsTypeAccessible(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (!IsAccessible(current.DeclaredAccessibility))
            {
                return false;
            }
        }

        return true;
    }

    private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }
        }

        return false;
    }
}
