using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Fluorine
{
    public static class ReactiveSetExtensions
    {
        // Keeps an ObservableCollection in step with the set. The reaction lives on the
        // given reactor, so the collection is only touched on that reactor's scheduler
        // (for WPF binding: a reactor on the DispatcherScheduler of the UI thread).
        // Each run diffs the new version against the last one applied, so the
        // collection receives only the net changes.
        public static ObservableCollection<T> ToObservableCollection<T>(this ReactiveSet<T> set, Reactor reactor)
        {
            var target = new ObservableCollection<T>();
            ReactiveSet<T>.Version applied = null;
            reactor.Add(() =>
            {
                var current = set.Value;
                foreach (var change in current.Diff(applied))
                {
                    switch (change.Kind)
                    {
                        case ReactiveSet<T>.ChangeKind.Added:
                            target.Add(change.Item);
                            break;
                        case ReactiveSet<T>.ChangeKind.Removed:
                            target.RemoveAt(IndexOf(target, change.Item, set.Comparer));
                            break;
                        case ReactiveSet<T>.ChangeKind.Replaced:
                            target[IndexOfInstance(target, change.Previous)] = change.Item;
                            break;
                    }
                }
                applied = current;
            });
            return target;
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
}
