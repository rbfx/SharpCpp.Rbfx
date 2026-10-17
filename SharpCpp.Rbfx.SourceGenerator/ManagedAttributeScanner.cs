using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpCpp.Rbfx.SourceGenerator;

/// <summary>One engine attribute of a managed type: the data for one ManagedAttributeDescriptor.Create call.</summary>
internal sealed class AttributeEntry
{
    public const string ValuePlaceholder = "{VALUE}";

    public AttributeEntry(
        string name,
        string memberName,
        string memberIdentifier,
        string declaringType,
        string valueType,
        string bindingType,
        string mode,
        ValueKind kind,
        string defaultExpression,
        string readAccess,
        string writeTemplate,
        IReadOnlyList<string> accessors)
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
        ReadAccess = readAccess;
        WriteTemplate = writeTemplate;
        Accessors = accessors;
    }

    public string Name { get; }

    public string MemberName { get; }

    public string MemberIdentifier { get; }

    public string DeclaringType { get; }

    public string ValueType { get; }

    public string BindingType { get; }

    public string Mode { get; }

    public ValueKind Kind { get; }

    /// <summary>The body of the default delegate, from the declaration of the member.</summary>
    public string DefaultExpression { get; }

    /// <summary>The expression that reads the member of the owner <c>o</c>.</summary>
    public string ReadAccess { get; }

    /// <summary>The expression that writes the member of the owner <c>o</c>. <see cref="ValuePlaceholder"/> stands for the new value.</summary>
    public string WriteTemplate { get; }

    /// <summary>The declarations of the UnsafeAccessor methods that the member needs. The list is empty for a member with direct access.</summary>
    public IReadOnlyList<string> Accessors { get; }
}

internal enum ValueKind
{
    Plain,
    String,
    Enum,
}

/// <summary>
/// Finds the SerializeField members of a managed type, so that the generated registry lists the attributes without reflection.
/// Every member that the scanner cannot describe is an error (RBFXSG004).
///
/// A member that the registry file can see (public or internal, in a type that is public or internal) is read and written
/// directly. A private or protected member is read and written through an UnsafeAccessor method of the registry class:
/// a field gets a field accessor that returns a reference, a property gets one accessor for the getter and one for the setter.
/// The members of a generic type get a generic accessor with the type parameters of the type (.NET 9 and later).
/// UnsafeAccessor needs a declaring type that the registry file can name, and a non-virtual member. Else the scanner reports an error.
/// </summary>
internal sealed class ManagedAttributeScanner
{
    internal static readonly DiagnosticDescriptor MemberDiagnostic = new(
        id: "RBFXSG004",
        title: "Managed rbfx attribute members must be describable by the source generator",
        messageFormat: "Attribute member '{0}' of type '{1}' cannot be listed in the managed type registry: {2}",
        category: "SharpCpp.Rbfx.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private const string AccessorAttribute = "global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.";

    private readonly INamedTypeSymbol _serializeField;
    private readonly INamedTypeSymbol _serializable;
    private readonly INamedTypeSymbol? _nonSerialized;
    private readonly IAssemblySymbol _bindingsAssembly;
    private readonly IAssemblySymbol _assembly;
    private readonly AttributeDefaultResolver _defaults;
    private int _accessorCount;

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

    /// <summary>Returns null when the bindings have no SerializeFieldAttribute, so that no type has attributes.</summary>
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
    /// Returns the attributes of the type: its own members, and the members of the base types that have no reflection of their own.
    /// A member that the scanner cannot describe gets an error and is left out.
    /// <paramref name="registered"/> holds the types of the registry. The chain stops at the first base type that
    /// the engine reflects, and a registered base type is reflected before the derived type.
    /// </summary>
    public List<AttributeEntry> Scan(SourceProductionContext context, INamedTypeSymbol type, HashSet<INamedTypeSymbol> registered)
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
        foreach (var current in chain)
        {
            var isLocal = SymbolEqualityComparer.Default.Equals(current.ContainingAssembly, _assembly);
            foreach (var member in current.GetMembers())
            {
                if (member is IFieldSymbol field && !field.IsStatic)
                {
                    ScanField(context, type, current, isLocal, field, entries);
                }
            }

            foreach (var member in current.GetMembers())
            {
                if (member is IPropertySymbol property && !property.IsStatic)
                {
                    ScanProperty(context, type, current, isLocal, property, entries);
                }
            }
        }

        if (entries.Count > 0 && !DerivesFrom(type, _serializable))
        {
            Report(context, type.Locations, type.Name, type, "the type is not a Serializable. Derive it from Serializable or Component, or remove the SerializeField attributes");
        }

        return entries;
    }

    private void ScanField(
        SourceProductionContext context, INamedTypeSymbol type, INamedTypeSymbol declaring, bool isLocal, IFieldSymbol field, List<AttributeEntry> entries)
    {
        var marker = FindAttribute(field, _serializeField);
        if (marker is null)
        {
            return;
        }

        // A NonSerialized field is skipped silently, so the check comes before the check of the assembly.
        if (IsNotSerialized(field))
        {
            return;
        }

        if (!isLocal)
        {
            ReportForeign(context, type, declaring, field);
            return;
        }

        if (GetObsoleteError(field, declaring) is { } obsolete)
        {
            Fail(context, type, field, obsolete);
            return;
        }

        if (field.AssociatedSymbol is not null)
        {
            Fail(context, type, field, "the attribute is on the backing field of a property. Put the attribute on the property");
            return;
        }

        if (field.IsReadOnly)
        {
            Fail(context, type, field, "it is readonly. The engine writes the attribute, so remove readonly");
            return;
        }

        var direct = IsAccessible(field.DeclaredAccessibility) && IsTypeAccessible(declaring);
        if (!direct && !CanUseAccessor(context, type, field, declaring))
        {
            return;
        }

        AddEntry(context, type, declaring, field, field.Type, marker, direct, entries);
    }

    private void ScanProperty(
        SourceProductionContext context, INamedTypeSymbol type, INamedTypeSymbol declaring, bool isLocal, IPropertySymbol property, List<AttributeEntry> entries)
    {
        var marker = FindAttribute(property, _serializeField);
        if (marker is null)
        {
            return;
        }

        if (!isLocal)
        {
            ReportForeign(context, type, declaring, property);
            return;
        }

        if (GetObsoleteError(property, declaring) is { } obsolete)
        {
            Fail(context, type, property, obsolete);
            return;
        }

        if (property.GetMethod is null || property.SetMethod is null || property.IsIndexer || property.RefKind != RefKind.None ||
            property.ExplicitInterfaceImplementations.Length > 0)
        {
            Fail(context, type, property, "it must be a property with a getter and a setter");
            return;
        }

        if (property.SetMethod.IsInitOnly)
        {
            Fail(context, type, property, "its setter is init-only. Use a normal setter");
            return;
        }

        var direct = IsAccessible(property.DeclaredAccessibility) && IsAccessible(property.GetMethod.DeclaredAccessibility) &&
                     IsAccessible(property.SetMethod.DeclaredAccessibility) && IsTypeAccessible(declaring);
        if (!direct && !CanUseAccessor(context, type, property, declaring))
        {
            return;
        }

        AddEntry(context, type, declaring, property, property.Type, marker, direct, entries);
    }

    // An UnsafeAccessor method names the declaring type in its signature, and it calls the member without a virtual dispatch.
    private bool CanUseAccessor(SourceProductionContext context, INamedTypeSymbol type, ISymbol member, INamedTypeSymbol declaring)
    {
        if (!IsTypeAccessible(declaring))
        {
            Fail(context, type, member, "its declaring type '" + declaring.Name + "' is private or protected, and the generated registry cannot name it. Make the declaring type internal or public");
            return false;
        }

        if (member.IsVirtual || member.IsOverride || member.IsAbstract)
        {
            Fail(context, type, member, "it is private or protected and virtual, and UnsafeAccessor cannot call a virtual member. Make it public or internal, or not virtual");
            return false;
        }

        for (var current = declaring; current is not null; current = current.ContainingType)
        {
            if (current.TypeParameters.Any(static parameter =>
                    parameter.HasReferenceTypeConstraint || parameter.HasValueTypeConstraint || parameter.HasNotNullConstraint ||
                    parameter.HasUnmanagedTypeConstraint || parameter.HasConstructorConstraint || parameter.ConstraintTypes.Length > 0))
            {
                Fail(context, type, member, "it is private or protected in the generic type '" + current.Name + "' with type parameter constraints, and UnsafeAccessor cannot reach it. Make the member public or internal");
                return false;
            }
        }

        return true;
    }

    private void AddEntry(
        SourceProductionContext context,
        INamedTypeSymbol type,
        INamedTypeSymbol declaring,
        ISymbol member,
        ITypeSymbol valueType,
        AttributeData marker,
        bool direct,
        List<AttributeEntry> entries)
    {
        if (!TryGetKind(valueType, out var kind))
        {
            Fail(context, type, member, "its type '" + valueType.ToDisplayString() + "' is not a supported attribute type. Use bool, int, long, float, double, string, an enum with a 32-bit underlying type, or a Urho3D.Math vector, quaternion or color");
            return;
        }

        if (kind == ValueKind.Enum && valueType is INamedTypeSymbol enumType && !IsTypeAccessible(enumType))
        {
            Fail(context, type, member, "its enum type is private or protected, and the generated registry cannot name it. Make the enum internal or public");
            return;
        }

        var defaultExpression = _defaults.TryGetDefault(member, valueType, kind, _constructors, out var defaultReason);
        if (defaultExpression is null)
        {
            Fail(context, type, member, "its default value cannot be read without an instance: " + defaultReason +
                ". Give the member a default with a literal, a constant or a static initializer (for example '= 2.5f;'), and do not assign it in a constructor");
            return;
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
        var declaringDisplay = declaring.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var identifier = SyntaxFacts.GetKeywordKind(member.Name) != SyntaxKind.None ? "@" + member.Name : member.Name;
        string readAccess;
        string writeTemplate;
        var accessors = new List<string>();
        if (direct)
        {
            readAccess = "((" + declaringDisplay + ")o)." + identifier;
            writeTemplate = readAccess + " = " + AttributeEntry.ValuePlaceholder;
        }
        else
        {
            var index = _accessorCount++;
            var typeParameters = GetTypeParameterList(declaring.OriginalDefinition);
            var typeArguments = GetTypeArgumentList(declaring);
            var ownerType = declaring.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var memberType = (member is IFieldSymbol field ? field.OriginalDefinition.Type : ((IPropertySymbol)member).OriginalDefinition.Type)
                .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var owner = "((" + declaringDisplay + ")o)";
            if (member is IFieldSymbol)
            {
                var accessor = "__Field" + index;
                accessors.Add(
                    "[" + AccessorAttribute + "Field, Name = " + SymbolDisplay.FormatLiteral(member.Name, quote: true) + ")]\n" +
                    "private static extern ref " + memberType + " " + accessor + typeParameters + "(" + ownerType + " owner);");
                readAccess = accessor + typeArguments + "(" + owner + ")";
                writeTemplate = readAccess + " = " + AttributeEntry.ValuePlaceholder;
            }
            else
            {
                var property = (IPropertySymbol)member;
                var getter = "__Get" + index;
                var setter = "__Set" + index;
                accessors.Add(
                    "[" + AccessorAttribute + "Method, Name = " + SymbolDisplay.FormatLiteral(property.GetMethod!.Name, quote: true) + ")]\n" +
                    "private static extern " + memberType + " " + getter + typeParameters + "(" + ownerType + " owner);");
                accessors.Add(
                    "[" + AccessorAttribute + "Method, Name = " + SymbolDisplay.FormatLiteral(property.SetMethod!.Name, quote: true) + ")]\n" +
                    "private static extern void " + setter + typeParameters + "(" + ownerType + " owner, " + memberType + " value);");
                readAccess = getter + typeArguments + "(" + owner + ")";
                writeTemplate = setter + typeArguments + "(" + owner + ", " + AttributeEntry.ValuePlaceholder + ")";
            }
        }

        entries.Add(new AttributeEntry(
            SymbolDisplay.FormatLiteral(name ?? member.Name, quote: true),
            SymbolDisplay.FormatLiteral(member.Name, quote: true),
            identifier,
            declaringDisplay,
            display,
            kind == ValueKind.Enum ? "object" : display,
            mode,
            kind,
            defaultExpression,
            readAccess,
            writeTemplate,
            accessors));
    }

    // The type parameters of the type and of its containing types, outermost first: the order of an UnsafeAccessor method of a generic type.
    private static List<ITypeSymbol> GetTypeParameters(INamedTypeSymbol type)
    {
        var result = new List<ITypeSymbol>();
        for (var current = type; current is not null; current = current.ContainingType)
        {
            result.InsertRange(0, current.TypeParameters);
        }

        return result;
    }

    private static List<ITypeSymbol> GetTypeArguments(INamedTypeSymbol type)
    {
        var result = new List<ITypeSymbol>();
        for (var current = type; current is not null; current = current.ContainingType)
        {
            result.InsertRange(0, current.TypeArguments);
        }

        return result;
    }

    private static string GetTypeParameterList(INamedTypeSymbol type)
    {
        var parameters = GetTypeParameters(type);
        return parameters.Count == 0 ? string.Empty : "<" + string.Join(", ", parameters.Select(static parameter => parameter.Name)) + ">";
    }

    private static string GetTypeArgumentList(INamedTypeSymbol type)
    {
        var arguments = GetTypeArguments(type);
        return arguments.Count == 0
            ? string.Empty
            : "<" + string.Join(", ", arguments.Select(static argument => argument.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))) + ">";
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
        Report(context, type.Locations, member.Name, type, "its base type '" + declaring.ToDisplayString() + "' is in another assembly, and the generator lists only the members of the types of this assembly. Declare the member in a type of this assembly");
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
