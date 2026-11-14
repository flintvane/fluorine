using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Fluorine;

// Base of the WPF views of reactive collections. The reactor is created on the
// thread that creates the view (for WPF: the UI thread, through the converter),
// so the view is only touched on that thread's scheduler. The reaction exists
// only while someone listens to CollectionChanged; each run calls Refresh,
// which diffs the source against the last version it applied.
abstract class ObservableReactiveCollection<T> : ObservableCollection<T>
{
    private readonly Reactor _reactor = new();

    private int _listeners;

    private bool _changeAllowed;

    public override event NotifyCollectionChangedEventHandler CollectionChanged
    {
        add
        {
            base.CollectionChanged += value;
            if (++_listeners == 1)
                _reactor.Add(Refresh);
        }
        remove
        {
            base.CollectionChanged -= value;
            if (--_listeners == 0)
                _reactor.Clear();
        }
    }

    // Reads the source (recorded, so the reaction follows it), then applies the
    // changes through Write.
    protected abstract void Refresh();

    // The only way to change the view. Handlers of CollectionChanged run inside
    // the reaction; their reads must not subscribe it.
    protected void Write(Action edit)
    {
        using var pause = Reaction.Pause();
        _changeAllowed = true;
        try
        {
            edit();
        }
        finally
        {
            _changeAllowed = false;
        }
    }

    // Every write of Collection<T> goes through these hooks. Checking here, before
    // the base call, rejects an outside write before anything has changed.
    void CheckChangeAllowed()
    {
        if (!_changeAllowed)
            throw new InvalidOperationException("Cannot change an observable collection that views a reactive collection");
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
}
