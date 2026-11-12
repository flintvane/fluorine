[← Reactive sets](reactive-sets.md) · [Topics](../index.md) · [Derived values, self-writes and cycles →](derived-values.md)

# 13. Reactive dictionaries

`ReactiveDictionary<TKey, TValue>` is a mutable dictionary that publishes immutable pictures of its entries. Use the dictionary to add, replace, remove and look up keys. Use a `Version` to enumerate what is there now, keep that picture across reaction runs, and compare it with a later picture.

A common way to expose both is a field for the dictionary and a property for the current picture:

```csharp
public ReactiveDictionary<string, int> Scores = new ReactiveDictionary<string, int>();
public ReactiveDictionary<string, int>.Version ScoreValues => Scores.Value;
```

`Scores` is where you call `Add`, `SetValue`, `Remove`, `Clear`, `ContainsKey` and `TryGetValue`. `ScoreValues` (or any other read of `Value`) is the picture you enumerate or pass to `Diff` and `SameAs`.

`ReactiveDictionary<TKey, TValue>` derives from `Register` and implements `IEnumerable<KeyValuePair<TKey, TValue>>`. Creating one does not need a scheduler.

## Creating a dictionary

```csharp
var empty = new ReactiveDictionary<string, int>();
var seeds = new ReactiveDictionary<string, int>(new[]
{
    new KeyValuePair<string, int>("a", 1),
    new KeyValuePair<string, int>("a", 2),
    new KeyValuePair<string, int>("b", 3)
}); // two entries; the first value for "a" is kept
var names = new ReactiveDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
var mixed = new ReactiveDictionary<string, int>(
    new[] { new KeyValuePair<string, int>("Ada", 1) }, StringComparer.OrdinalIgnoreCase);
```

- `new ReactiveDictionary<TKey, TValue>()` starts empty and uses `EqualityComparer<TKey>.Default`.
- The constructor that takes pairs keeps the first value for each key. A null sequence throws `ArgumentNullException`. A null key throws `ArgumentNullException`.
- `Comparer` returns the comparer the dictionary was created with.
- Keys use that comparer's `Equals` and `GetHashCode`. Hash codes must stay stable while a key is present.
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

## Three kinds of dependency

### One key's presence: `ContainsKey`

```csharp
reactor.Add(() =>
{
    badge.Visible = scores.ContainsKey("Ada");
});
```

Inside a reaction, `ContainsKey` subscribes only to that key's presence. The reaction runs again when that key is added or removed (including when `Clear` removes it). Replacing the value does not rerun it. Adding, removing or replacing a different key does not rerun it.

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

`Version` itself is a snapshot. `version.ContainsKey`, `version.TryGetValue`, the version indexer, `version.Count` and `foreach` over a version you already hold do not subscribe further.

Outside a reaction, or inside `Reaction.Pause`, a read just reports the current picture and records no dependency ([Pausing](pausing.md)).

## Pictures: `Version`

Each successful change publishes a new `Version`. Unchanged structure is shared with the picture it was built from. A version does not point back at the older one, so a picture nobody holds can be collected. The public surface of a picture is:

```csharp
public sealed class Version : IEnumerable<KeyValuePair<TKey, TValue>>
{
    public int Count { get; }
    public bool ContainsKey(TKey key);
    public bool TryGetValue(TKey key, out TValue value);
    public TValue this[TKey key] { get; }
    public bool SameAs(Version other);
    public Change[] Diff(Version previous);
}
```

Hold a previous picture in a variable that lives across runs:

```csharp
var scores = new ReactiveDictionary<string, int>();
var applied = new List<string>();
ReactiveDictionary<string, int>.Version seen = null;

reactor.Add(() =>
{
    var current = scores.Value;
    foreach (var change in current.Diff(seen))
        applied.Add(change.Kind + ":" + change.Key);
    seen = current;
});
```

`Diff` compares two pictures and returns the net `Added`, `Removed` and `Updated` entries. It does not mark either picture as seen; storing `seen = current` is what remembers the picture you applied. A null previous picture is treated as empty.

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

`Value` is the value in the newer picture for `Added` and `Updated`, and the value that left for `Removed`. `PreviousValue` is the value that was replaced for `Updated`, and `default(TValue)` for `Added` and `Removed`.

`SameAs` is true when two pictures are the same published structure, including when both are empty (`empty.SameAs(null)` is true). Two dictionaries built separately with the same entries produce pictures whose `Diff` is empty (same entries) but `SameAs` is false (they do not share structure).

When the two pictures come from the same dictionary, they share the branches that did not change. `Diff` returns immediately when the structure is identical, and otherwise walks only the branches that differ. Two unrelated pictures with the same entries still yield an empty `Diff`, without that shared-structure shortcut.

There is no separate "key appeared", "key disappeared" or "value changed" event. Those are `ContainsKey` or `TryGetValue` changing, or `ChangeKind.Added`, `ChangeKind.Removed` and `ChangeKind.Updated` from `Diff`.

---

*Key and value reads follow register recording (§7.1); queued runs coalesce as usual (§4.4). `ReactiveDictionary<TKey, TValue>` is described here from the C# library. (§ = spec/semantics.md 0.10, cs = spec/semantics-cs.md 0.6)*

[← Reactive sets](reactive-sets.md) · [Topics](../index.md) · [Derived values, self-writes and cycles →](derived-values.md)
