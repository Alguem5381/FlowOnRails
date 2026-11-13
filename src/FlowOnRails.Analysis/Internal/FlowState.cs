using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;

namespace FlowOnRails.Analysis.Internal;

internal readonly struct Slot : IEquatable<Slot>
{
    internal ISymbol? Symbol { get; }
    private CaptureId Capture { get; }
    private bool IsCapture { get; }

    internal Slot(ISymbol symbol) => (Symbol, Capture, IsCapture) = (symbol, default, false);

    internal Slot(CaptureId capture) => (Symbol, Capture, IsCapture) = (null, capture, true);

    public bool Equals(Slot other) =>
        IsCapture == other.IsCapture
        && (IsCapture
            ? Capture.Equals(other.Capture)
            : SymbolEqualityComparer.Default.Equals(Symbol, other.Symbol));

    public override bool Equals(object? candidate) => candidate is Slot other && Equals(other);

    public override int GetHashCode()
    {
        if (IsCapture)
            return Capture.GetHashCode();

        return Symbol is null ? 0 : SymbolEqualityComparer.Default.GetHashCode(Symbol);
    }
}

internal sealed class FlowState
{
    private readonly Dictionary<Slot, FlowValue> _values;
    private readonly Dictionary<Slot, Slot> _captures;
    private readonly Dictionary<Slot, ImmutableArray<Slot>> _aliases;
    internal bool Reachable { get; set; } = true;

    internal FlowState()
    {
        _values = [];
        _captures = [];
        _aliases = [];
    }

    private FlowState(FlowState source)
    {
        _values = new(source._values);
        _captures = new(source._captures);
        _aliases = new(source._aliases);
        Reachable = source.Reachable;
    }

    internal FlowState Copy() => new(this);

    internal IEnumerable<Slot> SymbolSlots => _values.Keys.Where(slot => slot.Symbol is not null);

    // CFG capture IDs belong to one graph. Only symbol storage can cross a call
    // boundary; carrying capture IDs into another graph would alias unrelated values.
    internal FlowState CopySymbols()
    {
        var result = new FlowState();

        foreach (var entry in _values)
            if (entry.Key.Symbol is not null)
                result._values[entry.Key] = entry.Value.WithoutFlowLinks();

        foreach (var entry in _aliases)
            if (entry.Key.Symbol is not null)
                result._aliases[entry.Key] = entry.Value;

        return result;
    }

    internal void BindAlias(Slot alias, Slot? source) =>
        _aliases[alias] = source is { } sourceSlot ? ReferenceStorage(sourceSlot) : [];

    internal void BindAlias(Slot alias, ImmutableArray<Slot> storage) => _aliases[alias] = storage;

    internal void Adopt(FlowState source)
    {
        _values.Clear();
        _captures.Clear();
        _aliases.Clear();

        foreach (var entry in source._values)
            _values.Add(entry.Key, entry.Value);

        foreach (var entry in source._captures)
            _captures.Add(entry.Key, entry.Value);

        foreach (var entry in source._aliases)
            _aliases.Add(entry.Key, entry.Value);

        Reachable = source.Reachable;
    }

    internal ImmutableArray<Slot> Storage(Slot slot) =>
        _aliases.TryGetValue(slot, out var storage) ? storage : [slot];

    internal ImmutableArray<Slot> ReferenceStorage(Slot slot)
    {
        if (slot.Symbol is not null || _aliases.ContainsKey(slot))
            return Storage(slot);

        // Value captures are snapshots, not cells. A capture can supply an address
        // only while every incoming path agrees on the original symbol storage.
        return _captures.TryGetValue(slot, out var source) ? ReferenceStorage(source) : [];
    }

    internal bool TryGet(Slot slot, out FlowValue value)
    {
        value = FlowValue.Bottom;
        var storage = Storage(slot);

        if (storage.Length == 0)
            return false;

        foreach (var source in storage)
        {
            if (!_values.TryGetValue(source, out var storedValue))
                return false;

            value = value.Join(storedValue);
        }

        return true;
    }

    internal void Refine(Slot slot, FlowValue value)
    {
        var storage = Storage(slot);

        // A conditional ref can denote different cells on different paths. A
        // predicate about one selected cell cannot refine all candidate cells.
        if (storage.Length != 1)
            return;

        slot = storage[0];
        _values[slot] = value;

        if (_captures.TryGetValue(slot, out var sourceSlot))
            Refine(sourceSlot, value);
    }

    internal void LinkCapture(Slot capture, Slot source)
    {
        if (!capture.Equals(source))
            _captures[capture] = source;
    }

    internal void Write(Slot slot, FlowValue value)
    {
        var storage = Storage(slot);

        if (storage.Length == 0)
        {
            _captures.Clear();

            foreach (var storedSlot in _values.Keys.ToArray())
                _values[storedSlot] = FlowValue.Other;

            return;
        }

        foreach (var source in storage)
            WriteStorage(source, storage.Length == 1 || !_values.TryGetValue(source, out var previous)
                ? value : previous.Join(value));
    }

    private void WriteStorage(Slot slot, FlowValue value)
    {
        // A write invalidates predicates about the previous value. Capture links must
        // also be removed so that refining a snapshot cannot change a newer assignment.
        foreach (var storedSlot in _values.Keys.ToArray())
            _values[storedSlot] = _values[storedSlot].WithoutLinks(slot);

        var invalidatedCaptures = _captures
            .Where(capture => capture.Key.Equals(slot) || capture.Value.Equals(slot))
            .Select(capture => capture.Key)
            .ToArray();

        foreach (var captureSlot in invalidatedCaptures)
            _captures.Remove(captureSlot);

        _values[slot] = value.WithoutLinks(slot);
    }

    internal void Forget(Func<ISymbol, FlowValue> createUnknownValue)
    {
        _captures.Clear();

        foreach (var storedSlot in _values.Keys.ToArray())
            _values[storedSlot] = storedSlot.Symbol is { } symbol ? createUnknownValue(symbol) : FlowValue.Other;
    }

    // At a control-flow join, values include either incoming path. A capture link is
    // safe to keep only when both paths agree on its source; otherwise refinements
    // could incorrectly discard an error from the other path.
    internal bool Merge(FlowState incoming)
    {
        var changed = false;

        foreach (var alias in _aliases.Keys.Union(incoming._aliases.Keys).ToArray())
        {
            var storage = Storage(alias);
            var incomingStorage = incoming.Storage(alias);
            var merged = storage.Length == 0 || incomingStorage.Length == 0
                ? ImmutableArray<Slot>.Empty : [.. storage.Union(incomingStorage)];

            if (storage.Length == merged.Length && storage.All(merged.Contains))
                continue;

            _aliases[alias] = merged;
            changed = true;
        }

        foreach (var captureSlot in _captures.Keys.ToArray())
        {
            if (incoming._captures.TryGetValue(captureSlot, out var sourceSlot)
                && _captures[captureSlot].Equals(sourceSlot))
                continue;

            _captures.Remove(captureSlot);
            changed = true;
        }

        foreach (var storedValue in incoming._values)
        {
            var mergedValue = _values.TryGetValue(storedValue.Key, out var currentValue)
                ? currentValue.Join(storedValue.Value)
                : storedValue.Value;

            if (currentValue is not null && currentValue.SameAs(mergedValue))
                continue;

            _values[storedValue.Key] = mergedValue;
            changed = true;
        }

        return changed;
    }
}
