using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.IO;

using Microsoft.CodeAnalysis;

namespace FlowOnRails.Analysis.Internal;

internal sealed class MetadataSummaries
{
    private readonly LibrarySymbols _symbols;
    private readonly INamedTypeSymbol? _attributeType;
    private readonly ConcurrentDictionary<IAssemblySymbol, ImmutableDictionary<string, ImmutableArray<AttributeData>>> _indices =
        new(SymbolEqualityComparer.Default);

    internal MetadataSummaries(LibrarySymbols symbols)
    {
        _symbols = symbols;
        _attributeType = symbols.Compilation.GetTypeByMetadataName("FlowOnRails.FlowAnalysisSummaryAttribute");
    }

    internal MethodFlowResult? Read(IMethodSymbol method, FlowValue[] arguments, out ErrorFlowLimitation? limitation)
    {
        limitation = null;

        // Locally generated metadata is an export, never a substitute for a body
        // available in the analyzer's final compilation (including generated bodies).
        if (SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, _symbols.Compilation.Assembly))
            return null;

        if (_attributeType is null)
        {
            limitation = MissingMetadata();

            return null;
        }

        var methodId = DocumentationCommentId.CreateDeclarationId(method.OriginalDefinition);
        var index = _indices.GetOrAdd(method.ContainingAssembly, BuildIndex);

        if (methodId is null || !index.TryGetValue(methodId, out var entries))
        {
            limitation = MissingMetadata();

            return null;
        }

        if (entries.Length != 1)
        {
            limitation = new ErrorFlowLimitation(ErrorFlowLimitationCode.MetadataConflict,
                "Summary metadata has conflicting entries");

            return null;
        }

        if (entries[0].ConstructorArguments.Length != 3
            || entries[0].ConstructorArguments[0].Value is not int version
            || entries[0].ConstructorArguments[2].Value is not string payload)
        {
            limitation = new ErrorFlowLimitation(ErrorFlowLimitationCode.MetadataInvalid,
                "Summary metadata is invalid; the called result remains open");

            return null;
        }

        if (version is not (1 or 2 or SummaryCodec.SchemaVersion))
        {
            limitation = new ErrorFlowLimitation(ErrorFlowLimitationCode.MetadataUnsupportedVersion,
                "Summary metadata has an unsupported version");

            return null;
        }

        try
        {
            var result = SummaryCodec.Decode(payload, method, arguments, _symbols, version);

            if (!_symbols.MatchesReturnType(method.ReturnType, result.ReturnValue))
                throw new InvalidDataException("Summary return shape does not match the method.");

            foreach (var parameter in method.Parameters)
                if (parameter.RefKind is RefKind.Ref or RefKind.Out
                    && parameter.Ordinal < result.ParameterOutputs.Length
                    && !_symbols.MatchesReturnType(parameter.Type, result.ParameterOutputs[parameter.Ordinal]))
                    throw new InvalidDataException("Summary output shape does not match the parameter.");

            return result;
        }
        catch (Exception exception) when (exception is FormatException or IOException or InvalidDataException
            or ArgumentException or InvalidOperationException)
        {
            limitation = new ErrorFlowLimitation(ErrorFlowLimitationCode.MetadataInvalid,
                "Summary metadata is invalid; the called result remains open");

            return null;
        }
    }

    private static ErrorFlowLimitation MissingMetadata() =>
        new ErrorFlowLimitation(ErrorFlowLimitationCode.MetadataMissing,
            "Called method body is unavailable and no assembly summary was found");

    private ImmutableDictionary<string, ImmutableArray<AttributeData>> BuildIndex(IAssemblySymbol assembly)
    {
        var entries = ImmutableDictionary.CreateBuilder<string, ImmutableArray<AttributeData>>(StringComparer.Ordinal);

        foreach (var attribute in assembly.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, _attributeType)
                || attribute.ConstructorArguments.Length < 2
                || attribute.ConstructorArguments[1].Value is not string methodId)
                continue;

            entries[methodId] = entries.TryGetValue(methodId, out var existing)
                ? existing.Add(attribute)
                : [attribute];
        }

        return entries.ToImmutable();
    }
}
