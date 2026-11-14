[← Reactive sorted dictionaries](reactive-sorted-dictionaries.md) · [Topics](../index.md) · [Reactive lists →](reactive-lists.md)

# 16. Reactive arrays

`ReactiveArray<T>` is a fixed-length sequence that publishes immutable pictures of its slots. Use the array to read and replace a slot. Use a `Version` to enumerate what is there now, keep that picture across reaction runs, and compare it with a later picture.

A common way to expose both is a field for the array and a property for the current picture:

```csharp
public ReactiveArray<int> Samples = new ReactiveArray<int>(4);
public ReactiveArray<int>.Version SampleValues => Samples.Value;
```

`Samples` is where you read and assign slots. `SampleValues` (or any other read of `Value`) is the picture you enumerate or pass to `Diff`.

`ReactiveArray<T>` derives from `Register` and implements `IEnumerable<T>`. Creating one does not need a scheduler. The length never changes. An index is a slot, so writing one slot does not move another. There is no `Contains`: membership of a value is the wrong question for a fixed row of slots.

## Creating an array

```csharp
var zeros = new ReactiveArray<int>(3);
var seeds = new ReactiveArray<string>(new[] { "Ada", "Byron" });
```

- `new ReactiveArray<T>(length)` starts with that many slots, each `default(T)`. A negative length throws `ArgumentOutOfRangeException`.
- The constructor that takes `IEnumerable<T>` stores those items in order and takes its length from the sequence. Duplicates stay. A null sequence throws `ArgumentNullException`.
- `Length` and `Count` are the same number.
- Values use `object.Equals`, the same comparison a register uses ([Equality](equality.md)). Implementing `IEquatable<T>` alone does not count. An equal value is left as it was: the stored instance stays, and nothing is published.
- A null element is a valid slot when `T` can be null.

The constructors store the slots without triggering reactions.

```csharp
seeds[0] = "Ada";     // equal to the stored value: nothing is published
seeds[1] = "Lovelace";
```

The indexer setter and `SetValue` can be called from any thread. `SetValue` returns true when the picture changes. The overload with `out oldValue` sets `oldValue` to the value that was stored. That read is not a subscription. An index outside `0 .. Length - 1` throws `ArgumentOutOfRangeException` and publishes nothing. If equality throws, `oldValue` is still the stored value, and the slot is left as it was.

## Two kinds of dependency

### One slot: the indexer

```csharp
reactor.Add(() =>
{
    label.Text = samples[1].ToString();
});
```

Inside a reaction, the getter subscribes only to that slot. The reaction runs again when that slot's value changes. Writing a different slot does not rerun it. An index outside the array throws `ArgumentOutOfRangeException` and subscribes to nothing: the length is fixed, so that index cannot become valid later.

### The whole array: `Value`, `Count`, `Length` and enumeration

```csharp
reactor.Add(() =>
{
    countLabel.Text = samples.Length.ToString();
});
```

`Value` records a read on the array itself, then returns the current `Version`. The reaction runs again after any slot change. `Count` and `Length` record the same kind of read, so a slot change reruns them even though the length number is the same. Enumerating the array (`foreach (var sample in samples)`) does too, because that enumerator is `Value`'s enumerator.

`Version` itself is a snapshot. The version indexer, `version.Count` and `foreach` over a version you already hold do not subscribe further.

Outside a reaction, or inside `Reaction.Pause`, a read just reports the current picture and records no dependency ([Pausing](pausing.md)).

## Pictures: `Version`

Each successful change publishes a new `Version`. Unchanged blocks are shared with the picture it was built from. A version does not point back at the older one, so a picture nobody holds can be collected.

```csharp
public sealed class Version : IEnumerable<T>
{
    public int Count { get; }
    public T this[int index] { get; }
    public Change[] Diff(Version previous);
}
```

```csharp
var samples = new ReactiveArray<int>(new[] { 1, 2, 3 });
var applied = new List<string>();
ReactiveArray<int>.Version seen = null;

reactor.Add(() =>
{
    var current = samples.Value;
    foreach (var change in current.Diff(seen))
        applied.Add(change.Kind + ":" + change.Index);
    seen = current;
});
```

`Diff` compares two pictures by index. Successive pictures of one array have the same length, so each change is `Updated`. An index past the older length is `Added`, and an index past the newer length is `Removed`, which is what you see when the two pictures came from arrays of different lengths. A null previous picture is treated as empty. `Diff` does not mark either picture as seen.

Writes that happen before the reaction next runs are coalesced into one run ([When reactions run](when-reactions-run.md)). `Diff(seen)` then reports the net change since the last stored picture.

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

When the two pictures come from the same array, they share the blocks that did not change. `Diff` skips a block both pictures still point at. Two unrelated pictures with the same slots still yield an empty `Diff`, without that shared-block shortcut.

---

*Slot reads follow register recording (§7.1, cs 12.28–12.30). Versions and `Diff` follow cs 12.30 and 12.15. (§ = spec/semantics.md 0.10, cs = spec/semantics-cs.md 0.6)*

[← Reactive sorted dictionaries](reactive-sorted-dictionaries.md) · [Topics](../index.md) · [Reactive lists →](reactive-lists.md)
