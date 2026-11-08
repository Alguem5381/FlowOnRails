using System.Collections.Frozen;

namespace FlowOnRails.AspNetCore.Visibility;

internal sealed class RuleIndex(IEnumerable<Rule> rules)
{
    private readonly FrozenDictionary<Type, RootRules> _roots = rules
        .GroupBy(rule => rule.Selection.Root)
        .ToFrozenDictionary(group => group.Key, group => new RootRules(group));

    internal Rule? FindRule(Type errorType)
    {
        for (
            var ancestor = errorType;
            ancestor is not null && typeof(Error).IsAssignableFrom(ancestor);
            ancestor = ancestor.BaseType
        )
        {
            if (!_roots.TryGetValue(ancestor, out var rules))
                continue;

            var match = rules.Select(errorType == ancestor);

            if (match is not null)
                return match;
        }

        return null;
    }

    private sealed class RootRules
    {
        private readonly Rule? _exact;
        private readonly Rule? _descendants;
        private readonly Rule? _tree;

        internal RootRules(IEnumerable<Rule> rules)
        {
            foreach (var rule in rules)
                switch (rule.Selection.Range)
                {
                    case ErrorRange.Exact:
                        _exact = rule;
                        break;
                    case ErrorRange.Descendants:
                        _descendants = rule;
                        break;
                    case ErrorRange.Tree:
                        _tree = rule;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(rules), "Unknown error selection range.");
                }
        }

        internal Rule? Select(bool isRoot) =>
            isRoot
                ? _exact ?? _tree
                : _descendants ?? _tree;
    }
}
