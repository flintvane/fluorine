using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Fluorine;

// Keeps an ObservableCollection in step with the set. The reaction lives on the
// given reactor, so the collection is only touched on that reactor's scheduler
// (for WPF binding: a reactor on the DispatcherScheduler of the UI thread).
// Each run diffs the new version against the last one applied, so the
// collection receives only the net changes.
class ObservableReactiveCollection<T>(ReactiveSet<T> set) : ObservableCollection<T>
{
    private readonly Reactor _reactor = new();

    private int listeners;

    public override event NotifyCollectionChangedEventHandler CollectionChanged
    {
        add
        {
            base.CollectionChanged += value;
            if (++listeners == 1)
                SetupReaction();
        }
        remove
        {
            base.CollectionChanged -= value;
            if (--listeners == 0)
                TeardownReaction();
        }
    }

    private void TeardownReaction() => _reactor.Clear();

    private void SetupReaction()
    {
        ReactiveSet<T>.Version applied = null;
        _reactor.Add(() =>
        {
            var current = set.Value;
            foreach (var change in current.Diff(applied))
            {
                switch (change.Kind)
                {
                    case ReactiveSet<T>.ChangeKind.Added:
                        this.Add(change.Item);
                        break;
                    case ReactiveSet<T>.ChangeKind.Removed:
                        this.RemoveAt(IndexOf(this, change.Item, set.Comparer));
                        break;
                    case ReactiveSet<T>.ChangeKind.Replaced:
                        this[IndexOfInstance(this, change.Previous)] = change.Item;
                        break;
                }
            }

            applied = current;
        });
    }

    static int IndexOf<T>(IList<T> list, T item, IEqualityComparer<T> comparer)
    {
        for (var i = 0; i < list.Count; i++)
            if (comparer.Equals(list[i], item))
                return i;
        return -1;
    }

    static int IndexOfInstance<T>(IList<T> list, T item)
    {
        for (var i = 0; i < list.Count; i++)
            if (ReferenceEquals(list[i], item))
                return i;
        return -1;
    }
}