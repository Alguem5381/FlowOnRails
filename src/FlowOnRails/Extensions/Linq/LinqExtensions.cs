namespace FlowOnRails.Extensions.Linq;

using FlowOnRails;

/// <summary>
/// Provides LINQ extension methods for collections of results.
/// </summary>
public static class LinqExtensions
{
    extension<T>(IEnumerable<RailFor<T>> results) where T : notnull
    {
        /// <summary>
        /// Combines a sequence of results into a single rail containing a list of values.
        /// If any rail in the sequence is a failure, the combination short-circuits and returns the first encountered failure.
        /// </summary>
        /// <returns>A successful rail containing a read-only list of values if all results succeed; otherwise, a failed rail with the first error.</returns>
        /// <example>
        /// <code>
        /// var results = new[] { Rail.Ok(1), Rail.Ok(2) };
        /// var combined = results.Combine();
        /// </code>
        /// </example>
        public RailFor<IReadOnlyList<T>> Combine()
        {
            var list = new List<T>();

            foreach (var rail in results)
            {
                if (rail.IsFailure)
                    return RailFor<IReadOnlyList<T>>.Fail(rail.Error!);

                list.Add(rail.Value);
            }

            return RailFor<IReadOnlyList<T>>.Ok(list);
        }

        /// <summary>
        /// Filters the sequence of results, returning only the values of successful results.
        /// </summary>
        /// <example>
        /// <code>
        /// var validValues = results.SelectOks();
        /// </code>
        /// </example>
        public IEnumerable<T> SelectOks() =>
            from rail in results where rail.IsSuccess select rail.Value;

        /// <summary>
        /// Filters the sequence of results, returning only the errors of failed results.
        /// </summary>
        public IEnumerable<Error> SelectErrors() =>
            from rail in results where rail.IsFailure select rail.Error!;

        /// <summary>
        /// Partitions the sequence of results into a tuple containing a list of successful values and a list of errors.
        /// </summary>
        public (IReadOnlyList<T> Oks, IReadOnlyList<Error> Errors) Partition()
        {
            var oks = new List<T>();
            var errors = new List<Error>();

            foreach (var rail in results)
                if (rail.IsSuccess)
                    oks.Add(rail.Value);
                else
                    errors.Add(rail.Error!);

            return (oks, errors);
        }

        /// <summary>
        /// Maps the successful results in the sequence to a new type.
        /// Failed results are passed through unchanged.
        /// </summary>
        public IEnumerable<RailFor<TOut>> Map<TOut>(Func<T, TOut> map) where TOut : notnull =>
            results.Select(rail => rail.Map(map));

        /// <summary>
        /// Binds the successful results in the sequence to a new rail.
        /// Failed results are passed through unchanged.
        /// </summary>
        public IEnumerable<RailFor<TOut>> Bind<TOut>(Func<T, RailFor<TOut>> bind) where TOut : notnull =>
            results.Select(rail => rail.Bind(bind));
    }
}