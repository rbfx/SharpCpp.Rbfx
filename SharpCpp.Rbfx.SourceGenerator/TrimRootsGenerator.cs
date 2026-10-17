using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace SharpCpp.Rbfx.SourceGenerator;

/// <summary>
/// Emits the trimmer roots of an assembly: the class <c>SharpCpp.Rbfx.Generated.TrimRoots</c> with the method
/// <c>Keep</c>. <c>Keep</c> has one <c>DynamicDependency</c> attribute for each member that only native code reaches.
/// ILLink and the NativeAOT compiler both read these attributes. The heads keep <c>Keep</c> of the head assembly
/// (an attribute on the entry method of <c>Program</c>), so they do not need <c>TrimmerRootAssembly</c> items.
/// The roots are:
/// <list type="bullet">
/// <item>Each concrete <c>Urho3D.Plugins.Plugin</c> class with its public constructors.</item>
/// <item>The assembly attribute <c>[assembly: GeneratedPluginType(typeof(X))]</c> for the first of these classes (by
/// documentation id) that the generated code can see and that has a public constructor that takes a <c>Context</c>. <c>PluginRuntime.FindPluginType</c> reads it, so it does not scan the assembly types.
/// The assembly attribute <c>PluginModule</c> has priority.</item>
/// <item>Each static <c>[UnmanagedCallersOnly]</c> method that the assembly does not use with the address-of operator.
/// Native code can get it only by name (<c>DotNetHost::GetFunctionPointer</c>). A method with an address-of use is
/// kept with the code that gives its address to native code.</item>
/// <item><c>Keep</c> of each referenced assembly that has <c>TrimRoots</c> (the assembly attribute
/// <c>[AssemblyMetadata("SharpCpp.Rbfx.TrimRoots", ...)]</c> marks it). Thus the roots of the plugin and of its
/// references follow from the roots of the head.</item>
/// </list>
/// A method with <c>RequiresUnreferencedCode</c> or <c>RequiresDynamicCode</c> is not a root: it is not for trimmed
/// applications. The class exists also without roots, so that a head can always name it. The generator emits nothing
/// when the assembly does not reference the rbfx bindings.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class TrimRootsGenerator : IIncrementalGenerator
{
    internal const string RootsNamespace = "SharpCpp.Rbfx.Generated";
    internal const string RootsClassName = "TrimRoots";
    internal const string RootsMethodName = "Keep";
    internal const string RootsHintName = "SharpCpp.Rbfx.TrimRoots.g.cs";

    private const string PluginMetadataName = "Urho3D.Plugins.Plugin";
    private const string ContextMetadataName = "Urho3D.Core.Context";
    private const string GeneratedPluginTypeMetadataName = "Urho3D.Plugins.GeneratedPluginTypeAttribute";
    private const string UnmanagedCallersOnlyMetadataName = "System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute";
    private const string RequiresUnreferencedCodeMetadataName = "System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute";
    private const string RequiresDynamicCodeMetadataName = "System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute";
    private const string DynamicDependencyMetadataName = "System.Diagnostics.CodeAnalysis.DynamicDependencyAttribute";
    private const string AssemblyMetadataMetadataName = "System.Reflection.AssemblyMetadataAttribute";

    /// <summary>The key of the AssemblyMetadataAttribute that marks an assembly with TrimRoots.</summary>
    internal const string RootsMarkerKey = "SharpCpp.Rbfx.TrimRoots";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var entryPoints = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is MethodDeclarationSyntax { AttributeLists.Count: > 0 } method &&
                    method.Modifiers.Any(SyntaxKind.StaticKeyword) &&
                    HasUnmanagedCallersOnlySyntax(method),
                static (syntaxContext, cancellationToken) => GetEntryPoint(syntaxContext, cancellationToken))
            .Where(static root => root is not null)
            .Select(static (root, _) => root!)
            .Collect();

        // The address-of uses of methods. The key is the documentation id of the method.
        var addressTaken = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is PrefixUnaryExpressionSyntax prefix && prefix.IsKind(SyntaxKind.AddressOfExpression),
                static (syntaxContext, cancellationToken) => GetAddressTakenMethod(syntaxContext, cancellationToken))
            .Where(static id => id is not null)
            .Select(static (id, _) => id!)
            .Collect();

        var plugins = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList.Types.Count: > 0 },
                static (syntaxContext, cancellationToken) => GetPlugin(syntaxContext, cancellationToken))
            .Where(static root => root is not null)
            .Select(static (root, _) => root!)
            .Collect();

        var assembly = context.CompilationProvider.Select(static (compilation, _) => GetAssemblyInfo(compilation));

        var inputs = assembly.Combine(entryPoints).Combine(addressTaken).Combine(plugins);
        context.RegisterSourceOutput(inputs, static (productionContext, input) =>
        {
            var source = Render(input.Left.Left.Left, input.Left.Left.Right, input.Left.Right, input.Right);
            if (source is not null)
            {
                productionContext.AddSource(RootsHintName, SourceText.From(source, Encoding.UTF8));
            }
        });
    }

    private static Root? GetEntryPoint(GeneratorSyntaxContext syntaxContext, CancellationToken cancellationToken)
    {
        if (syntaxContext.SemanticModel.GetDeclaredSymbol((MethodDeclarationSyntax)syntaxContext.Node, cancellationToken) is not IMethodSymbol method ||
            !method.IsStatic ||
            !HasAttribute(method, UnmanagedCallersOnlyMetadataName) ||
            HasAttribute(method, RequiresUnreferencedCodeMetadataName) ||
            HasAttribute(method, RequiresDynamicCodeMetadataName))
        {
            return null;
        }

        for (var type = method.ContainingType; type is not null; type = type.ContainingType)
        {
            if (HasAttribute(type, RequiresUnreferencedCodeMetadataName) || HasAttribute(type, RequiresDynamicCodeMetadataName))
            {
                return null;
            }
        }

        return CreateRoot(method.ContainingType, method.Name, method.GetDocumentationCommentId());
    }

    private static string? GetAddressTakenMethod(GeneratorSyntaxContext syntaxContext, CancellationToken cancellationToken)
    {
        var operand = ((PrefixUnaryExpressionSyntax)syntaxContext.Node).Operand;
        if (operand is not (IdentifierNameSyntax or MemberAccessExpressionSyntax or QualifiedNameSyntax or AliasQualifiedNameSyntax))
        {
            return null;
        }

        var info = syntaxContext.SemanticModel.GetSymbolInfo(operand, cancellationToken);
        var method = info.Symbol as IMethodSymbol ?? info.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        return method?.OriginalDefinition.GetDocumentationCommentId();
    }

    private static Root? GetPlugin(GeneratorSyntaxContext syntaxContext, CancellationToken cancellationToken)
    {
        if (syntaxContext.SemanticModel.GetDeclaredSymbol((ClassDeclarationSyntax)syntaxContext.Node, cancellationToken) is not INamedTypeSymbol type ||
            type.IsAbstract ||
            type.IsGenericType ||
            IsInGenericType(type) ||
            syntaxContext.SemanticModel.Compilation.GetTypeByMetadataName(PluginMetadataName) is not { } pluginType)
        {
            return null;
        }

        for (var baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(baseType, pluginType))
            {
                var hasContextConstructor = syntaxContext.SemanticModel.Compilation.GetTypeByMetadataName(ContextMetadataName) is { } contextType &&
                    type.InstanceConstructors.Any(constructor =>
                        constructor.DeclaredAccessibility == Accessibility.Public &&
                        constructor.Parameters.Length == 1 &&
                        SymbolEqualityComparer.Default.Equals(constructor.Parameters[0].Type, contextType));
                return CreateRoot(type, memberName: null, type.GetDocumentationCommentId(), hasContextConstructor);
            }
        }

        return null;
    }

    private static AssemblyInfo GetAssemblyInfo(Compilation compilation)
    {
        // Only an assembly that sees the rbfx bindings and DynamicDependencyAttribute gets roots.
        var enabled = compilation.GetTypeByMetadataName(PluginMetadataName) is not null &&
            compilation.GetTypeByMetadataName(DynamicDependencyMetadataName) is not null;
        var chained = new List<string>();
        if (enabled)
        {
            // TrimRoots is internal, and reference assemblies do not have internal types. So the marker is an assembly attribute.
            foreach (var reference in compilation.SourceModule.ReferencedAssemblySymbols)
            {
                if (HasRootsMarker(reference))
                {
                    chained.Add(reference.Identity.Name);
                }
            }

            chained.Sort(StringComparer.Ordinal);
        }

        var hasPluginTypeAttribute = enabled && compilation.GetTypeByMetadataName(GeneratedPluginTypeMetadataName) is not null;
        return new AssemblyInfo(enabled, compilation.AssemblyName ?? string.Empty, string.Join("\n", chained), hasPluginTypeAttribute);
    }

    private static bool HasRootsMarker(IAssemblySymbol assembly)
    {
        foreach (var attribute in assembly.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() == AssemblyMetadataMetadataName &&
                attribute.ConstructorArguments.Length == 2 &&
                attribute.ConstructorArguments[0].Value is RootsMarkerKey)
            {
                return true;
            }
        }

        return false;
    }

    private static Root? CreateRoot(INamedTypeSymbol? type, string? memberName, string? documentationId, bool hasContextConstructor = true)
    {
        if (type is null || documentationId is null || type.GetDocumentationCommentId() is not { } typeId)
        {
            return null;
        }

        // typeof needs a type that the generated class can see. Else the attribute names the type in the documentation
        // id format (without "T:"), as DynamicDependencyAttribute expects, and the assembly of the compilation.
        var typeOf = IsVisibleInAssembly(type) ? type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) : null;
        return new Root(typeOf, typeId.Substring(2), memberName, documentationId, hasContextConstructor);
    }

    internal static string? Render(AssemblyInfo assembly, ImmutableArray<Root> entryPoints, ImmutableArray<string> addressTaken, ImmutableArray<Root> plugins)
    {
        if (!assembly.Enabled)
        {
            return null;
        }

        var taken = new HashSet<string>(addressTaken, StringComparer.Ordinal);
        var assemblyName = Quote(assembly.Name);
        var lines = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var plugin in plugins)
        {
            lines.Add(plugin.TypeOf is { } typeOf
                ? $"[global::System.Diagnostics.CodeAnalysis.DynamicDependency(global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors, typeof({typeOf}))]"
                : $"[global::System.Diagnostics.CodeAnalysis.DynamicDependency(global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors, {Quote(plugin.TypeName)}, {assemblyName})]");
        }

        foreach (var entryPoint in entryPoints)
        {
            if (taken.Contains(entryPoint.DocumentationId))
            {
                continue;
            }

            lines.Add(entryPoint.TypeOf is { } typeOf
                ? $"[global::System.Diagnostics.CodeAnalysis.DynamicDependency({Quote(entryPoint.MemberName!)}, typeof({typeOf}))]"
                : $"[global::System.Diagnostics.CodeAnalysis.DynamicDependency({Quote(entryPoint.MemberName!)}, {Quote(entryPoint.TypeName)}, {assemblyName})]");
        }

        var chained = assembly.ChainedAssemblies.Length == 0 ? [] : assembly.ChainedAssemblies.Split('\n');
        foreach (var name in chained)
        {
            lines.Add($"[global::System.Diagnostics.CodeAnalysis.DynamicDependency({Quote(RootsMethodName)}, {Quote(RootsNamespace + "." + RootsClassName)}, {Quote(name)})]");
        }

        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("#nullable enable");
        builder.AppendLine();
        // The first plugin class by documentation id (not by file order) that the generated code can see and that has a public
        // constructor that takes a Context, as PluginRuntime.FindPluginType needs. An assembly attribute exists once.
        var pluginType = plugins
            .Where(static plugin => plugin.TypeOf is not null && plugin.HasContextConstructor)
            .OrderBy(static plugin => plugin.DocumentationId, StringComparer.Ordinal)
            .FirstOrDefault()?.TypeOf;
        if (pluginType is not null && assembly.HasPluginTypeAttribute)
        {
            builder.AppendLine($"[assembly: global::Urho3D.Plugins.GeneratedPluginType(typeof({pluginType}))]");
        }

        builder.AppendLine($"[assembly: global::System.Reflection.AssemblyMetadata({Quote(RootsMarkerKey)}, {Quote(RootsNamespace + "." + RootsClassName)})]");
        builder.AppendLine();
        builder.AppendLine($"namespace {RootsNamespace}");
        builder.AppendLine("{");
        builder.AppendLine("    /// <summary>");
        builder.AppendLine("    /// The trimmer roots of this assembly (SharpCpp.Rbfx.SourceGenerator). Only native code reaches these members.");
        builder.AppendLine("    /// The head keeps <see cref=\"Keep\"/>, and the attributes keep the members.");
        builder.AppendLine("    /// </summary>");
        builder.AppendLine("    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        builder.AppendLine($"    internal static class {RootsClassName}");
        builder.AppendLine("    {");
        foreach (var line in lines)
        {
            builder.Append("        ").AppendLine(line);
        }

        builder.AppendLine($"        internal static void {RootsMethodName}()");
        builder.AppendLine("        {");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine("}");

        return builder.ToString();
    }

    // A cheap syntax filter before the semantic check. The bindings have many static methods with other attributes.
    private static bool HasUnmanagedCallersOnlySyntax(MethodDeclarationSyntax method)
    {
        foreach (var list in method.AttributeLists)
        {
            foreach (var attribute in list.Attributes)
            {
                var name = attribute.Name is QualifiedNameSyntax qualified ? qualified.Right.Identifier.ValueText
                    : attribute.Name is AliasQualifiedNameSyntax alias ? alias.Name.Identifier.ValueText
                    : attribute.Name is SimpleNameSyntax simple ? simple.Identifier.ValueText
                    : string.Empty;
                if (name is "UnmanagedCallersOnly" or "UnmanagedCallersOnlyAttribute")
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string Quote(string value) => SymbolDisplay.FormatLiteral(value, quote: true);

    private static bool HasAttribute(ISymbol symbol, string metadataName)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() == metadataName)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsVisibleInAssembly(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsInGenericType(INamedTypeSymbol type)
    {
        for (var current = type.ContainingType; current is not null; current = current.ContainingType)
        {
            if (current.IsGenericType)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>One root. <see cref="TypeOf"/> is null when the generated class cannot see the type.</summary>
    internal sealed record Root(string? TypeOf, string TypeName, string? MemberName, string DocumentationId, bool HasContextConstructor = true);

    /// <summary>
    /// The assembly name, and the referenced assemblies with roots, separated by new lines (a string for value equality).
    /// <c>HasPluginTypeAttribute</c> is true when the compilation sees <c>GeneratedPluginTypeAttribute</c>.
    /// </summary>
    internal sealed record AssemblyInfo(bool Enabled, string Name, string ChainedAssemblies, bool HasPluginTypeAttribute);
}
