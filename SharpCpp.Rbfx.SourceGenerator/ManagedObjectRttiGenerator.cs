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
        isEnabledByDefault: true);

    internal const string RegistryAttributeMetadataName = "Urho3D.Plugins.ManagedTypeRegistryAttribute";
    internal const string RegistryNamespace = "SharpCpp.Rbfx.Generated";
    internal const string RegistryClassName = "GeneratedManagedTypeRegistryAttribute";
    internal const string RegistryHintName = "SharpCpp.Rbfx.ManagedTypeRegistry.g.cs";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var typeDeclarations = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => node is ClassDeclarationSyntax { BaseList.Types.Count: > 0 },
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
        foreach (var typeSymbol in uniqueSymbols)
        {
            if (objectFactorySymbol is not null && ImplementsFactoryForItself(typeSymbol, objectFactorySymbol))
            {
                factoryTypes.Add(typeSymbol);
            }

            if (!TryCreateCandidate(context, typeSymbol, objectSymbol, stringHashSymbol, nativeObjectRegistrationSymbol, out var candidate) ||
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

        EmitTypeRegistry(context, compilation, objectSymbol, factoryTypes, hasManagedPlugin);
    }

    // Emits the ManagedTypeRegistryAttribute of the assembly: one AOT safe registration for each concrete managed type.
    // The rules are the rules of ManagedPlugin.ShouldRegister (ManagedObjectFactory.IsFactoryType, not a Plugin),
    // so the list has the types that the default ShouldRegister accepts. ShouldRegister filters the list again at run time.
    private static void EmitTypeRegistry(
        SourceProductionContext context,
        Compilation compilation,
        INamedTypeSymbol objectSymbol,
        HashSet<INamedTypeSymbol> factoryTypes,
        bool hasManagedPlugin)
    {
        var registrySymbol = compilation.GetTypeByMetadataName(RegistryAttributeMetadataName);
        if (registrySymbol is null || SymbolEqualityComparer.Default.Equals(registrySymbol.ContainingAssembly, compilation.Assembly))
        {
            return;
        }

        var pluginSymbol = compilation.GetTypeByMetadataName("Urho3D.Plugins.Plugin");
        var types = new List<INamedTypeSymbol>();
        foreach (var typeSymbol in factoryTypes)
        {
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

        context.AddSource(RegistryHintName, SourceText.From(RenderTypeRegistry(types, attributes), Encoding.UTF8));
    }

    internal static string RenderTypeRegistry(
        IReadOnlyList<INamedTypeSymbol> types,
        IReadOnlyDictionary<INamedTypeSymbol, List<AttributeEntry>> attributes)
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
    }
}
