namespace FlowOnRails.AspNetCore.Visibility;

internal readonly record struct ErrorSelection(Type Root, ErrorRange Range)
{
    internal bool IncludesType(Type type) => Range switch
    {
        ErrorRange.Exact => type == Root,
        ErrorRange.Descendants => type != Root &&
                                  Root.IsAssignableFrom(type),
        ErrorRange.Tree => Root.IsAssignableFrom(type),
        _ => throw new ArgumentOutOfRangeException(nameof(Range), Range, "Unknown error selection range.")
    };

    internal bool ContainsSelection(ErrorSelection other)
    {
        if (other.Root == Root)
            return Range == ErrorRange.Tree ||
                   Range == other.Range;

        return IncludesType(other.Root);
    }

    internal bool ReopensExcludedRange(ErrorSelection blocked)
    {
        if (blocked.Root != Root)
            return blocked.IncludesType(Root);

        return blocked.Range == ErrorRange.Tree &&
               Range is ErrorRange.Exact or ErrorRange.Descendants;
    }
}
