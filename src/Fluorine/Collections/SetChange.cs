namespace Fluorine;

public enum ChangeKind
{
    Added,
    Removed,
    Updated
}

public readonly struct SetChange<T>(ChangeKind kind, T item, T previous)
{
    public SetChange(ChangeKind kind, T item) : this(kind, item, default)
    {
    }

    public ChangeKind Kind { get; } = kind;

    /// <summary>The added instance, the removed instance, or the instance now stored.</summary>
    public T Item { get; } = item;

    /// <summary>The instance that was stored, when <see cref="Kind"/> is <see cref="ChangeKind.Updated"/>.</summary>
    public T Previous { get; } = previous;
}