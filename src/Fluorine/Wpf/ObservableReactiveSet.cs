using System.Collections.Generic;

namespace Fluorine;

// WPF view of a ReactiveSet. The set has no order, so the view keeps the
// position of each item in its own index and fills holes from the end.
class ObservableReactiveSet<T>(ReactiveSet<T> set) : ObservableReactiveCollection<T>
{
    private ReactiveSet<T>.Version _applied;

    protected override void Refresh()
    {
        var current = set.Value;
        Write(() =>
        {
            foreach (var change in current.Diff(_applied))
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
        });
        _applied = current;
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