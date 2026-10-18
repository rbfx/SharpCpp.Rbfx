using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace SharpCpp.Rbfx.SourceGenerator;

[Generator(LanguageNames.CSharp)]
public sealed class ManagedObjectRttiGenerator : IIncrementalGenerator
{
    private const string GeneratedOverrideHookName = "IsGeneratedManagedOverride";

    private static readonly string[] RttiMethodNames = ["GetType", "GetTypeName", "GetTypeInfo", "IsInstanceOf"];

    private static readonly DiagnosticDescriptor PartialRequiredDiagnostic = new(
        id: "RBFXSG001",
        title: "Managed rbfx object types must be partial",
        messageFormat: "Type '{0}' derives from Urho3D.Core.Object and needs generated RTTI overrides; mark it partial or implement the RTTI members manually",
        category: "SharpCpp.Rbfx.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor TypeNameCollisionDiagnostic = new(
        id: "RBFXSG002",
        title: "Managed rbfx RTTI names must be unique",
        messageFormat: "Type '{0}' cannot use generated rbfx RTTI because the type name '{1}' already belongs to another Object-derived type",
        category: "SharpCpp.Rbfx.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor RegistryAccessibilityDiagnostic = new(
        id: "RBFXSG003",
        title: "Managed rbfx object types must be visible to the type registry",
        messageFormat: "Type '{0}' is not in the managed type registry because it or a containing type is private, protected or private protected, and the registry is the only way to register a type. Make it internal or public.",
        category: "SharpCpp.Rbfx.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: new[] { WellKnownDiagnosticTags.NotConfigurable });

    private static readonly DiagnosticDescriptor FactoryReflectionTargetDiagnostic = new(
        id: "RBFXSG005",
        title: "FactoryReflection is on a type that cannot be registered",
        messageFormat: "Type '{0}' has [FactoryReflection], but it cannot be registered: {1}",
        category: "SharpCpp.Rbfx.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: new[] { WellKnownDiagnosticTags.NotConfigurable });

    private static readonly DiagnosticDescriptor FactoryReflectionCategoryDiagnostic = new(
        id: "RBFXSG006",
        title: "FactoryReflection category must not be empty",
        messageFormat: "Type '{0}' has [FactoryReflection] with an empty Category. Set a name, or leave Category unset to use the category of the plugin.",
        category: "SharpCpp.Rbfx.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: new[] { WellKnownDiagnosticTags.NotConfigurable });

    private static readonly DiagnosticDescriptor UnmarkedBaseDiagnostic = new(
        id: "RBFXSG007",
        title: "Base type of a registered type is not registered",
        messageFormat: "Type '{0}' has [FactoryReflection], but its base type '{1}' in the same assembly has not. The engine has no reflection of '{1}'. The attributes of '{1}' are listed with '{0}'. Add [FactoryReflection] to '{1}' to register it.",
        category: "SharpCpp.Rbfx.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnmarkedTypeDiagnostic = new(
        id: "RBFXSG008",
        title: "Managed rbfx object type is not registered",
        messageFormat: "Type '{0}' is not registered in the engine, because it has no [FactoryReflection]. Add [FactoryReflection] to register it.",
        category: "SharpCpp.Rbfx.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    internal const string FactoryReflectionMetadataName = "Urho3D.Plugins.FactoryReflectionAttribute";
    internal const string RegistryAttributeMetadataName = "Urho3D.Plugins.ManagedTypeRegistryAttribute";
    internal const string RegistryNamespace = "SharpCpp.Rbfx.Generated";
    internal const string RegistryClassName = "GeneratedManagedTypeRegistryAttribute";
    internal const string RegistryHintName = "SharpCpp.Rbfx.ManagedTypeRegistry.g.cs";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var typeDeclarations = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => node is ClassDeclarationSyntax { BaseList.Types.Count: > 0 } or ClassDeclarationSyntax { AttributeLists.Count: > 0 },
            static (syntaxContext, cancellationToken) => syntaxContext.SemanticModel.GetDeclaredSymbol((ClassDeclarationSyntax)syntaxContext.Node, cancellationToken) as INamedTypeSymbol)
            .Where(static symbol => symbol is not null);

        var inputs = context.CompilationProvider.Combine(typeDeclarations.Collect());
        context.RegisterSourceOutput(inputs, static (productionContext, input) =>
        {
            Execute(productionContext, input.Left, input.Right);
        });
    }

    private static void Execute(SourceProductionContext context, Compilation compilation, ImmutableArray<INamedTypeSymbol?> discoveredSymbols)
    {
        var objectSymbol = compilation.GetTypeByMetadataName("Urho3D.Core.Object");
        var stringHashSymbol = compilation.GetTypeByMetadataName("Urho3D.Math.StringHash");
        var nativeObjectRegistrationSymbol = compilation.GetTypeByMetadataName("SharpCpp.Support.INativeObjectRegistration`1");
        var contextSymbol = compilation.GetTypeByMetadataName("Urho3D.Core.Context");
        var objectFactorySymbol = compilation.GetTypeByMetadataName("SharpCpp.Rbfx.Bindings.Interop.IManagedObjectFactory`1");
        if (objectSymbol is null || stringHashSymbol is null || nativeObjectRegistrationSymbol is null)
        {
            return;
        }

        var uniqueSymbols = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var discoveredSymbol in discoveredSymbols)
        {
            if (discoveredSymbol is not null)
            {
                uniqueSymbols.Add(discoveredSymbol);
            }
        }

        var factoryTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var reportedTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var typeSymbol in uniqueSymbols)
        {
            if (objectFactorySymbol is not null && ImplementsFactoryForItself(typeSymbol, objectFactorySymbol))
            {
                factoryTypes.Add(typeSymbol);
            }

            if (!TryCreateCandidate(context, typeSymbol, objectSymbol, stringHashSymbol, nativeObjectRegistrationSymbol, reportedTypes, out var candidate) ||
                candidate is null)
            {
                continue;
            }

            if (HasTypeNameCollision(compilation, candidate.Symbol, candidate.TypeName, objectSymbol))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    TypeNameCollisionDiagnostic,
                    candidate.Location,
                    candidate.Symbol.ToDisplayString(),
                    candidate.TypeName));
                reportedTypes.Add(candidate.Symbol);
                continue;
            }

            candidate.ImplementsObjectFactory = objectFactorySymbol is not null &&
                contextSymbol is not null &&
                !candidate.Symbol.IsAbstract &&
                !ImplementsInterface(candidate.Symbol, objectFactorySymbol) &&
                HasContextConstructor(candidate.Symbol, contextSymbol);

            if (candidate.ImplementsObjectFactory)
            {
                factoryTypes.Add(candidate.Symbol);
            }

            candidate.SlotKeys = CollectRttiSlotKeys(candidate.Symbol, objectSymbol);

            context.AddSource(GetHintName(candidate), SourceText.From(Render(candidate), Encoding.UTF8));
        }

        // An assembly with a ManagedPlugin has a registry even without managed object types, so that the plugin finds it.
        var managedPluginSymbol = compilation.GetTypeByMetadataName("Urho3D.Plugins.ManagedPlugin");
        var hasManagedPlugin = false;
        if (managedPluginSymbol is not null)
        {
            foreach (var symbol in uniqueSymbols)
            {
                if (symbol.TypeKind == TypeKind.Class && !symbol.IsAbstract && DerivesFrom(symbol, managedPluginSymbol))
                {
                    hasManagedPlugin = true;
                    break;
                }
            }
        }

        EmitTypeRegistry(context, compilation, objectSymbol, uniqueSymbols, factoryTypes, reportedTypes, hasManagedPlugin);
    }

    // Emits the ManagedTypeRegistryAttribute of the assembly: one AOT safe registration for each marked managed type.
    // A type is listed only when it has FactoryReflectionAttribute and passes the rules of ManagedPlugin.ShouldRegister
    // (ManagedObjectFactory.IsFactoryType, not a Plugin). ShouldRegister filters the list again at run time.
    private static void EmitTypeRegistry(
        SourceProductionContext context,
        Compilation compilation,
        INamedTypeSymbol objectSymbol,
        HashSet<INamedTypeSymbol> allTypes,
        HashSet<INamedTypeSymbol> factoryTypes,
        HashSet<INamedTypeSymbol> reportedTypes,
        bool hasManagedPlugin)
    {
        var registrySymbol = compilation.GetTypeByMetadataName(RegistryAttributeMetadataName);
        if (registrySymbol is null || SymbolEqualityComparer.Default.Equals(registrySymbol.ContainingAssembly, compilation.Assembly))
        {
            return;
        }

        var pluginSymbol = compilation.GetTypeByMetadataName("Urho3D.Plugins.Plugin");
        var factoryReflectionSymbol = compilation.GetTypeByMetadataName(FactoryReflectionMetadataName);
        var categories = new Dictionary<INamedTypeSymbol, string?>(SymbolEqualityComparer.Default);
        var invalid = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        if (factoryReflectionSymbol is not null)
        {
            ValidateFactoryReflection(
                context, compilation, objectSymbol, pluginSymbol, factoryReflectionSymbol, allTypes, factoryTypes, reportedTypes, hasManagedPlugin, categories, invalid);
        }

        var types = new List<INamedTypeSymbol>();
        foreach (var typeSymbol in factoryTypes)
        {
            if (!categories.ContainsKey(typeSymbol) || invalid.Contains(typeSymbol))
            {
                continue;
            }

            switch (ClassifyRegistryType(typeSymbol, objectSymbol, pluginSymbol))
            {
                case RegistryTypeKind.Included:
                    types.Add(typeSymbol);
                    break;
                case RegistryTypeKind.Inaccessible:
                    context.ReportDiagnostic(Diagnostic.Create(RegistryAccessibilityDiagnostic, GetLocation(typeSymbol), typeSymbol.ToDisplayString()));
                    break;
            }
        }

        if (types.Count == 0 && !hasManagedPlugin)
        {
            return;
        }

        types.Sort(static (left, right) =>
        {
            var depth = GetDepth(left).CompareTo(GetDepth(right));
            return depth != 0 ? depth : string.CompareOrdinal(GetMetadataFullName(left), GetMetadataFullName(right));
        });

        // Every registered type has an attribute list. A member that the scanner cannot describe is an error.
        var attributes = new Dictionary<INamedTypeSymbol, List<AttributeEntry>>(SymbolEqualityComparer.Default);
        if (ManagedAttributeScanner.Create(compilation, registrySymbol.ContainingAssembly) is { } scanner)
        {
            var registered = new HashSet<INamedTypeSymbol>(types, SymbolEqualityComparer.Default);
            foreach (var typeSymbol in types)
            {
                attributes.Add(typeSymbol, scanner.Scan(context, typeSymbol, registered));
            }
        }

        context.AddSource(RegistryHintName, SourceText.From(RenderTypeRegistry(types, attributes, categories), Encoding.UTF8));
    }

    internal static string RenderTypeRegistry(
        IReadOnlyList<INamedTypeSymbol> types,
        IReadOnlyDictionary<INamedTypeSymbol, List<AttributeEntry>> attributes,
        IReadOnlyDictionary<INamedTypeSymbol, string?> categories)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("#nullable enable");
        builder.AppendLine("#pragma warning disable CS0618 // Members can be obsolete.");
        builder.AppendLine();
        builder.Append("[assembly: global::").Append(RegistryNamespace).Append('.').Append(RegistryClassName).AppendLine("]");
        builder.AppendLine();
        builder.Append("namespace ").AppendLine(RegistryNamespace);
        builder.AppendLine("{");
        builder.AppendLine("    /// <summary>The managed object types of this assembly. ManagedPlugin registers them without an assembly scan.</summary>");
        builder.AppendLine("    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        builder.Append("    internal sealed class ").Append(RegistryClassName).Append(" : global::").AppendLine(RegistryAttributeMetadataName);
        builder.AppendLine("    {");
        builder.AppendLine("        private static readonly global::Urho3D.Plugins.ManagedTypeRegistration[] Items = new global::Urho3D.Plugins.ManagedTypeRegistration[]");
        builder.AppendLine("        {");
        foreach (var type in types)
        {
            var display = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            builder.Append("            new(typeof(").Append(display)
                .Append("), static (context, category) => global::SharpCpp.Rbfx.Bindings.Interop.ManagedObjectFactory.RegisterFactoryAndGetTypeInfo<")
                .Append(display).Append(">(context, category)");
            RenderAttributes(builder, attributes.TryGetValue(type, out var entries) ? entries : new List<AttributeEntry>());
            if (categories.TryGetValue(type, out var category) && category is not null)
            {
                builder.Append(", Category: ").Append(Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(category, quote: true));
            }

            builder.AppendLine("),");
        }

        builder.AppendLine("        };");
        builder.AppendLine();
        builder.AppendLine("        public override global::System.Collections.Generic.IReadOnlyList<global::Urho3D.Plugins.ManagedTypeRegistration> Registrations => Items;");

        // The UnsafeAccessor methods of the private and protected members.
        foreach (var accessor in attributes.Values.SelectMany(static list => list).SelectMany(static entry => entry.Accessors))
        {
            builder.AppendLine();
            foreach (var line in accessor.Split('\n'))
            {
                builder.Append("        ").AppendLine(line);
            }
        }
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    // The complete attribute list of a type. The lambdas are static, and they cast the owner to the declaring type of the member.
    private static void RenderAttributes(StringBuilder builder, List<AttributeEntry> entries)
    {
        const string DescriptorType = "global::Urho3D.Plugins.ManagedAttributeDescriptor";
        if (entries.Count == 0)
        {
            builder.Append(", global::System.Array.Empty<").Append(DescriptorType).Append(">()");
            return;
        }

        builder.AppendLine(", new " + DescriptorType + "[]");
        builder.AppendLine("            {");
        foreach (var entry in entries)
        {
            var read = entry.Kind switch
            {
                ValueKind.Enum => "(object)" + entry.ReadAccess,
                ValueKind.String => entry.ReadAccess + "!",
                _ => entry.ReadAccess,
            };
            var write = entry.WriteTemplate.Replace(AttributeEntry.ValuePlaceholder, entry.Kind == ValueKind.Enum ? "(" + entry.ValueType + ")v" : "v");
            builder.Append("                ").Append(DescriptorType).Append(".Create<").Append(entry.BindingType).Append(">(")
                .Append(entry.Name).Append(", ").Append(entry.MemberName).Append(", typeof(").Append(entry.ValueType).Append("), ")
                .Append(entry.Mode).Append(", static o => ").Append(read).Append(", static (o, v) => ").Append(write)
                .Append(", static () => ").Append(entry.DefaultExpression);

            builder.AppendLine("),");
        }

        builder.Append("            }");
    }

    private enum RegistryTypeKind
    {
        Included,
        Excluded,
        Inaccessible,
    }

    private static RegistryTypeKind ClassifyRegistryType(INamedTypeSymbol typeSymbol, INamedTypeSymbol objectSymbol, INamedTypeSymbol? pluginSymbol)
    {
        if (typeSymbol.TypeKind != TypeKind.Class || typeSymbol.IsAbstract || typeSymbol.IsFileLocal ||
            !DerivesFrom(typeSymbol, objectSymbol) ||
            (pluginSymbol is not null && DerivesFromOrIs(typeSymbol, pluginSymbol)))
        {
            return RegistryTypeKind.Excluded;
        }

        // Generic types are not registered.
        for (var current = typeSymbol; current is not null; current = current.ContainingType)
        {
            if (current.TypeParameters.Length > 0)
            {
                return RegistryTypeKind.Excluded;
            }
        }

        // The generated class must see the type, so private and protected nested types are left out.
        for (var current = typeSymbol; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal))
            {
                return RegistryTypeKind.Inaccessible;
            }
        }

        return RegistryTypeKind.Included;
    }

    // True when the registry of the assembly of the type lists the type. The scanner uses this for a base type from metadata.
    // The registry lists a type that has FactoryReflectionAttribute, passes ClassifyRegistryType and implements IManagedObjectFactory
    // for itself. The factory is generated or written by hand, so the type needs no public constructor with a Context parameter.
    internal static bool IsListedByRegistry(
        INamedTypeSymbol typeSymbol,
        INamedTypeSymbol objectSymbol,
        INamedTypeSymbol? pluginSymbol,
        INamedTypeSymbol objectFactorySymbol,
        INamedTypeSymbol factoryReflectionSymbol) =>
        HasFactoryReflection(typeSymbol, factoryReflectionSymbol, out _) &&
        ClassifyRegistryType(typeSymbol, objectSymbol, pluginSymbol) == RegistryTypeKind.Included &&
        ImplementsFactoryForItself(typeSymbol, objectFactorySymbol);

    // Finds FactoryReflectionAttribute on the type itself. The attribute is not inherited. A null category is the plugin category.
    private static bool HasFactoryReflection(INamedTypeSymbol typeSymbol, INamedTypeSymbol factoryReflectionSymbol, out string? category)
    {
        category = null;
        foreach (var attribute in typeSymbol.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, factoryReflectionSymbol))
            {
                continue;
            }

            foreach (var argument in attribute.NamedArguments)
            {
                if (argument.Key == "Category")
                {
                    category = argument.Value.Value as string;
                }
            }

            return true;
        }

        return false;
    }

    // Checks each type with FactoryReflectionAttribute of the compilation. A valid type goes to categories (null is the plugin category),
    // an invalid type goes to invalid. The checks also give the migration warning for a type that now needs the attribute.
    private static void ValidateFactoryReflection(
        SourceProductionContext context,
        Compilation compilation,
        INamedTypeSymbol objectSymbol,
        INamedTypeSymbol? pluginSymbol,
        INamedTypeSymbol factoryReflectionSymbol,
        HashSet<INamedTypeSymbol> allTypes,
        HashSet<INamedTypeSymbol> factoryTypes,
        HashSet<INamedTypeSymbol> reportedTypes,
        bool hasManagedPlugin,
        Dictionary<INamedTypeSymbol, string?> categories,
        HashSet<INamedTypeSymbol> invalid)
    {
        foreach (var typeSymbol in allTypes)
        {
            if (typeSymbol.TypeKind != TypeKind.Class || !SymbolEqualityComparer.Default.Equals(typeSymbol.ContainingAssembly, compilation.Assembly))
            {
                continue;
            }

            if (!HasFactoryReflection(typeSymbol, factoryReflectionSymbol, out var category))
            {
                if (hasManagedPlugin && factoryTypes.Contains(typeSymbol) &&
                    ClassifyRegistryType(typeSymbol, objectSymbol, pluginSymbol) == RegistryTypeKind.Included)
                {
                    context.ReportDiagnostic(Diagnostic.Create(UnmarkedTypeDiagnostic, GetLocation(typeSymbol), typeSymbol.ToDisplayString()));
                }

                continue;
            }

            var reason = GetFactoryReflectionTargetError(typeSymbol, objectSymbol, pluginSymbol, factoryTypes, reportedTypes);
            if (reason is not null)
            {
                invalid.Add(typeSymbol);
                if (reason.Length > 0)
                {
                    context.ReportDiagnostic(Diagnostic.Create(FactoryReflectionTargetDiagnostic, GetLocation(typeSymbol), typeSymbol.ToDisplayString(), reason));
                }

                continue;
            }

            if (category is not null && string.IsNullOrWhiteSpace(category))
            {
                invalid.Add(typeSymbol);
                context.ReportDiagnostic(Diagnostic.Create(FactoryReflectionCategoryDiagnostic, GetLocation(typeSymbol), typeSymbol.ToDisplayString()));
                continue;
            }

            categories[typeSymbol] = category;
            ReportUnmarkedBase(context, compilation, typeSymbol, objectSymbol, pluginSymbol, factoryReflectionSymbol, factoryTypes);
        }
    }

    // Returns the reason why the type cannot be registered, an empty string when another diagnostic reports it already, or null.
    private static string? GetFactoryReflectionTargetError(
        INamedTypeSymbol typeSymbol,
        INamedTypeSymbol objectSymbol,
        INamedTypeSymbol? pluginSymbol,
        HashSet<INamedTypeSymbol> factoryTypes,
        HashSet<INamedTypeSymbol> reportedTypes)
    {
        if (!DerivesFrom(typeSymbol, objectSymbol))
        {
            return "the type does not derive from Urho3D.Core.Object";
        }

        if (typeSymbol.IsAbstract)
        {
            return "the type is abstract";
        }

        if (pluginSymbol is not null && DerivesFromOrIs(typeSymbol, pluginSymbol))
        {
            return "the type is a Plugin. Plugins are not registered";
        }

        for (var current = typeSymbol; current is not null; current = current.ContainingType)
        {
            if (current.TypeParameters.Length > 0)
            {
                return "the type or a containing type is generic";
            }
        }

        if (typeSymbol.IsFileLocal)
        {
            return "the type is file-local";
        }

        if (!factoryTypes.Contains(typeSymbol))
        {
            // The partial rule and the name rule report their own error.
            return reportedTypes.Contains(typeSymbol)
                ? string.Empty
                : "the type has no factory. Add a public constructor with one Urho3D.Core.Context parameter, or implement IManagedObjectFactory<T> for the type";
        }

        return null;
    }

    // The nearest base type of the same assembly that the registry can list must have the attribute too. Else the engine has no reflection of it.
    // A base type that can never be registered is skipped.
    private static void ReportUnmarkedBase(
        SourceProductionContext context,
        Compilation compilation,
        INamedTypeSymbol typeSymbol,
        INamedTypeSymbol objectSymbol,
        INamedTypeSymbol? pluginSymbol,
        INamedTypeSymbol factoryReflectionSymbol,
        HashSet<INamedTypeSymbol> factoryTypes)
    {
        for (var current = typeSymbol.BaseType; current is not null; current = current.BaseType)
        {
            if (!SymbolEqualityComparer.Default.Equals(current.ContainingAssembly, compilation.Assembly) ||
                SymbolEqualityComparer.Default.Equals(current, objectSymbol))
            {
                return;
            }

            if (!factoryTypes.Contains(current) ||
                ClassifyRegistryType(current, objectSymbol, pluginSymbol) != RegistryTypeKind.Included)
            {
                continue;
            }

            if (!HasFactoryReflection(current, factoryReflectionSymbol, out _))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UnmarkedBaseDiagnostic, GetLocation(typeSymbol), typeSymbol.ToDisplayString(), current.ToDisplayString()));
            }

            return;
        }
    }

    private static bool ImplementsFactoryForItself(INamedTypeSymbol typeSymbol, INamedTypeSymbol objectFactorySymbol)
    {
        foreach (var interfaceSymbol in typeSymbol.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(interfaceSymbol.OriginalDefinition, objectFactorySymbol) &&
                SymbolEqualityComparer.Default.Equals(interfaceSymbol.TypeArguments[0], typeSymbol))
            {
                return true;
            }
        }

        return false;
    }

    private static int GetDepth(INamedTypeSymbol typeSymbol)
    {
        var depth = 0;
        for (var current = typeSymbol.BaseType; current is not null; current = current.BaseType)
        {
            depth++;
        }

        return depth;
    }

    // The name that System.Type.FullName returns: Namespace.Outer+Inner.
    private static string GetMetadataFullName(INamedTypeSymbol typeSymbol)
    {
        var name = typeSymbol.MetadataName;
        for (var current = typeSymbol.ContainingType; current is not null; current = current.ContainingType)
        {
            name = current.MetadataName + "+" + name;
        }

        return GetNamespace(typeSymbol) is { } ns ? ns + "." + name : name;
    }

    private static bool TryCreateCandidate(
        SourceProductionContext context,
        INamedTypeSymbol typeSymbol,
        INamedTypeSymbol objectSymbol,
        INamedTypeSymbol stringHashSymbol,
        INamedTypeSymbol nativeObjectRegistrationSymbol,
        HashSet<INamedTypeSymbol> reportedTypes,
        out Candidate? candidate)
    {
        candidate = null;

        if (typeSymbol.TypeKind != TypeKind.Class ||
            typeSymbol.ContainingType is not null ||
            typeSymbol.TypeParameters.Length > 0)
        {
            return false;
        }

        if (!DerivesFrom(typeSymbol, objectSymbol) ||
            ImplementsDirectNativeRegistration(typeSymbol, nativeObjectRegistrationSymbol) ||
            HasManualRttiImplementation(typeSymbol, stringHashSymbol))
        {
            return false;
        }

        if (!IsPartial(typeSymbol))
        {
            context.ReportDiagnostic(Diagnostic.Create(PartialRequiredDiagnostic, GetLocation(typeSymbol), typeSymbol.Name));
            reportedTypes.Add(typeSymbol);
            return false;
        }

        if (typeSymbol.BaseType is null)
        {
            return false;
        }

        candidate = new Candidate(
            typeSymbol,
            GetAccessibilityText(typeSymbol),
            GetNamespace(typeSymbol),
            typeSymbol.Name,
            typeSymbol.BaseType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            hideStaticTypeNameMembers: !SymbolEqualityComparer.Default.Equals(typeSymbol.BaseType, objectSymbol),
            GetLocation(typeSymbol));
        return true;
    }

    private static bool ImplementsInterface(INamedTypeSymbol typeSymbol, INamedTypeSymbol interfaceDefinition)
    {
        foreach (var interfaceSymbol in typeSymbol.Interfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(interfaceSymbol.OriginalDefinition, interfaceDefinition))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasContextConstructor(INamedTypeSymbol typeSymbol, INamedTypeSymbol contextSymbol)
    {
        foreach (var constructor in typeSymbol.InstanceConstructors)
        {
            if (constructor.DeclaredAccessibility == Accessibility.Public &&
                constructor.Parameters.Length == 1 &&
                SymbolEqualityComparer.Default.Equals(constructor.Parameters[0].Type, contextSymbol))
            {
                return true;
            }
        }

        return false;
    }

    private static bool DerivesFrom(INamedTypeSymbol typeSymbol, INamedTypeSymbol baseType)
    {
        for (var current = typeSymbol.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ImplementsDirectNativeRegistration(INamedTypeSymbol typeSymbol, INamedTypeSymbol nativeObjectRegistrationSymbol)
    {
        var inheritedInterfaces = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        if (typeSymbol.BaseType is not null)
        {
            foreach (var interfaceSymbol in typeSymbol.BaseType.AllInterfaces)
            {
                inheritedInterfaces.Add(interfaceSymbol);
            }
        }

        foreach (var interfaceSymbol in typeSymbol.Interfaces)
        {
            if (!inheritedInterfaces.Contains(interfaceSymbol) &&
                SymbolEqualityComparer.Default.Equals(interfaceSymbol.OriginalDefinition, nativeObjectRegistrationSymbol))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasManualRttiImplementation(INamedTypeSymbol typeSymbol, INamedTypeSymbol stringHashSymbol)
    {
        return HasInstanceMethod(typeSymbol, "GetType") ||
            HasInstanceMethod(typeSymbol, "GetTypeName") ||
            HasInstanceMethod(typeSymbol, "GetTypeInfo") ||
            HasIsInstanceOfMethod(typeSymbol, stringHashSymbol) ||
            HasStaticMethod(typeSymbol, "GetTypeStatic") ||
            HasStaticMethod(typeSymbol, "GetTypeNameStatic") ||
            HasStaticMethod(typeSymbol, "GetTypeInfoStatic");
    }

    private static bool HasInstanceMethod(INamedTypeSymbol typeSymbol, string methodName)
    {
        foreach (var member in typeSymbol.GetMembers(methodName))
        {
            if (member is IMethodSymbol { MethodKind: MethodKind.Ordinary, IsStatic: false } method &&
                SymbolEqualityComparer.Default.Equals(method.ContainingType, typeSymbol) &&
                method.Parameters.Length == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasIsInstanceOfMethod(INamedTypeSymbol typeSymbol, INamedTypeSymbol stringHashSymbol)
    {
        foreach (var member in typeSymbol.GetMembers("IsInstanceOf"))
        {
            if (member is IMethodSymbol { MethodKind: MethodKind.Ordinary, IsStatic: false } method &&
                SymbolEqualityComparer.Default.Equals(method.ContainingType, typeSymbol) &&
                method.Parameters.Length == 1 &&
                SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, stringHashSymbol))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasStaticMethod(INamedTypeSymbol typeSymbol, string methodName)
    {
        foreach (var member in typeSymbol.GetMembers(methodName))
        {
            if (member is IMethodSymbol { MethodKind: MethodKind.Ordinary, IsStatic: true } method &&
                SymbolEqualityComparer.Default.Equals(method.ContainingType, typeSymbol) &&
                method.Parameters.Length == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPartial(INamedTypeSymbol typeSymbol)
    {
        foreach (var syntaxReference in typeSymbol.DeclaringSyntaxReferences)
        {
            if (syntaxReference.GetSyntax() is ClassDeclarationSyntax classDeclaration &&
                classDeclaration.Modifiers.Any(static modifier => modifier.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasTypeNameCollision(Compilation compilation, INamedTypeSymbol typeSymbol, string typeName, INamedTypeSymbol objectSymbol)
    {
        foreach (var candidate in compilation.GetSymbolsWithName(typeName, SymbolFilter.Type))
        {
            if (candidate is not INamedTypeSymbol otherType ||
                !DerivesFromOrIs(otherType, objectSymbol) ||
                SymbolEqualityComparer.Default.Equals(otherType, typeSymbol))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool DerivesFromOrIs(INamedTypeSymbol typeSymbol, INamedTypeSymbol baseType)
    {
        return SymbolEqualityComparer.Default.Equals(typeSymbol, baseType) || DerivesFrom(typeSymbol, baseType);
    }

    private static Location GetLocation(INamedTypeSymbol typeSymbol)
    {
        return typeSymbol.Locations.Length > 0 ? typeSymbol.Locations[0] : Location.None;
    }

    private static string GetAccessibilityText(INamedTypeSymbol typeSymbol)
    {
        return typeSymbol.DeclaredAccessibility switch
        {
            Accessibility.Public => "public",
            _ => "internal"
        };
    }

    private static string? GetNamespace(INamedTypeSymbol typeSymbol)
    {
        return typeSymbol.ContainingNamespace is { IsGlobalNamespace: false } containingNamespace
            ? containingNamespace.ToDisplayString()
            : null;
    }

    private static string GetHintName(Candidate candidate)
    {
        var builder = new StringBuilder();
        foreach (var character in candidate.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        builder.Append(".ManagedObjectRtti.g.cs");
        return builder.ToString();
    }

    private static string Render(Candidate candidate)
    {
        var builder = new StringBuilder();
        var indent = candidate.Namespace is null ? string.Empty : "    ";
        var memberIndent = indent + "    ";
        var bodyIndent = memberIndent + "    ";

        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("#nullable enable");
        builder.AppendLine();

        if (candidate.Namespace is not null)
        {
            builder.Append("namespace ").Append(candidate.Namespace).AppendLine();
            builder.AppendLine("{");
        }

        builder.Append(indent).Append(candidate.Accessibility).Append(" partial class ").Append(candidate.Symbol.Name);
        if (candidate.ImplementsObjectFactory)
        {
            builder.Append(" : global::SharpCpp.Rbfx.Bindings.Interop.IManagedObjectFactory<").Append(candidate.Symbol.Name).Append('>');
        }

        builder.AppendLine();
        builder.Append(indent).AppendLine("{");

        builder.Append(memberIndent).AppendLine("public override global::Urho3D.Math.StringHash GetType()");
        builder.Append(memberIndent).AppendLine("{");
        builder.Append(bodyIndent).AppendLine("return GetTypeStatic();");
        builder.Append(memberIndent).AppendLine("}");
        builder.AppendLine();

        builder.Append(memberIndent).AppendLine("public override global::Ea.String GetTypeName()");
        builder.Append(memberIndent).AppendLine("{");
        builder.Append(bodyIndent).AppendLine("return GetTypeNameStatic();");
        builder.Append(memberIndent).AppendLine("}");
        builder.AppendLine();

        builder.Append(memberIndent).AppendLine("public override global::Urho3D.Core.TypeInfo GetTypeInfo()");
        builder.Append(memberIndent).AppendLine("{");
        builder.Append(bodyIndent).AppendLine("return GetTypeInfoStatic();");
        builder.Append(memberIndent).AppendLine("}");
        builder.AppendLine();

        builder.Append(memberIndent).AppendLine("public override bool IsInstanceOf(global::Urho3D.Math.StringHash type)");
        builder.Append(memberIndent).AppendLine("{");
        builder.Append(bodyIndent).AppendLine("return GetTypeInfoStatic().IsTypeOf(type);");
        builder.Append(memberIndent).AppendLine("}");
        builder.AppendLine();

        builder.Append(memberIndent).Append("public ");
        if (candidate.HideStaticTypeNameMembers)
        {
            builder.Append("new ");
        }

        builder.AppendLine("static global::Urho3D.Math.StringHash GetTypeStatic()");
        builder.Append(memberIndent).AppendLine("{");
        builder.Append(bodyIndent).AppendLine("return GetTypeInfoStatic().GetType();");
        builder.Append(memberIndent).AppendLine("}");
        builder.AppendLine();

        builder.Append(memberIndent).Append("public ");
        if (candidate.HideStaticTypeNameMembers)
        {
            builder.Append("new ");
        }

        builder.AppendLine("static global::Ea.String GetTypeNameStatic()");
        builder.Append(memberIndent).AppendLine("{");
        builder.Append(bodyIndent).AppendLine("return GetTypeInfoStatic().GetTypeName();");
        builder.Append(memberIndent).AppendLine("}");
        builder.AppendLine();

        builder.Append(memberIndent).AppendLine("public new static global::Urho3D.Core.TypeInfo GetTypeInfoStatic()");
        builder.Append(memberIndent).AppendLine("{");
        builder.Append(bodyIndent)
            .Append("return global::SharpCpp.Rbfx.Bindings.Interop.ManagedTypeInfoRegistry.GetOrCreate(")
            .Append(EscapeStringLiteral(candidate.TypeName))
            .Append(", ")
            .Append(candidate.BaseTypeDisplay)
            .AppendLine(".GetTypeInfoStatic());");
        builder.Append(memberIndent).AppendLine("}");

        if (candidate.SlotKeys.Count > 0)
        {
            // The IsManagedOverride generator cannot see these overrides, so the type lists their slots here.
            builder.AppendLine();
            builder.Append(memberIndent).AppendLine("protected override bool IsGeneratedManagedOverride(string slotKey)");
            builder.Append(memberIndent).AppendLine("{");
            builder.Append(bodyIndent).Append("return slotKey is ")
                .Append(string.Join(" or ", candidate.SlotKeys.Select(EscapeStringLiteral)))
                .AppendLine(" || base.IsGeneratedManagedOverride(slotKey);");
            builder.Append(memberIndent).AppendLine("}");
        }

        if (candidate.ImplementsObjectFactory)
        {
            builder.AppendLine();
            builder.Append(memberIndent).Append("static ").Append(candidate.Symbol.Name)
                .Append(" global::SharpCpp.Rbfx.Bindings.Interop.IManagedObjectFactory<").Append(candidate.Symbol.Name)
                .AppendLine(">.Create(global::Urho3D.Core.Context context)");
            builder.Append(memberIndent).AppendLine("{");
            builder.Append(bodyIndent).Append("return new ").Append(candidate.Symbol.Name).AppendLine("(context);");
            builder.Append(memberIndent).AppendLine("}");
        }

        builder.Append(indent).AppendLine("}");

        if (candidate.Namespace is not null)
        {
            builder.AppendLine("}");
        }

        return builder.ToString();
    }

    // The slot keys of the RTTI methods that Render overrides. Each key comes from the ManagedVirtualSlot attribute
    // of a base method in the overridden chain. The result is empty when the bindings have no IsGeneratedManagedOverride hook.
    private static IReadOnlyList<string> CollectRttiSlotKeys(INamedTypeSymbol typeSymbol, INamedTypeSymbol objectSymbol)
    {
        if (objectSymbol.GetMembers(GeneratedOverrideHookName).IsEmpty)
        {
            return Array.Empty<string>();
        }

        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var name in RttiMethodNames)
        {
            var method = FindInstanceMethod(typeSymbol.BaseType, name);
            for (var current = method; current is not null; current = current.OverriddenMethod)
            {
                foreach (var attribute in current.OriginalDefinition.GetAttributes())
                {
                    if (attribute.AttributeClass is { Name: "ManagedVirtualSlotAttribute" } attributeClass &&
                        attributeClass.ContainingNamespace.ToDisplayString() == "SharpCpp.Support" &&
                        attribute.ConstructorArguments.Length == 1 &&
                        attribute.ConstructorArguments[0].Value is string key)
                    {
                        keys.Add(key);
                    }
                }
            }
        }

        return keys.ToList();
    }

    private static IMethodSymbol? FindInstanceMethod(INamedTypeSymbol? typeSymbol, string name)
    {
        for (var current = typeSymbol; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(name))
            {
                if (member is IMethodSymbol { IsStatic: false, MethodKind: MethodKind.Ordinary } method &&
                    (method.IsVirtual || method.IsOverride || method.IsAbstract) &&
                    method.Parameters.Length == (name == "IsInstanceOf" ? 1 : 0))
                {
                    return method;
                }
            }
        }

        return null;
    }

    private static string EscapeStringLiteral(string value)
    {
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    private sealed class Candidate
    {
        public Candidate(
            INamedTypeSymbol symbol,
            string accessibility,
            string? @namespace,
            string typeName,
            string baseTypeDisplay,
            bool hideStaticTypeNameMembers,
            Location location)
        {
            Symbol = symbol;
            Accessibility = accessibility;
            Namespace = @namespace;
            TypeName = typeName;
            BaseTypeDisplay = baseTypeDisplay;
            HideStaticTypeNameMembers = hideStaticTypeNameMembers;
            Location = location;
        }

        public INamedTypeSymbol Symbol { get; }

        public string Accessibility { get; }

        public string? Namespace { get; }

        public string TypeName { get; }

        public string BaseTypeDisplay { get; }

        public bool HideStaticTypeNameMembers { get; }

        public Location Location { get; }

        public bool ImplementsObjectFactory { get; set; }

        public IReadOnlyList<string> SlotKeys { get; set; } = Array.Empty<string>();
    }
}
