using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Fluorine;

// Keeps an ObservableCollection in step with a sorted set, in comparer order.
// The reactor is created on the thread that creates the collection (for WPF:
// the UI thread, through the converter), so the collection is only touched on
// that thread's scheduler.
//
// No index of our own is needed: the set's tree knows the size of every
// subtree, so a version gives the position of an element in O(log n).
// Removals use positions in the old picture, highest first, so earlier
// positions do not move. Additions and replacements then use positions in the
// new picture, lowest first: by the time an element is placed, everything
// before it in the new picture is already in the collection.
class ObservableReactiveSortedCollection<T>(ReactiveSortedSet<T> set) : ObservableCollection<T>
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
    private ReactiveSortedSet<T>.Version _applied;

    private void SetupReaction()
    {
        _reactor.Add(() =>
        {
            var current = set.Value;
            // Handlers of CollectionChanged run inside this reaction; their reads must not subscribe it.
            using var pause = Reaction.Pause();
            var changes = current.Diff(_applied);
            _changeAllowed = true;
            try
            {
                for (var i = changes.Length - 1; i >= 0; i--)
                    if (changes[i].Kind == ReactiveSortedSet<T>.ChangeKind.Removed)
                        RemoveAt(_applied.IndexOf(changes[i].Item));

                foreach (var change in changes)
                {
                    switch (change.Kind)
                    {
                        case ReactiveSortedSet<T>.ChangeKind.Added:
                            Insert(current.IndexOf(change.Item), change.Item);
                            break;
                        case ReactiveSortedSet<T>.ChangeKind.Replaced:
                            this[current.IndexOf(change.Item)] = change.Item;
                            break;
                    }
                }
            }
            finally
            {
                _changeAllowed = false;
            }

            _applied = current;
        });
    }
}
