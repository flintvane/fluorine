namespace Fluorine;

public readonly struct DictChange<TKey,TValue>(ChangeKind kind, TKey key, TValue value, TValue previousValue)
{
    public ChangeKind Kind { get; } = kind;
    public TKey Key { get; } = key;
    public TValue Value { get; } = value;
    public TValue PreviousValue { get; } = previousValue;
}