[← Reactive sorted sets](reactive-sorted-sets.md) · [Topics](../index.md) · [Derived values, self-writes and cycles →](derived-values.md)

# 15. Reactive sorted dictionaries

`ReactiveSortedDictionary<TKey, TValue>` is a mutable dictionary that publishes immutable pictures of its entries, kept in key order. Use the dictionary to add, replace, remove and look up keys. Use a `Version` to enumerate what is there now, keep that picture across reaction runs, and compare it with a later picture.

It follows the same rules as a [reactive dictionary](reactive-dictionaries.md). The difference is the order. Keys use `IComparer<TKey>` instead of an equality comparer, and enumeration and `Diff` both run in that order. A picture shares the blocks that did not change with the picture it was built from. A block holds up to five entries.

A common way to expose both is a field for the dictionary and a property for the current picture:

```csharp
public ReactiveSortedDictionary<string, int> Scores = new ReactiveSortedDictionary<string, int>();
public ReactiveSortedDictionary<string, int>.Version ScoreValues => Scores.Value;
```

`Scores` is where you call `Add`, `SetValue`, `Remove`, `Clear`, `ContainsKey` and `TryGetValue`. `ScoreValues` (or any other read of `Value`) is the picture you enumerate or pass to `Diff`.

`ReactiveSortedDictionary<TKey, TValue>` derives from `Register` and implements `IEnumerable<KeyValuePair<TKey, TValue>>`. Creating one does not need a scheduler.

## Creating a dictionary

```csharp
var empty = new ReactiveSortedDictionary<string, int>();
var seeds = new ReactiveSortedDictionary<string, int>(new[]
{
    new KeyValuePair<string, int>("b", 3),
    new KeyValuePair<string, int>("a", 1)
});
var names = new ReactiveSortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
var mixed = new ReactiveSortedDictionary<string, int>(
    new[] { new KeyValuePair<string, int>("Ada", 1) }, StringComparer.OrdinalIgnoreCase);
```

- `new ReactiveSortedDictionary<TKey, TValue>()` starts empty and uses `Comparer<TKey>.Default`.
- The constructor that takes pairs stores those entries. A null sequence throws `ArgumentNullException`. A null key throws `ArgumentNullException`. A repeated key keeps the first value.
- `Comparer` returns the comparer the dictionary was created with. A null comparer means the default.
- Keys use that comparer. Compare returning zero means the same key. The result of comparing two keys must stay stable while either is present.
- Values use `object.Equals`, the same comparison a register uses ([Equality](equality.md)). Implementing `IEquatable<TValue>` alone does not count. An equal value is left as it was: the stored instance stays, and nothing is published.
- A null key is rejected. A null value is a valid entry when `TValue` can be null.

The constructors store the entries without triggering reactions.

```csharp
names.Add("Ada", 1);                    // returns true
names.Add("ada", 2);                    // returns false: "Ada" is already present, and its value stays 1
names.ContainsKey("ADA");               // true
names.SetValue("ada", 1);               // returns false: the value compares equal
names.SetValue("ada", 4, out var old);  // returns true, old is 1
names.Remove("aDa");                    // returns true
names.Clear();                          // drops every entry; a second Clear does nothing
```

`Add` returns false when the key is already present and leaves the stored value alone. `Remove` returns false when the key is absent. `SetValue` returns true when the picture changes: the key was absent, or it was present and `object.Equals` says the value differs. The overload with `out oldValue` sets `oldValue` to the value that was stored, or to `default(TValue)` when the key was absent. `default(TValue)` is also a legal stored value, so that pair of results does not by itself say the key was new.

The indexer follows the usual dictionary shape. The setter adds or replaces. The getter throws `KeyNotFoundException` when the key is absent.

```csharp
names["Ada"] = 1;
var score = names["Ada"];
```

`Add`, `SetValue`, the indexer setter, `Remove` and `Clear` can be called from any thread. A write that leaves the picture unchanged triggers nothing. Replacing a value keeps the key instance that was already stored.

Enumerating the dictionary, its `Keys`, or its `Values` yields entries from low to high under the key comparer. `Keys` and `Values` on the dictionary record a read of the whole dictionary, because they read `Value` first. The same properties on a `Version` you already hold do not.

## Three kinds of dependency

### One key's presence: `ContainsKey`

```csharp
reactor.Add(() =>
{
    badge.Visible = scores.ContainsKey("Ada");
});
```

Inside a reaction, `ContainsKey` subscribes only to that key's presence. The reaction runs again when that key is added or removed (including when `Clear` removes it). Replacing the value does not rerun it. Adding, removing or replacing a different key does not rerun it. The key is matched with the comparer.

### One key's value: `TryGetValue` and the indexer

```csharp
reactor.Add(() =>
{
    if (scores.TryGetValue("Ada", out var score))
        label.Text = score.ToString();
});
```

Inside a reaction, `TryGetValue` subscribes to that key's value. The indexer getter does the same, then throws `KeyNotFoundException` when the key is absent. The reaction runs again when that key is added, removed, or its value changes. A different key does not rerun it.

A getter that throws still leaves the dependency in place. The exception is logged like any other exception from a reaction ([Errors and logging](errors-and-logging.md)), and the next change of that key's value runs the reaction again.

### The whole dictionary: `Value`, `Count` and enumeration

```csharp
reactor.Add(() =>
{
    countLabel.Text = scores.Value.Count.ToString();
});
```

`Value` records a read on the dictionary itself, then returns the current `Version`. The reaction runs again after any real change: an `Add` or `Remove` that returns true, a `SetValue` that returns true, an indexer set that changes the picture, or a `Clear` that had something to drop.

`Count` records the same kind of read. So does enumerating the dictionary (`foreach (var pair in scores)`), because that enumerator is `Value`'s enumerator.

`Version` itself is a snapshot. `version.ContainsKey`, `version.TryGetValue`, the version indexer, `version.Count` and `foreach` over a version you already hold do not subscribe further. A held picture stays as it was when later writes publish a new one.

Outside a reaction, or inside `Reaction.Pause`, a read just reports the current picture and records no dependency ([Pausing](pausing.md)).

## Pictures: `Version`

Each successful change publishes a new `Version`. Unchanged blocks are shared with the picture it was built from. A version does not point back at the older one, so a picture nobody holds can be collected. The public surface of a picture is:

```csharp
public sealed class Version : IEnumerable<KeyValuePair<TKey, TValue>>
{
    public int Count { get; }
    public bool ContainsKey(TKey key);
    public bool TryGetValue(TKey key, out TValue value);
    public TValue this[TKey key] { get; }
    public Change[] Diff(Version previous);
}
```

Hold a previous picture in a variable that lives across runs:

```csharp
var scores = new ReactiveSortedDictionary<string, int>();
var applied = new List<string>();
ReactiveSortedDictionary<string, int>.Version seen = null;

reactor.Add(() =>
{
    var current = scores.Value;
    foreach (var change in current.Diff(seen))
        applied.Add(change.Kind + ":" + change.Key);
    seen = current;
});
```

`Diff` compares two pictures and returns the net `Added`, `Removed` and `Updated` entries, in comparer order. It does not mark either picture as seen; storing `seen = current` is what remembers the picture you applied. A null previous picture is treated as empty.

Writes that happen before the reaction next runs are coalesced into one run ([When reactions run](when-reactions-run.md)). `Diff(seen)` then reports the net change since the last stored picture. Setting `"a"` to `1` and then to `2` before the next run appears as one `Added` with value `2` when `"a"` was absent from the picture you kept, and as one `Updated` to `2` when `"a"` was already in that picture.

If a run returns before it updates `seen`, the next run still diffs against that older picture and applies everything that has happened since.

```csharp
public enum ChangeKind { Added, Removed, Updated }

public readonly struct Change
{
    public ChangeKind Kind { get; }
    public TKey Key { get; }
    public TValue Value { get; }
    public TValue PreviousValue { get; }
}
```

`Value` is the value in the newer picture for `Added` and `Updated`, and the value that left for `Removed`. `PreviousValue` is the value that was replaced for `Updated`, and `default(TValue)` for `Added` and `Removed`. For `Updated` and `Removed`, `Key` is the instance from the picture you kept.

When the two pictures come from the same dictionary, they share the blocks that did not change. `Diff` returns immediately when the structure is identical, and otherwise walks only the blocks that differ. Two pictures built separately with the same comparer, with the same entries in a different shape, still yield an empty `Diff`. `Diff` compares with this picture's comparer, so both pictures need to be ordered by it.

There is no separate "key appeared", "key disappeared" or "value changed" event. Those are `ContainsKey` or `TryGetValue` changing, or `ChangeKind.Added`, `ChangeKind.Removed` and `ChangeKind.Updated` from `Diff`.

---

*Key and value reads follow register recording (§7.1, cs 12.8–12.11); versions and `Diff` follow cs 12.12–12.15 and cs 12.24–12.28. (§ = spec/semantics.md 0.10, cs = spec/semantics-cs.md 0.6)*

[← Reactive sorted sets](reactive-sorted-sets.md) · [Topics](../index.md) · [Derived values, self-writes and cycles →](derived-values.md)
