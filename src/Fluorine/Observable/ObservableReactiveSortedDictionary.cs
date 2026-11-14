using System;

namespace Fluorine;

// WPF view of a ReactiveSortedDictionary in key order, showing a projection of
// each entry (its key, its value, or the pair). Positions come from the
// dictionary's tree by key in O(log n), as in ObservableReactiveSortedSet:
// removals by old position, highest first; additions and updates by new
// position, lowest first.
class ObservableReactiveSortedDictionary<TKey, TValue, TItem>(
    ReactiveSortedDictionary<TKey, TValue> dictionary,
    Func<TKey, TValue, TItem> project,
    bool followsValues) : ObservableReactiveCollection<TItem>
{
    private ReactiveSortedDictionary<TKey, TValue>.Version _applied;

    protected override void Refresh()
    {
        var current = dictionary.Value;
        var changes = current.Diff(_applied);
        Write(() =>
        {
            for (var i = changes.Length - 1; i >= 0; i--)
                if (changes[i].Kind == ReactiveSortedDictionary<TKey, TValue>.ChangeKind.Removed)
                    RemoveAt(_applied.IndexOf(changes[i].Key));

            foreach (var change in changes)
            {
                switch (change.Kind)
                {
                    case ReactiveSortedDictionary<TKey, TValue>.ChangeKind.Added:
                        Insert(current.IndexOf(change.Key), project(change.Key, change.Value));
                        break;
                    case ReactiveSortedDictionary<TKey, TValue>.ChangeKind.Updated:
                        if (followsValues)
                            this[current.IndexOf(change.Key)] = project(change.Key, change.Value);
                        break;
                }
            }
        });
        _applied = current;
    }
}
