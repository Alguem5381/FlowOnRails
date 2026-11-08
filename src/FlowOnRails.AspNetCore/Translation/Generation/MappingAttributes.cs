namespace FlowOnRails.AspNetCore.Translation;

/// <summary>Declares the HTTP status of an exact error configuration method.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class HttpProblemAttribute(int status) : Attribute
{
    /// <summary>The response status, from 400 through 599.</summary>
    public int Status { get; } = status;
}

/// <summary>Copies eligible public properties of the selected error contract.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class AutomaticExtensionsAttribute : Attribute;

/// <summary>Groups automatic properties inside a data extension.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class UseDataFieldAttribute : Attribute;

/// <summary>Includes the assembly containing a marker in error discovery.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ErrorAssemblyAttribute(Type marker) : Attribute
{
    /// <summary>A type identifying the assembly to inspect.</summary>
    public Type Marker { get; } = marker;
}

/// <summary>Declares the configurable fallback problem for a catalog.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class GenericProblemAttribute(int status = 500) : Attribute
{
    /// <summary>The fallback status, from 400 through 599.</summary>
    public int Status { get; } = status;
}
