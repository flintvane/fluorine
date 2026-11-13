using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Fluorine;

// Keeps an ObservableCollection in step with the set. The reactor is created on
// the thread that creates the collection (for WPF: the UI thread, through the
// converter), so the collection is only touched on that thread's scheduler.
// Each run diffs the new version against the last one applied, so the
// collection receives only the net changes.
class ObservableReactiveCollection<T>(ReactiveSet<T> set) : ObservableCollection<T>
{
    private readonly Reactor _reactor = new();

    private int _listeners;

    public override event NotifyCollectionChangedEventHandler CollectionChanged
    {
        add
        {
            base.CollectionChanged += value;
            if (++_listeners == 1)
                SetupReaction();
        }
        remove
        {
            base.CollectionChanged -= value;
            if (--_listeners == 0)
                TeardownReaction();
        }
    }

    private bool _changeAllowed;

    // Every write of Collection<T> goes through these hooks. Checking here, before
    // the base call, rejects an outside write before anything has changed.
    void CheckChangeAllowed()
    {
        if (!_changeAllowed)
            throw new InvalidOperationException("Cannot change an observable collection that views a reactive set");
    }

    protected override void InsertItem(int index, T item)
    {
        CheckChangeAllowed();
        base.InsertItem(index, item);
    }

    protected override void RemoveItem(int index)
    {
        CheckChangeAllowed();
        base.RemoveItem(index);
    }

    protected override void SetItem(int index, T item)
    {
        CheckChangeAllowed();
        base.SetItem(index, item);
    }

    protected override void MoveItem(int oldIndex, int newIndex)
    {
        CheckChangeAllowed();
        base.MoveItem(oldIndex, newIndex);
    }

    protected override void ClearItems()
    {
        CheckChangeAllowed();
        base.ClearItems();
    }

    private void TeardownReaction() => _reactor.Clear();
    private ReactiveSet<T>.Version applied;

    private void SetupReaction()
    {
        _reactor.Add(() =>
        {
            var current = set.Value;
            // Handlers of CollectionChanged run inside this reaction; their reads must not subscribe it.
            using var pause = Reaction.Pause();
            _changeAllowed = true;
            try
            {
                foreach (var change in current.Diff(applied))
                {
                    switch (change.Kind)
                    {
                        case ReactiveSet<T>.ChangeKind.Added:
                            SetIndex(change.Item, Count);
                            Add(change.Item);
                            break;
                        case ReactiveSet<T>.ChangeKind.Removed:
                            RemoveBySwap(change.Item);
                            break;
                        case ReactiveSet<T>.ChangeKind.Replaced:
                            var at = TakeIndex(change.Previous);
                            SetIndex(change.Item, at);
                            this[at] = change.Item;
                            break;
                    }
                }
            }
            finally
            {
                _changeAllowed = false;
            }

            applied = current;
        });
    }

    // The set has no order, so a removal moves the last item into the hole
    // instead of shifting everything after it.
    void RemoveBySwap(T item)
    {
        var at = TakeIndex(item);
        var last = Count - 1;
        if (at != last)
        {
            var moved = this[last];
            SetIndex(moved, at);
            this[at] = moved;
        }

        RemoveAt(last);
    }

    // Position of each item, keyed with the set's comparer. A dictionary takes
    // no null key, and the set may hold null, so null keeps its own slot.
    private Dictionary<T, int> _index;
    private int _nullIndex = -1;

    void SetIndex(T item, int at)
    {
        if (item == null)
            _nullIndex = at;
        else
            (_index ??= new Dictionary<T, int>(set.Comparer))[item] = at;
    }

    int TakeIndex(T item)
    {
        if (item == null)
        {
            var at = _nullIndex;
            _nullIndex = -1;
            return at;
        }

        var found = _index[item];
        _index.Remove(item);
        return found;
    }
}