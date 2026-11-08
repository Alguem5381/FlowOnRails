using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FlowOnRails.AspNetCore.Translation;

/// <summary>Configures the values of one exact error contract.</summary>
public sealed class ProblemBuilder<TError> where TError : Error
{
    private string? _type;
    private string? _title;
    private Func<TError, HttpContext, string?>? _detail;
    private Func<TError, HttpContext, string?>? _instance;
    private Action<TError, IDictionary<string, object?>>? _extensions;

    internal ProblemBuilder() { }

    /// <summary>Sets the problem type URI reference.</summary>
    public void Type(string value)
    {
        ProblemFields.ValidateUri(value);

        _type = value;
    }

    /// <summary>Sets the stable summary of this problem type.</summary>
    public void Title(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        _title = value;
    }

    /// <summary>Sets a fixed occurrence description.</summary>
    public void Detail(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        _detail = (_, _) => value;
    }

    /// <summary>Creates a description from the selected error contract.</summary>
    public void Detail(Func<TError, string?> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        _detail = (error, _) => factory(error);
    }

    /// <summary>Creates a description using the selected error and current request.</summary>
    public void Detail(Func<TError, HttpContext, string?> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        _detail = factory;
    }

    /// <summary>Sets the URI reference identifying an occurrence.</summary>
    public void Instance(string value)
    {
        ProblemFields.ValidateUri(value);

        _instance = (_, _) => value;
    }

    /// <summary>Creates an occurrence URI using the selected contract and current request.</summary>
    public void Instance(Func<TError, HttpContext, string?> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        _instance = factory;
    }

    /// <summary>Writes application-defined extension names and values.</summary>
    public void Extensions(Action<TError, IDictionary<string, object?>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        if (_extensions is not null)
            throw new InvalidOperationException("Configure manual extensions once per error.");

        _extensions = configure;
    }

    internal Func<Error, HttpContext, ProblemDetails> Build(
        int status,
        Action<TError, IDictionary<string, object?>>? automatic,
        bool useDataField)
    {
        if (automatic is not null && _extensions is not null)
            throw new InvalidOperationException("Automatic and manual extensions cannot be combined.");

        if (useDataField && automatic is null)
            throw new InvalidOperationException("A data field requires automatic extensions.");

        var fields = new ProblemFields(status, _type, _title);
        var detail = _detail;
        var instance = _instance;
        var extensions = _extensions;

        return (error, context) =>
        {
            var selected = (TError)error;
            var problem = fields.Create();
            problem.Detail = detail?.Invoke(selected, context);
            problem.Instance = instance?.Invoke(selected, context);

            if (automatic is not null)
            {
                var data = useDataField
                    ? new Dictionary<string, object?>(StringComparer.Ordinal)
                    : problem.Extensions;

                automatic(selected, data);

                if (useDataField)
                    problem.Extensions.Add("data", data);
            }
            else
                extensions?.Invoke(selected, problem.Extensions);

            return problem;
        };
    }
}
