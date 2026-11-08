using System.Collections.Frozen;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FlowOnRails.AspNetCore.Translation;

/// <summary>Builds the immutable exact mappings used by generated catalogs.</summary>
public sealed class TranslationBuilder
{
    private readonly Dictionary<Type, Func<Error, HttpContext, ProblemDetails>?> _mappings = [];
    private Func<HttpContext, ProblemDetails>? _generic;

    /// <summary>Records accessible closed contracts discovered by a generated catalog.</summary>
    public void Discover(params Type[] types)
    {
        ArgumentNullException.ThrowIfNull(types);

        foreach (var type in types)
        {
            ArgumentNullException.ThrowIfNull(type);

            if (!typeof(Error).IsAssignableFrom(type) || type.ContainsGenericParameters)
                throw new ArgumentException("Discovered contracts must be closed error types.", nameof(types));

            _mappings.TryAdd(type, null);
        }
    }

    /// <summary>Registers one exact mapping, with an optional generated property projector.</summary>
    public void Add<TError>(
        int status,
        Action<ProblemBuilder<TError>> configure,
        Action<TError, IDictionary<string, object?>>? automatic = null,
        bool useDataField = false)
        where TError : Error
    {
        ArgumentNullException.ThrowIfNull(configure);
        ProblemFields.ValidateStatus(status);

        if (_mappings.TryGetValue(typeof(TError), out var existing) && existing is not null)
            throw new InvalidOperationException($"A mapping for '{typeof(TError)}' is already registered.");

        var builder = new ProblemBuilder<TError>();

        configure(builder);
        _mappings[typeof(TError)] = builder.Build(status, automatic, useDataField);
    }

    /// <summary>Registers the single configurable generic problem.</summary>
    public void Generic(int status, Action<GenericProblemBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        ProblemFields.ValidateStatus(status);

        if (_generic is not null)
            throw new InvalidOperationException("A generic problem is already configured.");

        var builder = new GenericProblemBuilder();

        configure(builder);
        _generic = builder.Build(status);
    }

    /// <summary>Creates an immutable snapshot without evaluating occurrence factories.</summary>
    public ProblemTranslator Build() => new(
        _mappings.ToFrozenDictionary(),
        _generic ?? new GenericProblemBuilder().Build(StatusCodes.Status500InternalServerError));
}
