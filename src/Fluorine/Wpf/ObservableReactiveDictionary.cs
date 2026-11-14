using System;
using System.Collections.Generic;

namespace Fluorine;

// WPF view of a ReactiveDictionary, showing a projection of each entry (its
// key, its value, or the pair). The dictionary has no order, so the view keeps
// the position of each key and fills holes from the end. Positions are tracked
// by key, never by item: values may repeat, keys never do.
class ObservableReactiveDictionary<TKey, TValue, TItem>(
    ReactiveDictionary<TKey, TValue> dictionary,
    Func<TKey, TValue, TItem> project,
    bool followsValues) : ObservableReactiveCollection<TItem>
{
    private readonly Dictionary<TKey, int> _index = new(dictionary.Comparer);
    private readonly List<TKey> _keys = [];
    private ReactiveDictionary<TKey, TValue>.Version _applied;

    protected override void Refresh()
    {
        var current = dictionary.Value;
        Write(() =>
        {
            foreach (var change in current.Diff(_applied))
            {
                switch (change.Kind)
                {
                    case ReactiveDictionary<TKey, TValue>.ChangeKind.Added:
                        _index[change.Key] = Count;
                        _keys.Add(change.Key);
                        Add(project(change.Key, change.Value));
                        break;
                    case ReactiveDictionary<TKey, TValue>.ChangeKind.Removed:
                        RemoveBySwap(change.Key);
                        break;
                    case ReactiveDictionary<TKey, TValue>.ChangeKind.Updated:
                        if (followsValues)
                            this[_index[change.Key]] = project(change.Key, change.Value);
                        break;
                }
            }
        });
        _applied = current;
    }

    // The key of each slot is kept alongside, so the item moved into a hole can
    // have its position updated without looking it up by value.
    void RemoveBySwap(TKey key)
    {
        var at = _index[key];
        _index.Remove(key);
        var last = Count - 1;
        if (at != last)
        {
            var movedKey = _keys[last];
            _keys[at] = movedKey;
            _index[movedKey] = at;
            this[at] = this[last];
        }

        _keys.RemoveAt(last);
        RemoveAt(last);
    }
}
