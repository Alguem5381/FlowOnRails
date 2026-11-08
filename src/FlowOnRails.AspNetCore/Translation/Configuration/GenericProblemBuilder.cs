using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FlowOnRails.AspNetCore.Translation;

/// <summary>Configures a fallback without access to a hidden domain error.</summary>
public sealed class GenericProblemBuilder
{
    private string? _type;
    private string? _title;
    private Func<HttpContext, string?>? _detail;
    private Func<HttpContext, string?>? _instance;
    private Action<HttpContext, IDictionary<string, object?>>? _extensions;

    internal GenericProblemBuilder() { }

    /// <summary>Sets the generic problem type URI reference.</summary>
    public void Type(string value)
    {
        ProblemFields.ValidateUri(value);

        _type = value;
    }

    /// <summary>Sets the stable summary of the generic problem.</summary>
    public void Title(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        _title = value;
    }

    /// <summary>Sets a fixed generic description.</summary>
    public void Detail(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        _detail = _ => value;
    }

    /// <summary>Creates a description using the current request.</summary>
    public void Detail(Func<HttpContext, string?> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        _detail = factory;
    }

    /// <summary>Sets the URI reference identifying an occurrence.</summary>
    public void Instance(string value)
    {
        ProblemFields.ValidateUri(value);

        _instance = _ => value;
    }

    /// <summary>Creates an occurrence URI using the current request.</summary>
    public void Instance(Func<HttpContext, string?> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        _instance = factory;
    }

    /// <summary>Writes request-specific extensions into a fresh problem.</summary>
    public void Extensions(Action<HttpContext, IDictionary<string, object?>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        if (_extensions is not null)
            throw new InvalidOperationException("Configure generic extensions once.");

        _extensions = configure;
    }

    internal Func<HttpContext, ProblemDetails> Build(int status)
    {
        var fields = new ProblemFields(status, _type, _title);
        var detail = _detail;
        var instance = _instance;
        var extensions = _extensions;

        return context =>
        {
            var problem = fields.Create();
            problem.Detail = detail?.Invoke(context);
            problem.Instance = instance?.Invoke(context);
            extensions?.Invoke(context, problem.Extensions);

            return problem;
        };
    }
}
