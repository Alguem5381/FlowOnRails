using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;

using FlowOnRails.Analysis.Internal;
using Microsoft.CodeAnalysis;

namespace FlowOnRails.Analysis;

/// <summary>Queries method error flow in one immutable Roslyn compilation.</summary>
/// <remarks>
/// Reuse an instance within a compilation. Create another when the compilation changes.
/// Concurrent queries on the same instance are supported.
/// Queries do not depend on diagnostic severities or the MSBuild analysis opt-in.
/// </remarks>
public sealed class ErrorFlowAnalysis
{
    private readonly LibrarySymbols _symbols;
    private readonly MethodBodyResolver _bodies;

    /// <summary>Creates an analysis context for a compilation referencing FlowOnRails.</summary>
    /// <exception cref="ArgumentNullException">The compilation is null.</exception>
    /// <exception cref="ArgumentException">The compilation lacks the required rail and error types.</exception>
    public ErrorFlowAnalysis(Compilation compilation)
    {
        if (compilation is null)
            throw new ArgumentNullException(nameof(compilation));

        _symbols = LibrarySymbols.Create(compilation)
            ?? throw new ArgumentException("The compilation must reference FlowOnRails.", nameof(compilation));
        _bodies = new MethodBodyResolver(compilation);
    }

    private ErrorFlowAnalysis(LibrarySymbols symbols)
    {
        _symbols = symbols;
        _bodies = new MethodBodyResolver(symbols.Compilation);
    }

    /// <summary>Creates a context when the compilation references the required FlowOnRails types.</summary>
    /// <returns>True with a non-null context; false with null when the required types are absent or ambiguous.</returns>
    /// <exception cref="ArgumentNullException">The compilation is null.</exception>
    public static bool TryCreate(Compilation compilation, [NotNullWhen(true)] out ErrorFlowAnalysis? analysis)
    {
        if (compilation is null)
            throw new ArgumentNullException(nameof(compilation));

        var symbols = LibrarySymbols.Create(compilation);
        analysis = symbols is null ? null : new ErrorFlowAnalysis(symbols);

        return analysis is not null;
    }

    /// <summary>Checks whether a method belongs to this compilation or its references and has a supported return type.</summary>
    /// <remarks>
    /// Returns false for null, foreign symbols and unsupported returns. An unavailable
    /// body or open contract remains analyzable and produces a conservative summary.
    /// This check does not execute flow analysis.
    /// </remarks>
    public bool CanAnalyze([NotNullWhen(true)] IMethodSymbol? method) =>
        method is not null && _symbols.IsTrackedReturn(method.ReturnType) && BelongsToCompilation(method);

    /// <summary>Analyzes a supported method, returning false for null, foreign symbols or unsupported returns.</summary>
    /// <remarks>
    /// Open contracts, unavailable bodies and invalid metadata still produce a non-null,
    /// conservative summary and return true. Cancellation and unexpected failures propagate.
    /// </remarks>
    /// <returns>True with a non-null summary; false with null when the method cannot be queried.</returns>
    /// <exception cref="OperationCanceledException">The supported query was canceled.</exception>
    public bool TryAnalyzeMethod(
        IMethodSymbol? method,
        [NotNullWhen(true)] out ErrorFlowSummary? summary,
        CancellationToken cancellationToken = default)
    {
        summary = null;

        if (method is null || !CanAnalyze(method))
            return false;

        summary = AnalyzeMethod(method, cancellationToken);

        return true;
    }

    /// <summary>Analyzes a method returning Rail, RailFor, Error or a supported awaitable of them.</summary>
    /// <remarks>
    /// Uses unknown parameter inputs. Concrete local bodies are interpreted; referenced
    /// assemblies use exported metadata. Interface and unresolved virtual contracts stay open.
    /// This preview API does not expose contextual call-site argument specialization.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The method is null.</exception>
    /// <exception cref="ArgumentException">The method belongs to another compilation or has an unsupported return type.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public ErrorFlowSummary AnalyzeMethod(IMethodSymbol method, CancellationToken cancellationToken = default)
    {
        if (method is null)
            throw new ArgumentNullException(nameof(method));

        if (!_symbols.IsTrackedReturn(method.ReturnType))
            throw new ArgumentException("The method must return a rail or Error, optionally in Task/ValueTask.", nameof(method));

        if (!BelongsToCompilation(method))
            throw new ArgumentException("Use a method symbol obtained from this compilation or its references.", nameof(method));

        cancellationToken.ThrowIfCancellationRequested();

        if (method.ContainingType.TypeKind == TypeKind.Interface || method.IsAbstract
            || (method.IsVirtual || method.IsOverride) && !method.IsSealed && !method.ContainingType.IsSealed)
            return OpenContract(method, new ErrorFlowLimitation(ErrorFlowLimitationCode.OpenDispatch,
                "Interface or virtual dispatch may return additional errors"));

        var graph = _bodies.GetGraph(method, cancellationToken);

        if (graph is not null)
            return new ErrorFlowSummary(MethodFlowAnalysis.Analyze(
                _symbols, _bodies, graph, method, cancellationToken), _symbols, ErrorFlowSummaryOrigin.SourceBody);

        var arguments = method.Parameters.Select(parameter => _symbols.Unknown(parameter.Type)).ToArray();
        var metadata = _bodies.Metadata.Read(method, arguments, out var limitation);

        return metadata is not null
            ? new ErrorFlowSummary(metadata, _symbols, ErrorFlowSummaryOrigin.AssemblyMetadata)
            : OpenContract(method, limitation ?? new ErrorFlowLimitation(ErrorFlowLimitationCode.BodyUnavailable,
                "Called method body is unavailable in this compilation"));
    }

    private ErrorFlowSummary OpenContract(IMethodSymbol method, ErrorFlowLimitation limitation) =>
        new ErrorFlowSummary(new MethodFlowResult(_symbols.OpenContract(method), false, [limitation]),
            _symbols, ErrorFlowSummaryOrigin.OpenContract);

    private bool BelongsToCompilation(IMethodSymbol method) =>
        SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, _symbols.Compilation.Assembly)
        || _symbols.Compilation.References.Any(reference => SymbolEqualityComparer.Default.Equals(
            _symbols.Compilation.GetAssemblyOrModuleSymbol(reference), method.ContainingAssembly));
}
