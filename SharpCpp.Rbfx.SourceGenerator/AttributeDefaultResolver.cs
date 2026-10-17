using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpCpp.Rbfx.SourceGenerator;

/// <summary>
/// Finds the default value of an attribute member from its declaration (<c>[SerializeField] float Speed = 2.5f;</c>), so that the
/// generated registry needs no temporary instance of the type to read it.
///
/// A default is EMITTABLE when the registry file can compute it without an instance of the type:
///  - The member is a field, or an auto-property that is not virtual, abstract or override.
///  - No constructor of the type or of its base types in the described chain assigns the member. The scan is syntactic: an
///    assignment, an increment, a ref or out argument, a ref local or an address-of of the member (by name or as this.name).
///    A method that a constructor calls to set the member is NOT seen. A later step replaces the temporary instance, so it will
///    not see such a member either.
///  - The initializer, if there is one, has no reference to the instance (this, base, an instance member of the type), no lambda,
///    no object or collection initializer, no extension call, no ref or out argument, no private or protected symbol and no type
///    parameter. Without an initializer the default is default(T).
///
/// The text comes from the syntax of the initializer, which a rewriter changes so that it compiles in the registry file, whatever
/// the usings of the original file are:
///  - A sub-expression with a compile-time constant value becomes a literal of that value (an enum value becomes its member).
///    A private constant needs no access then. The constants nameof(x) and string concatenation are folded too.
///  - Each type name becomes a global::-qualified name, and each unqualified static member becomes global::Type.Member.
///  - All other tokens stay as they are, and a literal stays as written.
/// The semantic model of the compilation decides all of this, not the text. A default that does not pass is not emittable.
/// A later step turns a not emittable default into an error, when the temporary instance is removed.
/// </summary>
internal sealed class AttributeDefaultResolver
{
    private readonly Compilation _compilation;
    private readonly Dictionary<SyntaxTree, SemanticModel> _models = new();

    public AttributeDefaultResolver(Compilation compilation)
    {
        _compilation = compilation;
    }

    /// <summary>The syntax of the constructors of the types, with the semantic model of each syntax tree.</summary>
    public List<(SemanticModel Model, SyntaxNode Node)> CollectConstructors(IEnumerable<INamedTypeSymbol> chain)
    {
        var result = new List<(SemanticModel, SyntaxNode)>();
        foreach (var type in chain)
        {
            foreach (var constructor in type.InstanceConstructors)
            {
                foreach (var reference in constructor.DeclaringSyntaxReferences)
                {
                    var node = reference.GetSyntax();
                    if (node is ConstructorDeclarationSyntax)
                    {
                        result.Add((GetModel(node), node));
                    }
                    else if (node is TypeDeclarationSyntax declaration)
                    {
                        // A primary constructor: its parameters and the arguments of the base type.
                        if (declaration.ParameterList is not null)
                        {
                            result.Add((GetModel(node), declaration.ParameterList));
                        }

                        if (declaration.BaseList is not null)
                        {
                            result.Add((GetModel(node), declaration.BaseList));
                        }
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the body of the default delegate (the text after <c>static () =&gt;</c>), or null when the default is not emittable.
    /// An enum member gets the boxed enum value, a string member the null-forgiving operator when its value can be null.
    /// </summary>
    public string? TryGetDefault(ISymbol member, ITypeSymbol valueType, ValueKind kind, List<(SemanticModel Model, SyntaxNode Node)> constructors)
    {
        ExpressionSyntax? initializer;
        switch (member)
        {
            case IFieldSymbol field:
                if (field.DeclaringSyntaxReferences.Length != 1 || field.DeclaringSyntaxReferences[0].GetSyntax() is not VariableDeclaratorSyntax declarator)
                {
                    return null;
                }

                initializer = declarator.Initializer?.Value;
                break;
            case IPropertySymbol property:
                if (property.IsVirtual || property.IsOverride || property.IsAbstract || property.IsPartialDefinition ||
                    property.PartialImplementationPart is not null || property.DeclaringSyntaxReferences.Length != 1 ||
                    property.DeclaringSyntaxReferences[0].GetSyntax() is not PropertyDeclarationSyntax declaration ||
                    declaration.ExpressionBody is not null || declaration.AccessorList is null ||
                    declaration.AccessorList.Accessors.Any(static accessor => accessor.Body is not null || accessor.ExpressionBody is not null))
                {
                    return null;
                }

                initializer = declaration.Initializer?.Value;
                break;
            default:
                return null;
        }

        if (IsAssignedInConstructors(member, constructors))
        {
            return null;
        }

        var enumName = kind == ValueKind.Enum ? valueType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) : null;
        if (initializer is null)
        {
            return enumName is null ? "default!" : "(object)default(" + enumName + ")";
        }

        var model = GetModel(initializer);

        // The null-forgiving operator has no operation of its own, so the check looks at its operand.
        var operand = initializer;
        while (operand is PostfixUnaryExpressionSyntax postfix && postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression))
        {
            operand = postfix.Operand;
        }

        if (model.GetOperation(operand) is not { } operation || !IsEmittable(operation))
        {
            return null;
        }

        var rewriter = new ExpressionRewriter(this, model, initializer);
        var text = rewriter.Visit(initializer)?.NormalizeWhitespace().ToFullString();
        if (text is null || rewriter.Failed)
        {
            return null;
        }

        switch (kind)
        {
            case ValueKind.Enum:
                // A constant of the enum type is a member access or an unchecked cast. Another expression gets a cast.
                var isEnumConstant = model.GetConstantValue(initializer).HasValue && SymbolEqualityComparer.Default.Equals(model.GetTypeInfo(initializer).Type, valueType);
                return isEnumConstant ? "(object)" + text : "(object)(" + enumName + ")(" + text + ")";
            case ValueKind.String:
                return model.GetConstantValue(initializer).Value is string ? text : "(" + text + ")!";
            default:
                return text;
        }
    }

    private SemanticModel GetModel(SyntaxNode node)
    {
        if (!_models.TryGetValue(node.SyntaxTree, out var model))
        {
            model = _compilation.GetSemanticModel(node.SyntaxTree);
            _models.Add(node.SyntaxTree, model);
        }

        return model;
    }

    private static bool IsAssignedInConstructors(ISymbol member, List<(SemanticModel Model, SyntaxNode Node)> constructors)
    {
        var memberType = member switch
        {
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            _ => null,
        };

        foreach (var (model, node) in constructors)
        {
            foreach (var name in node.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
            {
                if (name.Identifier.ValueText != member.Name)
                {
                    continue;
                }

                var info = model.GetSymbolInfo(name);
                var matches = info.Symbol is { } symbol
                    ? SymbolEqualityComparer.Default.Equals(symbol.OriginalDefinition, member.OriginalDefinition)
                    : info.CandidateSymbols.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, member.OriginalDefinition));
                if (matches && IsWritten(name, memberType))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // The name is the target of a write, or a reference that a write can use: this.x = ..., x++, ref x, out x, (x, y) = ..., &x.
    // A part of a value-type member (x.Y = 1, x.Y += 1, x.Normalize()) or an element of an array member (x[0] = 1) is a write too.
    // Then the outermost member or element access is the target. Any call on a value-type member counts, because it can mutate it.
    private static bool IsWritten(IdentifierNameSyntax name, ITypeSymbol? memberType)
    {
        SyntaxNode current = name;
        if (current.Parent is MemberAccessExpressionSyntax access && access.Name == name)
        {
            current = access;
        }

        if (memberType is { IsValueType: true } or IArrayTypeSymbol)
        {
            var viaElement = false;
            while (true)
            {
                if (current.Parent is ParenthesizedExpressionSyntax parenthesized)
                {
                    current = parenthesized;
                }
                else if (current.Parent is MemberAccessExpressionSyntax inner && inner.Expression == current)
                {
                    current = inner;
                }
                else if (current.Parent is ElementAccessExpressionSyntax element && element.Expression == current)
                {
                    current = element;
                    viaElement = true;
                }
                else
                {
                    break;
                }
            }

            if (current.Parent is InvocationExpressionSyntax invocation && invocation.Expression == current && (memberType.IsValueType || viaElement))
            {
                return true;
            }
        }

        var parent = current.Parent;
        if (parent is ArgumentSyntax argument && (argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) || argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword)))
        {
            return true;
        }

        while (parent is ParenthesizedExpressionSyntax or TupleExpressionSyntax or (ArgumentSyntax { Parent: TupleExpressionSyntax }))
        {
            current = parent;
            parent = current.Parent;
        }

        return parent switch
        {
            AssignmentExpressionSyntax assignment => assignment.Left == current,
            PrefixUnaryExpressionSyntax unary => unary.IsKind(SyntaxKind.PreIncrementExpression) || unary.IsKind(SyntaxKind.PreDecrementExpression) ||
                                                 unary.IsKind(SyntaxKind.AddressOfExpression),
            PostfixUnaryExpressionSyntax postfix => postfix.IsKind(SyntaxKind.PostIncrementExpression) || postfix.IsKind(SyntaxKind.PostDecrementExpression),
            RefExpressionSyntax => true,
            _ => false,
        };
    }

    // The operation tree must not need the instance, and every symbol of it must be usable in the registry file.
    // A constant sub-tree is replaced by a literal, so only the type of it counts.
    private bool IsEmittable(IOperation operation)
    {
        if (operation.Type is { } type && !IsTypeUsable(type))
        {
            return false;
        }

        if (operation.ConstantValue.HasValue)
        {
            return true;
        }

        switch (operation)
        {
            case ILiteralOperation:
            case IDefaultValueOperation:
            case IConditionalOperation:
            case ICoalesceOperation:
            case IArrayInitializerOperation:
                break;
            case ITypeOfOperation typeOf when IsTypeUsable(typeOf.TypeOperand):
                break;
            case IArrayCreationOperation { IsImplicit: true }:
                break;
            case IUnaryOperation unary when unary.OperatorMethod is null || IsMemberUsable(unary.OperatorMethod):
                break;
            case IBinaryOperation binary when binary.OperatorMethod is null || IsMemberUsable(binary.OperatorMethod):
                break;
            case IConversionOperation conversion when conversion.OperatorMethod is null || IsMemberUsable(conversion.OperatorMethod):
                break;
            case IObjectCreationOperation { Initializer: null } creation when creation.Constructor is null || IsMemberUsable(creation.Constructor):
                break;
            case IFieldReferenceOperation reference when IsMemberUsable(reference.Field):
                break;
            case IPropertyReferenceOperation reference when reference.Property.Parameters.Length == 0 && IsMemberUsable(reference.Property) &&
                                                            (reference.Property.GetMethod is null || IsMemberUsable(reference.Property.GetMethod)):
                break;
            case IInvocationOperation invocation when !invocation.TargetMethod.IsExtensionMethod && IsMemberUsable(invocation.TargetMethod):
                break;
            case IArgumentOperation argument when argument.Parameter is null || argument.Parameter.RefKind == RefKind.None:
                break;
            default:
                return false;
        }

        foreach (var child in operation.ChildOperations)
        {
            if (!IsEmittable(child))
            {
                return false;
            }
        }

        return true;
    }

    private bool IsMemberUsable(ISymbol symbol)
    {
        for (var current = symbol; current is not null; current = current.ContainingType)
        {
            // The registry file is outside of the generic type, so a type parameter has no meaning there (Base<T>.Member).
            if ((current is INamedTypeSymbol named && named.TypeArguments.Any(HasTypeParameter)) ||
                (current is IMethodSymbol method && method.TypeArguments.Any(HasTypeParameter)))
            {
                return false;
            }

            if (!_compilation.IsSymbolAccessibleWithin(current, _compilation.Assembly) || ManagedAttributeScanner.IsObsoleteError(current))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasTypeParameter(ITypeSymbol type) => type switch
    {
        ITypeParameterSymbol => true,
        IArrayTypeSymbol array => HasTypeParameter(array.ElementType),
        INamedTypeSymbol named => named.TypeArguments.Any(HasTypeParameter) || (named.ContainingType is { } outer && HasTypeParameter(outer)),
        _ => false,
    };

    private bool IsTypeUsable(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return IsTypeUsable(array.ElementType);
            case INamedTypeSymbol named:
                if (named.TypeKind == TypeKind.Error || !IsMemberUsable(named.OriginalDefinition))
                {
                    return false;
                }

                return named.TypeArguments.All(IsTypeUsable);
            default:
                // Type parameters, pointers, dynamic and function pointers.
                return false;
        }
    }

    private string? RenderConstant(object? value, ITypeSymbol? type)
    {
        if (value is null)
        {
            return type is null || type.SpecialType == SpecialType.System_Object ? "null" : "default(" + type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")";
        }

        if (type is { TypeKind: TypeKind.Enum } enumType)
        {
            var display = enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            foreach (var candidate in enumType.GetMembers().OfType<IFieldSymbol>())
            {
                if (candidate.HasConstantValue && Equals(candidate.ConstantValue, value))
                {
                    return display + "." + (SyntaxFacts.GetKeywordKind(candidate.Name) != SyntaxKind.None ? "@" + candidate.Name : candidate.Name);
                }
            }

            return "unchecked((" + display + ")" + Parenthesize(FormatLiteral(value)) + ")";
        }

        var literal = FormatLiteral(value);
        switch (value)
        {
            case sbyte or byte or short or ushort:
                return "unchecked((" + (type ?? _compilation.GetSpecialType(SpecialType.System_Int32)).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")" +
                       Parenthesize(literal) + ")";
            case bool or char or string or int or uint or long or ulong or float or double or decimal:
                return literal;
            default:
                return null;
        }
    }

    // A negative literal after a cast must be in parentheses: (E)-1 is a subtraction when E is a name.
    private static string Parenthesize(string literal) => literal.StartsWith("-", StringComparison.Ordinal) ? "(" + literal + ")" : literal;

    // The literal of a constant, with the suffix of its type. SymbolDisplay.FormatPrimitive omits the suffix of float and double.
    private static string FormatLiteral(object value)
    {
        var invariant = CultureInfo.InvariantCulture;
        switch (value)
        {
            case float single:
                return float.IsNaN(single) ? "float.NaN"
                    : float.IsPositiveInfinity(single) ? "float.PositiveInfinity"
                    : float.IsNegativeInfinity(single) ? "float.NegativeInfinity"
                    : single.ToString("R", invariant) + "F";
            case double number:
                return double.IsNaN(number) ? "double.NaN"
                    : double.IsPositiveInfinity(number) ? "double.PositiveInfinity"
                    : double.IsNegativeInfinity(number) ? "double.NegativeInfinity"
                    : number.ToString("R", invariant) + "D";
            case decimal money:
                return money.ToString(invariant) + "M";
            case long integer:
                return integer.ToString(invariant) + "L";
            case uint unsigned:
                return unsigned.ToString(invariant) + "U";
            case ulong wide:
                return wide.ToString(invariant) + "UL";
            default:
                return SymbolDisplay.FormatPrimitive(value, quoteStrings: true, useHexadecimalNumbers: false);
        }
    }

    private sealed class ExpressionRewriter : CSharpSyntaxRewriter
    {
        private readonly AttributeDefaultResolver _owner;
        private readonly SemanticModel _model;
        private readonly SyntaxNode _root;

        public ExpressionRewriter(AttributeDefaultResolver owner, SemanticModel model, SyntaxNode root)
        {
            _owner = owner;
            _model = model;
            _root = root;
        }

        public bool Failed { get; private set; }

        public override SyntaxNode? Visit(SyntaxNode? node)
        {
            if (node is not ExpressionSyntax expression || expression is LiteralExpressionSyntax || expression is PredefinedTypeSyntax)
            {
                return base.Visit(node);
            }

            var constant = _model.GetConstantValue(expression);
            if (constant.HasValue)
            {
                var text = _owner.RenderConstant(constant.Value, _model.GetTypeInfo(expression).Type);
                if (text is null)
                {
                    Failed = true;
                    return node;
                }

                // A negative number after an operator must not join its sign: a - -1.
                if (text.StartsWith("-", StringComparison.Ordinal) && node != _root && node.Parent is not ArgumentSyntax)
                {
                    text = "(" + text + ")";
                }

                return SyntaxFactory.ParseExpression(text).WithTriviaFrom(expression);
            }

            if (expression is IdentifierNameSyntax or GenericNameSyntax or QualifiedNameSyntax or AliasQualifiedNameSyntax or MemberAccessExpressionSyntax)
            {
                var symbol = _model.GetSymbolInfo(expression).Symbol;
                if (symbol is INamedTypeSymbol or IArrayTypeSymbol)
                {
                    if (HasTypeParameter((ITypeSymbol)symbol))
                    {
                        Failed = true;
                        return node;
                    }

                    return SyntaxFactory.ParseTypeName(((ITypeSymbol)symbol).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).WithTriviaFrom(expression);
                }

                if (expression is SimpleNameSyntax name && IsStandalone(name) &&
                    symbol is IFieldSymbol or IPropertySymbol or IMethodSymbol or IEventSymbol && symbol.IsStatic && symbol.ContainingType is { } owner)
                {
                    if (HasTypeParameter(owner))
                    {
                        Failed = true;
                        return node;
                    }

                    var visited = base.Visit(node)!;
                    return SyntaxFactory.ParseExpression(owner.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + visited.ToString().Trim())
                        .WithTriviaFrom(expression);
                }
            }

            return base.Visit(node);
        }

        // A name that is the right side of a member access, a named argument and so on is not looked up in the scope.
        private static bool IsStandalone(SimpleNameSyntax name) => name.Parent switch
        {
            MemberAccessExpressionSyntax access => access.Name != name,
            QualifiedNameSyntax qualified => qualified.Right != name,
            AliasQualifiedNameSyntax => false,
            MemberBindingExpressionSyntax => false,
            NameColonSyntax => false,
            NameEqualsSyntax => false,
            _ => true,
        };
    }
}
