using Microsoft.CodeAnalysis;

namespace FlowOnRails.Analysis.Internal;

internal sealed class LibrarySymbols
{
    internal Compilation Compilation { get; }
    internal INamedTypeSymbol Error { get; }
    private INamedTypeSymbol Rail { get; }
    private INamedTypeSymbol RailFor { get; }
    private INamedTypeSymbol? ResultsWith { get; }

    private LibrarySymbols(
        Compilation compilation,
        INamedTypeSymbol error,
        INamedTypeSymbol rail,
        INamedTypeSymbol railFor,
        INamedTypeSymbol? resultsWith
    ) =>
        (Compilation, Error, Rail, RailFor, ResultsWith) = (compilation, error, rail, railFor, resultsWith);

    internal static LibrarySymbols? Create(Compilation compilation)
    {
        var error = compilation.GetTypeByMetadataName("FlowOnRails.Error");
        var rail = compilation.GetTypeByMetadataName("FlowOnRails.Rail");
        var railFor = compilation.GetTypeByMetadataName("FlowOnRails.RailFor`1");
        var resultsWith = compilation.GetTypeByMetadataName("FlowOnRails.ResultsWithAttribute");

        return error is null || rail is null || railFor is null
            ? null
            : new LibrarySymbols(compilation, error, rail, railFor, resultsWith);
    }

    internal bool IsError(ITypeSymbol? type) => type is not null && ErrorSet.IsSubtype(type, Error);

    internal bool IsRail(ITypeSymbol? type) =>
        type is INamedTypeSymbol namedType
        && (SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, Rail)
            || SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, RailFor));

    internal ITypeSymbol? AwaitedType(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol namedType)
            return null;

        var task = Compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1");
        var valueTask = Compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask`1");

        return SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, task)
               || SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, valueTask)
            ? namedType.TypeArguments[0]
            : null;
    }

    internal ITypeSymbol UnwrapReturnType(ITypeSymbol type) =>
        AwaitedType(type) is { } awaitedType ? UnwrapReturnType(awaitedType) : type;

    internal bool IsAwaitableRail(ITypeSymbol type) =>
        AwaitedType(type) is { } awaitedType && IsRail(UnwrapReturnType(awaitedType));

    internal bool IsTrackedReturn(ITypeSymbol type) =>
        IsRail(UnwrapReturnType(type)) || IsError(UnwrapReturnType(type));

    internal bool MatchesReturnType(ITypeSymbol type, FlowValue value)
    {
        if (value.IsBottom)
            return true;

        if (AwaitedType(type) is { } awaitedType)
            return value.AwaitedValue is { } awaitedValue && MatchesReturnType(awaitedType, awaitedValue);

        if (IsRail(type))
            return value.Kind == ValueKind.Rail;

        if (IsError(type))
            return value.Kind == ValueKind.Error;

        return type.SpecialType == SpecialType.System_Boolean
            ? value.Kind == ValueKind.Boolean : value.Kind == ValueKind.Other;
    }

    internal bool IsTaskContainer(INamedTypeSymbol type) =>
        SymbolEqualityComparer.Default.Equals(type.OriginalDefinition,
            Compilation.GetTypeByMetadataName("System.Threading.Tasks.Task"))
        || SymbolEqualityComparer.Default.Equals(type.OriginalDefinition,
            Compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1"))
        || SymbolEqualityComparer.Default.Equals(type.OriginalDefinition,
            Compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask"))
        || SymbolEqualityComparer.Default.Equals(type.OriginalDefinition,
            Compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask`1"));

    internal bool IsAsyncOperator(IMethodSymbol method) =>
        SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, Error.ContainingAssembly)
        && method.ContainingNamespace.ToDisplayString() == "FlowOnRails.Extensions.Tasks"
        && method.Name.EndsWith("Async", System.StringComparison.Ordinal);

    internal FlowValue OpenContract(IMethodSymbol methodSymbol)
    {
        var errors = ErrorSet.Of(Error, false);

        foreach (var attribute in methodSymbol.GetAttributes())
        {
            if (attribute.AttributeClass is not { IsGenericType: true } attributeType
                || !SymbolEqualityComparer.Default.Equals(attributeType.BaseType, ResultsWith)
                || !SymbolEqualityComparer.Default.Equals(attributeType.ContainingAssembly, ResultsWith?.ContainingAssembly)
                || attributeType.Name != "ResultsWithAttribute")
                continue;

            foreach (var errorType in attributeType.TypeArguments)
                if (IsError(errorType))
                    errors = errors.Union(ErrorSet.Of(errorType, errorType.IsSealed));
        }

        return WrapReturnType(methodSymbol.ReturnType,
            IsRail(UnwrapReturnType(methodSymbol.ReturnType))
                ? FlowValue.Rail(errors, true, true)
                : FlowValue.Error(errors, nullable: true));
    }

    internal FlowValue WrapReturnType(ITypeSymbol type, FlowValue value) =>
        AwaitedType(type) is { } awaitedType
            ? FlowValue.Awaitable(WrapReturnType(awaitedType, value))
            : value;

    internal FlowValue Unknown(ITypeSymbol? type)
    {
        if (AwaitedType(type) is { } awaitedType)
            return FlowValue.Awaitable(Unknown(awaitedType));
        if (IsRail(type))
            return FlowValue.Rail(ErrorSet.Of(Error, false), true, true);

        if (IsError(type))
            return FlowValue.Error(ErrorSet.Of(type!, type!.IsSealed), nullable: true);

        if (type?.TypeKind == TypeKind.Delegate)
            return FlowValue.Callable([], unknown: true);

        return type?.SpecialType == SpecialType.System_Boolean
            ? FlowValue.Bool(null)
            : FlowValue.Other;
    }
}
