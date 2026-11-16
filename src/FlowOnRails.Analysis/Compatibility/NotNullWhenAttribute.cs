using System;

namespace System.Diagnostics.CodeAnalysis;

// netstandard2.0 lacks this compiler-recognized nullable flow annotation. Keep
// the polyfill internal; callers still receive the Try methods' nullability contract.
[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
internal sealed class NotNullWhenAttribute : Attribute
{
    public bool ReturnValue { get; }

    public NotNullWhenAttribute(bool returnValue) => ReturnValue = returnValue;
}
