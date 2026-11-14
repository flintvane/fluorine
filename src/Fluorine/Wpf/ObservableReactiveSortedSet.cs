namespace Fluorine;

// WPF view of a ReactiveSortedSet, in comparer order.
//
// No index of our own is needed: the set's tree knows the size of every
// subtree, so a version gives the position of an element in O(log n).
// Removals use positions in the old picture, highest first, so earlier
// positions do not move. Additions and replacements then use positions in the
// new picture, lowest first: by the time an element is placed, everything
// before it in the new picture is already in the view.
class ObservableReactiveSortedSet<T>(ReactiveSortedSet<T> set) : ObservableReactiveCollection<T>
{
    private ReactiveSortedSet<T>.Version _applied;

    protected override void Refresh()
    {
        var current = set.Value;
        var changes = current.Diff(_applied);
        Write(() =>
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
        });
        _applied = current;
    }
}
