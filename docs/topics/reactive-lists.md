[← Reactive arrays](reactive-arrays.md) · [Topics](../index.md) · [Derived values, self-writes and cycles →](derived-values.md)

# 17. Reactive lists

`ReactiveList<T>` is a variable-length sequence that publishes immutable pictures of its elements. Use the list to append, insert, delete and replace. Use a `Version` to enumerate what is there now, keep that picture across reaction runs, and compare it with a later picture.

A common way to expose both is a field for the list and a property for the current picture:

```csharp
public ReactiveList<string> Names = new ReactiveList<string>();
public ReactiveList<string>.Version NameValues => Names.Value;
```

`Names` is where you call `Add`, `Insert`, `Remove`, `RemoveAt`, `Clear`, `Contains` and `IndexOf`. `NameValues` (or any other read of `Value`) is the picture you enumerate or pass to `Diff`.

`ReactiveList<T>` derives from `Register` and implements `IEnumerable<T>`. Creating one does not need a scheduler. An index is a position. `Insert` and `RemoveAt` shift the positions after the edit.

## Creating a list

```csharp
var empty = new ReactiveList<int>();
var seeds = new ReactiveList<int>(new[] { 1, 1, 2 });
```

- `new ReactiveList<T>()` starts empty.
- The constructor that takes `IEnumerable<T>` stores those items in order. Duplicates stay. A null sequence throws `ArgumentNullException`.
- Values use `object.Equals`, the same comparison a register uses ([Equality](equality.md)). Implementing `IEquatable<T>` alone does not count. An equal write keeps the stored instance and publishes nothing.
- `GetHashCode` must agree with `Equals` and stay stable while the value is present. `Contains` uses that pair to tell one occurrence from another.
- A null element is valid when `T` can be null.

The constructors store the items without triggering reactions.

```csharp
names.Add("Ada");
names.Insert(0, "Byron");     // the index may be Count, which appends
names.RemoveAt(1);            // deletes the element at that index
names.Remove("Byron");        // deletes the first equal element; false when absent
names.Clear();                // a second Clear does nothing
```

`Add`, `Insert`, `Remove`, `RemoveAt`, the indexer setter and `Clear` can be called from any thread. An index below zero throws `ArgumentOutOfRangeException` and records nothing. A write past the end throws and publishes nothing. The getter past the end is different: it subscribes, then throws, so a later growth that fills that position runs the reaction again.

`SetValue` returns true when the picture changes. The overload with `out oldValue` sets `oldValue` to the stored value without recording a read. If equality throws, `oldValue` is still that stored value and the list is left as it was.

## Four kinds of dependency

### One position: the indexer

```csharp
reactor.Add(() =>
{
    label.Text = names[0];
});
```

Inside a reaction, the getter subscribes to the value at that position. The reaction runs again when the value at that position changes. That includes an insert or delete at an earlier position, when the shift leaves a different value behind, and it includes the position going from absent to present or present to absent.

A shift that leaves `object.Equals` the same value in that position does not rerun it. Inserting `"Ada"` at `0` in `["Ada", "Ada"]` leaves both existing positions equal to `"Ada"`, so a reaction that read `names[0]` or `names[1]` does not rerun. A reaction that read `names[2]` does, because that position was empty and now holds `"Ada"`.

Writing a later position, or inserting after the watched index, does not rerun it.

### Presence: `Contains`

```csharp
reactor.Add(() =>
{
    badge.Visible = names.Contains("Ada");
});
```

Inside a reaction, `Contains` subscribes to whether that value occurs at least once. The reaction runs again when the number of equal elements goes from zero to one, or from one to zero, including when `Clear` removes the last one. Appending a second `"Ada"` does not rerun it. Removing one of two does not rerun it either. Replacing a slot reruns it only when that replacement is what carries the count across the boundary.

### The first index: `IndexOf`

`IndexOf` records a read of the whole list. Any picture change reruns it, because an edit anywhere earlier can move the answer. It returns the first matching index, or `-1`. On a `Version` you already hold, `IndexOf` records nothing.

### The whole list: `Value`, `Count` and enumeration

```csharp
reactor.Add(() =>
{
    countLabel.Text = names.Count.ToString();
});
```

`Value` records a read on the list itself, then returns the current `Version`. The reaction runs again after any real change: an append, an insert, a delete, a `SetValue` that returns true, or a `Clear` that had something to drop. `Count` records the same kind of read, so replacing a slot reruns it even though the count number is the same. Enumerating the list does too, because that enumerator is `Value`'s enumerator.

`Version` itself is a snapshot. The version indexer, `version.Contains`, `version.IndexOf`, `version.Count` and `foreach` over a version you already hold do not subscribe further.

Outside a reaction, or inside `Reaction.Pause`, a read just reports the current picture and records no dependency ([Pausing](pausing.md)).

## Pictures: `Version`

Each successful change publishes a new `Version`. Unchanged blocks are shared with the picture it was built from. A version does not point back at the older one, so a picture nobody holds can be collected.

```csharp
public sealed class Version : IEnumerable<T>
{
    public int Count { get; }
    public T this[int index] { get; }
    public bool Contains(T item);
    public int IndexOf(T item);
    public Change[] Diff(Version previous);
}
```

`Diff` compares two pictures by index, in order. It is the net picture, not an edit script. Inserting `"X"` at `0` in `["A", "B", "C"]` is `Updated` at `0`, `1` and `2`, then `Added` at `3`. Deleting at the front is the same comparison in the other direction: the tail index that disappeared is `Removed`, and the indexes that slid down are `Updated`. Appending is one `Added` at the new index.

A null previous picture is treated as empty. `Diff` does not mark either picture as seen. Writes that happen before the reaction next runs are coalesced into one run ([When reactions run](when-reactions-run.md)), and `Diff(seen)` reports the net picture since the picture you kept.

```csharp
public enum ChangeKind { Added, Removed, Updated }

public readonly struct Change
{
    public ChangeKind Kind { get; }
    public int Index { get; }
    public T Value { get; }
    public T Previous { get; }
}
```

`Value` is the value in the newer picture for `Added` and `Updated`, and the value that left for `Removed`. `Previous` is the value that was replaced for `Updated`, and `default(T)` for `Added` and `Removed`.

When the two pictures come from the same list, they share the blocks that did not change. `Diff` skips a block both pictures still point at. Two unrelated pictures with the same elements in the same order still yield an empty `Diff`, without that shared-block shortcut.

There is no separate "inserted" or "deleted" event. Those are a position changing, `Contains` changing, or `ChangeKind.Added`, `ChangeKind.Removed` and `ChangeKind.Updated` from `Diff`.

---

*Position and presence reads follow register recording (§7.1, cs 12.31–12.33). Versions and `Diff` follow cs 12.30 and 12.15. (§ = spec/semantics.md 0.10, cs = spec/semantics-cs.md 0.6)*

[← Reactive arrays](reactive-arrays.md) · [Topics](../index.md) · [Derived values, self-writes and cycles →](derived-values.md)
