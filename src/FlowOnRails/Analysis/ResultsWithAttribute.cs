namespace FlowOnRails;

/// <summary>
/// Declares known returned error types. Additional errors may still be returned;
/// this annotation never establishes an exhaustive interface contract.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public abstract class ResultsWithAttribute : Attribute
{
    /// <summary>Gets the error types declared by this annotation.</summary>
    public IReadOnlyList<Type> ErrorTypes { get; }

    /// <summary>Initializes an annotation with its declared error types.</summary>
    /// <param name="errorTypes">The declared types derived from <see cref="Error"/>.</param>
    protected ResultsWithAttribute(params Type[] errorTypes) =>
        ErrorTypes = Array.AsReadOnly(errorTypes);
}

/// <summary>Declares known returned errors without excluding other error types.</summary>
/// <typeparam name="TError">A known returned error type.</typeparam>
public sealed class ResultsWithAttribute<TError> : ResultsWithAttribute
    where TError : Error
{
    /// <summary>Initializes the annotation with its generic error types.</summary>
    public ResultsWithAttribute() : base(typeof(TError)) { }
}

/// <summary>Declares known returned errors without excluding other error types.</summary>
/// <typeparam name="TError1">A known returned error type.</typeparam>
/// <typeparam name="TError2">A known returned error type.</typeparam>
public sealed class ResultsWithAttribute<TError1, TError2> : ResultsWithAttribute
    where TError1 : Error
    where TError2 : Error
{
    /// <summary>Initializes the annotation with its generic error types.</summary>
    public ResultsWithAttribute() : base(typeof(TError1), typeof(TError2)) { }
}

/// <summary>Declares known returned errors without excluding other error types.</summary>
/// <typeparam name="TError1">A known returned error type.</typeparam>
/// <typeparam name="TError2">A known returned error type.</typeparam>
/// <typeparam name="TError3">A known returned error type.</typeparam>
public sealed class ResultsWithAttribute<TError1, TError2, TError3> : ResultsWithAttribute
    where TError1 : Error
    where TError2 : Error
    where TError3 : Error
{
    /// <summary>Initializes the annotation with its generic error types.</summary>
    public ResultsWithAttribute() : base(typeof(TError1), typeof(TError2), typeof(TError3)) { }
}

/// <summary>Declares known returned errors without excluding other error types.</summary>
/// <typeparam name="TError1">A known returned error type.</typeparam>
/// <typeparam name="TError2">A known returned error type.</typeparam>
/// <typeparam name="TError3">A known returned error type.</typeparam>
/// <typeparam name="TError4">A known returned error type.</typeparam>
public sealed class ResultsWithAttribute
    <
        TError1,
        TError2,
        TError3,
        TError4
    > : ResultsWithAttribute
    where TError1 : Error
    where TError2 : Error
    where TError3 : Error
    where TError4 : Error
{
    /// <summary>Initializes the annotation with its generic error types.</summary>
    public ResultsWithAttribute() : base(
        typeof(TError1),
        typeof(TError2),
        typeof(TError3),
        typeof(TError4)) { }
}

/// <summary>Declares known returned errors without excluding other error types.</summary>
/// <typeparam name="TError1">A known returned error type.</typeparam>
/// <typeparam name="TError2">A known returned error type.</typeparam>
/// <typeparam name="TError3">A known returned error type.</typeparam>
/// <typeparam name="TError4">A known returned error type.</typeparam>
/// <typeparam name="TError5">A known returned error type.</typeparam>
public sealed class ResultsWithAttribute
    <
        TError1,
        TError2,
        TError3,
        TError4,
        TError5
    > : ResultsWithAttribute
    where TError1 : Error
    where TError2 : Error
    where TError3 : Error
    where TError4 : Error
    where TError5 : Error
{
    /// <summary>Initializes the annotation with its generic error types.</summary>
    public ResultsWithAttribute() : base(
        typeof(TError1),
        typeof(TError2),
        typeof(TError3),
        typeof(TError4),
        typeof(TError5)) { }
}

/// <summary>Declares known returned errors without excluding other error types.</summary>
/// <typeparam name="TError1">A known returned error type.</typeparam>
/// <typeparam name="TError2">A known returned error type.</typeparam>
/// <typeparam name="TError3">A known returned error type.</typeparam>
/// <typeparam name="TError4">A known returned error type.</typeparam>
/// <typeparam name="TError5">A known returned error type.</typeparam>
/// <typeparam name="TError6">A known returned error type.</typeparam>
public sealed class ResultsWithAttribute
    <
        TError1,
        TError2,
        TError3,
        TError4,
        TError5,
        TError6
    > : ResultsWithAttribute
    where TError1 : Error
    where TError2 : Error
    where TError3 : Error
    where TError4 : Error
    where TError5 : Error
    where TError6 : Error
{
    /// <summary>Initializes the annotation with its generic error types.</summary>
    public ResultsWithAttribute() : base(
        typeof(TError1),
        typeof(TError2),
        typeof(TError3),
        typeof(TError4),
        typeof(TError5),
        typeof(TError6)) { }
}

/// <summary>Declares known returned errors without excluding other error types.</summary>
/// <typeparam name="TError1">A known returned error type.</typeparam>
/// <typeparam name="TError2">A known returned error type.</typeparam>
/// <typeparam name="TError3">A known returned error type.</typeparam>
/// <typeparam name="TError4">A known returned error type.</typeparam>
/// <typeparam name="TError5">A known returned error type.</typeparam>
/// <typeparam name="TError6">A known returned error type.</typeparam>
/// <typeparam name="TError7">A known returned error type.</typeparam>
public sealed class ResultsWithAttribute
    <
        TError1,
        TError2,
        TError3,
        TError4,
        TError5,
        TError6,
        TError7
    > : ResultsWithAttribute
    where TError1 : Error
    where TError2 : Error
    where TError3 : Error
    where TError4 : Error
    where TError5 : Error
    where TError6 : Error
    where TError7 : Error
{
    /// <summary>Initializes the annotation with its generic error types.</summary>
    public ResultsWithAttribute() : base(
        typeof(TError1),
        typeof(TError2),
        typeof(TError3),
        typeof(TError4),
        typeof(TError5),
        typeof(TError6),
        typeof(TError7)) { }
}

/// <summary>Declares known returned errors without excluding other error types.</summary>
/// <typeparam name="TError1">A known returned error type.</typeparam>
/// <typeparam name="TError2">A known returned error type.</typeparam>
/// <typeparam name="TError3">A known returned error type.</typeparam>
/// <typeparam name="TError4">A known returned error type.</typeparam>
/// <typeparam name="TError5">A known returned error type.</typeparam>
/// <typeparam name="TError6">A known returned error type.</typeparam>
/// <typeparam name="TError7">A known returned error type.</typeparam>
/// <typeparam name="TError8">A known returned error type.</typeparam>
public sealed class ResultsWithAttribute
    <
        TError1,
        TError2,
        TError3,
        TError4,
        TError5,
        TError6,
        TError7,
        TError8
    > : ResultsWithAttribute
    where TError1 : Error
    where TError2 : Error
    where TError3 : Error
    where TError4 : Error
    where TError5 : Error
    where TError6 : Error
    where TError7 : Error
    where TError8 : Error
{
    /// <summary>Initializes the annotation with its generic error types.</summary>
    public ResultsWithAttribute() : base(
        typeof(TError1),
        typeof(TError2),
        typeof(TError3),
        typeof(TError4),
        typeof(TError5),
        typeof(TError6),
        typeof(TError7),
        typeof(TError8)) { }
}

/// <summary>Declares known returned errors without excluding other error types.</summary>
/// <typeparam name="TError1">A known returned error type.</typeparam>
/// <typeparam name="TError2">A known returned error type.</typeparam>
/// <typeparam name="TError3">A known returned error type.</typeparam>
/// <typeparam name="TError4">A known returned error type.</typeparam>
/// <typeparam name="TError5">A known returned error type.</typeparam>
/// <typeparam name="TError6">A known returned error type.</typeparam>
/// <typeparam name="TError7">A known returned error type.</typeparam>
/// <typeparam name="TError8">A known returned error type.</typeparam>
/// <typeparam name="TError9">A known returned error type.</typeparam>
public sealed class ResultsWithAttribute
    <
        TError1,
        TError2,
        TError3,
        TError4,
        TError5,
        TError6,
        TError7,
        TError8,
        TError9
    > : ResultsWithAttribute
    where TError1 : Error
    where TError2 : Error
    where TError3 : Error
    where TError4 : Error
    where TError5 : Error
    where TError6 : Error
    where TError7 : Error
    where TError8 : Error
    where TError9 : Error
{
    /// <summary>Initializes the annotation with its generic error types.</summary>
    public ResultsWithAttribute() : base(
        typeof(TError1),
        typeof(TError2),
        typeof(TError3),
        typeof(TError4),
        typeof(TError5),
        typeof(TError6),
        typeof(TError7),
        typeof(TError8),
        typeof(TError9)) { }
}

/// <summary>Declares known returned errors without excluding other error types.</summary>
/// <typeparam name="TError1">A known returned error type.</typeparam>
/// <typeparam name="TError2">A known returned error type.</typeparam>
/// <typeparam name="TError3">A known returned error type.</typeparam>
/// <typeparam name="TError4">A known returned error type.</typeparam>
/// <typeparam name="TError5">A known returned error type.</typeparam>
/// <typeparam name="TError6">A known returned error type.</typeparam>
/// <typeparam name="TError7">A known returned error type.</typeparam>
/// <typeparam name="TError8">A known returned error type.</typeparam>
/// <typeparam name="TError9">A known returned error type.</typeparam>
/// <typeparam name="TError10">A known returned error type.</typeparam>
public sealed class ResultsWithAttribute
    <
        TError1,
        TError2,
        TError3,
        TError4,
        TError5,
        TError6,
        TError7,
        TError8,
        TError9,
        TError10
    > : ResultsWithAttribute
    where TError1 : Error
    where TError2 : Error
    where TError3 : Error
    where TError4 : Error
    where TError5 : Error
    where TError6 : Error
    where TError7 : Error
    where TError8 : Error
    where TError9 : Error
    where TError10 : Error
{
    /// <summary>Initializes the annotation with its generic error types.</summary>
    public ResultsWithAttribute() : base(
        typeof(TError1),
        typeof(TError2),
        typeof(TError3),
        typeof(TError4),
        typeof(TError5),
        typeof(TError6),
        typeof(TError7),
        typeof(TError8),
        typeof(TError9),
        typeof(TError10)) { }
}

/// <summary>Declares known returned errors without excluding other error types.</summary>
/// <typeparam name="TError1">A known returned error type.</typeparam>
/// <typeparam name="TError2">A known returned error type.</typeparam>
/// <typeparam name="TError3">A known returned error type.</typeparam>
/// <typeparam name="TError4">A known returned error type.</typeparam>
/// <typeparam name="TError5">A known returned error type.</typeparam>
/// <typeparam name="TError6">A known returned error type.</typeparam>
/// <typeparam name="TError7">A known returned error type.</typeparam>
/// <typeparam name="TError8">A known returned error type.</typeparam>
/// <typeparam name="TError9">A known returned error type.</typeparam>
/// <typeparam name="TError10">A known returned error type.</typeparam>
/// <typeparam name="TError11">A known returned error type.</typeparam>
public sealed class ResultsWithAttribute
    <
        TError1,
        TError2,
        TError3,
        TError4,
        TError5,
        TError6,
        TError7,
        TError8,
        TError9,
        TError10,
        TError11
    > : ResultsWithAttribute
    where TError1 : Error
    where TError2 : Error
    where TError3 : Error
    where TError4 : Error
    where TError5 : Error
    where TError6 : Error
    where TError7 : Error
    where TError8 : Error
    where TError9 : Error
    where TError10 : Error
    where TError11 : Error
{
    /// <summary>Initializes the annotation with its generic error types.</summary>
    public ResultsWithAttribute() : base(
        typeof(TError1),
        typeof(TError2),
        typeof(TError3),
        typeof(TError4),
        typeof(TError5),
        typeof(TError6),
        typeof(TError7),
        typeof(TError8),
        typeof(TError9),
        typeof(TError10),
        typeof(TError11)) { }
}

/// <summary>Declares known returned errors without excluding other error types.</summary>
/// <typeparam name="TError1">A known returned error type.</typeparam>
/// <typeparam name="TError2">A known returned error type.</typeparam>
/// <typeparam name="TError3">A known returned error type.</typeparam>
/// <typeparam name="TError4">A known returned error type.</typeparam>
/// <typeparam name="TError5">A known returned error type.</typeparam>
/// <typeparam name="TError6">A known returned error type.</typeparam>
/// <typeparam name="TError7">A known returned error type.</typeparam>
/// <typeparam name="TError8">A known returned error type.</typeparam>
/// <typeparam name="TError9">A known returned error type.</typeparam>
/// <typeparam name="TError10">A known returned error type.</typeparam>
/// <typeparam name="TError11">A known returned error type.</typeparam>
/// <typeparam name="TError12">A known returned error type.</typeparam>
public sealed class ResultsWithAttribute
    <
        TError1,
        TError2,
        TError3,
        TError4,
        TError5,
        TError6,
        TError7,
        TError8,
        TError9,
        TError10,
        TError11,
        TError12
    > : ResultsWithAttribute
    where TError1 : Error
    where TError2 : Error
    where TError3 : Error
    where TError4 : Error
    where TError5 : Error
    where TError6 : Error
    where TError7 : Error
    where TError8 : Error
    where TError9 : Error
    where TError10 : Error
    where TError11 : Error
    where TError12 : Error
{
    /// <summary>Initializes the annotation with its generic error types.</summary>
    public ResultsWithAttribute() : base(
        typeof(TError1),
        typeof(TError2),
        typeof(TError3),
        typeof(TError4),
        typeof(TError5),
        typeof(TError6),
        typeof(TError7),
        typeof(TError8),
        typeof(TError9),
        typeof(TError10),
        typeof(TError11),
        typeof(TError12)) { }
}

/// <summary>Declares known returned errors without excluding other error types.</summary>
/// <typeparam name="TError1">A known returned error type.</typeparam>
/// <typeparam name="TError2">A known returned error type.</typeparam>
/// <typeparam name="TError3">A known returned error type.</typeparam>
/// <typeparam name="TError4">A known returned error type.</typeparam>
/// <typeparam name="TError5">A known returned error type.</typeparam>
/// <typeparam name="TError6">A known returned error type.</typeparam>
/// <typeparam name="TError7">A known returned error type.</typeparam>
/// <typeparam name="TError8">A known returned error type.</typeparam>
/// <typeparam name="TError9">A known returned error type.</typeparam>
/// <typeparam name="TError10">A known returned error type.</typeparam>
/// <typeparam name="TError11">A known returned error type.</typeparam>
/// <typeparam name="TError12">A known returned error type.</typeparam>
/// <typeparam name="TError13">A known returned error type.</typeparam>
public sealed class ResultsWithAttribute
    <
        TError1,
        TError2,
        TError3,
        TError4,
        TError5,
        TError6,
        TError7,
        TError8,
        TError9,
        TError10,
        TError11,
        TError12,
        TError13
    > : ResultsWithAttribute
    where TError1 : Error
    where TError2 : Error
    where TError3 : Error
    where TError4 : Error
    where TError5 : Error
    where TError6 : Error
    where TError7 : Error
    where TError8 : Error
    where TError9 : Error
    where TError10 : Error
    where TError11 : Error
    where TError12 : Error
    where TError13 : Error
{
    /// <summary>Initializes the annotation with its generic error types.</summary>
    public ResultsWithAttribute() : base(
        typeof(TError1),
        typeof(TError2),
        typeof(TError3),
        typeof(TError4),
        typeof(TError5),
        typeof(TError6),
        typeof(TError7),
        typeof(TError8),
        typeof(TError9),
        typeof(TError10),
        typeof(TError11),
        typeof(TError12),
        typeof(TError13)) { }
}

/// <summary>Declares known returned errors without excluding other error types.</summary>
/// <typeparam name="TError1">A known returned error type.</typeparam>
/// <typeparam name="TError2">A known returned error type.</typeparam>
/// <typeparam name="TError3">A known returned error type.</typeparam>
/// <typeparam name="TError4">A known returned error type.</typeparam>
/// <typeparam name="TError5">A known returned error type.</typeparam>
/// <typeparam name="TError6">A known returned error type.</typeparam>
/// <typeparam name="TError7">A known returned error type.</typeparam>
/// <typeparam name="TError8">A known returned error type.</typeparam>
/// <typeparam name="TError9">A known returned error type.</typeparam>
/// <typeparam name="TError10">A known returned error type.</typeparam>
/// <typeparam name="TError11">A known returned error type.</typeparam>
/// <typeparam name="TError12">A known returned error type.</typeparam>
/// <typeparam name="TError13">A known returned error type.</typeparam>
/// <typeparam name="TError14">A known returned error type.</typeparam>
public sealed class ResultsWithAttribute
    <
        TError1,
        TError2,
        TError3,
        TError4,
        TError5,
        TError6,
        TError7,
        TError8,
        TError9,
        TError10,
        TError11,
        TError12,
        TError13,
        TError14
    > : ResultsWithAttribute
    where TError1 : Error
    where TError2 : Error
    where TError3 : Error
    where TError4 : Error
    where TError5 : Error
    where TError6 : Error
    where TError7 : Error
    where TError8 : Error
    where TError9 : Error
    where TError10 : Error
    where TError11 : Error
    where TError12 : Error
    where TError13 : Error
    where TError14 : Error
{
    /// <summary>Initializes the annotation with its generic error types.</summary>
    public ResultsWithAttribute() : base(
        typeof(TError1),
        typeof(TError2),
        typeof(TError3),
        typeof(TError4),
        typeof(TError5),
        typeof(TError6),
        typeof(TError7),
        typeof(TError8),
        typeof(TError9),
        typeof(TError10),
        typeof(TError11),
        typeof(TError12),
        typeof(TError13),
        typeof(TError14)) { }
}

/// <summary>Declares known returned errors without excluding other error types.</summary>
/// <typeparam name="TError1">A known returned error type.</typeparam>
/// <typeparam name="TError2">A known returned error type.</typeparam>
/// <typeparam name="TError3">A known returned error type.</typeparam>
/// <typeparam name="TError4">A known returned error type.</typeparam>
/// <typeparam name="TError5">A known returned error type.</typeparam>
/// <typeparam name="TError6">A known returned error type.</typeparam>
/// <typeparam name="TError7">A known returned error type.</typeparam>
/// <typeparam name="TError8">A known returned error type.</typeparam>
/// <typeparam name="TError9">A known returned error type.</typeparam>
/// <typeparam name="TError10">A known returned error type.</typeparam>
/// <typeparam name="TError11">A known returned error type.</typeparam>
/// <typeparam name="TError12">A known returned error type.</typeparam>
/// <typeparam name="TError13">A known returned error type.</typeparam>
/// <typeparam name="TError14">A known returned error type.</typeparam>
/// <typeparam name="TError15">A known returned error type.</typeparam>
public sealed class ResultsWithAttribute
    <
        TError1,
        TError2,
        TError3,
        TError4,
        TError5,
        TError6,
        TError7,
        TError8,
        TError9,
        TError10,
        TError11,
        TError12,
        TError13,
        TError14,
        TError15
    > : ResultsWithAttribute
    where TError1 : Error
    where TError2 : Error
    where TError3 : Error
    where TError4 : Error
    where TError5 : Error
    where TError6 : Error
    where TError7 : Error
    where TError8 : Error
    where TError9 : Error
    where TError10 : Error
    where TError11 : Error
    where TError12 : Error
    where TError13 : Error
    where TError14 : Error
    where TError15 : Error
{
    /// <summary>Initializes the annotation with its generic error types.</summary>
    public ResultsWithAttribute() : base(
        typeof(TError1),
        typeof(TError2),
        typeof(TError3),
        typeof(TError4),
        typeof(TError5),
        typeof(TError6),
        typeof(TError7),
        typeof(TError8),
        typeof(TError9),
        typeof(TError10),
        typeof(TError11),
        typeof(TError12),
        typeof(TError13),
        typeof(TError14),
        typeof(TError15)) { }
}

/// <summary>Declares known returned errors without excluding other error types.</summary>
/// <typeparam name="TError1">A known returned error type.</typeparam>
/// <typeparam name="TError2">A known returned error type.</typeparam>
/// <typeparam name="TError3">A known returned error type.</typeparam>
/// <typeparam name="TError4">A known returned error type.</typeparam>
/// <typeparam name="TError5">A known returned error type.</typeparam>
/// <typeparam name="TError6">A known returned error type.</typeparam>
/// <typeparam name="TError7">A known returned error type.</typeparam>
/// <typeparam name="TError8">A known returned error type.</typeparam>
/// <typeparam name="TError9">A known returned error type.</typeparam>
/// <typeparam name="TError10">A known returned error type.</typeparam>
/// <typeparam name="TError11">A known returned error type.</typeparam>
/// <typeparam name="TError12">A known returned error type.</typeparam>
/// <typeparam name="TError13">A known returned error type.</typeparam>
/// <typeparam name="TError14">A known returned error type.</typeparam>
/// <typeparam name="TError15">A known returned error type.</typeparam>
/// <typeparam name="TError16">A known returned error type.</typeparam>
public sealed class ResultsWithAttribute
    <
        TError1,
        TError2,
        TError3,
        TError4,
        TError5,
        TError6,
        TError7,
        TError8,
        TError9,
        TError10,
        TError11,
        TError12,
        TError13,
        TError14,
        TError15,
        TError16
    > : ResultsWithAttribute
    where TError1 : Error
    where TError2 : Error
    where TError3 : Error
    where TError4 : Error
    where TError5 : Error
    where TError6 : Error
    where TError7 : Error
    where TError8 : Error
    where TError9 : Error
    where TError10 : Error
    where TError11 : Error
    where TError12 : Error
    where TError13 : Error
    where TError14 : Error
    where TError15 : Error
    where TError16 : Error
{
    /// <summary>Initializes the annotation with its generic error types.</summary>
    public ResultsWithAttribute() : base(
        typeof(TError1),
        typeof(TError2),
        typeof(TError3),
        typeof(TError4),
        typeof(TError5),
        typeof(TError6),
        typeof(TError7),
        typeof(TError8),
        typeof(TError9),
        typeof(TError10),
        typeof(TError11),
        typeof(TError12),
        typeof(TError13),
        typeof(TError14),
        typeof(TError15),
        typeof(TError16)) { }
}
