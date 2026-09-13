namespace FlowOnRails;

public readonly partial struct RailFor<TValue> where TValue : notnull
{
    /// <summary>
    /// Enables the use of multiple 'from' clauses in LINQ query syntax with <see cref="RailFor{TValue}"/>.
    /// Binds the current rail to a new rail and projects both values into a final rail.
    /// </summary>
    /// <typeparam name="TIntermediate">The type of the intermediate value returned by the bind function.</typeparam>
    /// <typeparam name="TResult">The type of the final projected value.</typeparam>
    /// <param name="bind">A function that takes the current value and returns a new <see cref="RailFor{TIntermediate}"/>.</param>
    /// <param name="project">A function that combines the current value and the intermediate value into the final rail.</param>
    /// <returns>A successful rail containing the projected value if both operations succeed; otherwise, the first failure encountered.</returns>
    public RailFor<TResult> SelectMany<TIntermediate, TResult>(
        Func<TValue, RailFor<TIntermediate>> bind,
        Func<TValue, TIntermediate, TResult> project)
        where TIntermediate : notnull
        where TResult : notnull =>
        Bind(val => bind(val).Map(intermediate => project(val, intermediate)));

    /// <summary>
    /// Enables the use of 'select' clauses in LINQ query syntax with <see cref="RailFor{TValue}"/>.
    /// Projects the current value into a new form.
    /// </summary>
    /// <typeparam name="TResult">The type of the new value.</typeparam>
    /// <param name="map">A transform function to apply to the current value.</param>
    /// <returns>A successful rail containing the transformed value if the current rail is successful; otherwise, a failed rail.</returns>
    public RailFor<TResult> Select<TResult>(Func<TValue, TResult> map) where TResult : notnull =>
        Map(map);
}
