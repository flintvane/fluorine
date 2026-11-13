[← Signal registers and shadow registers](signal-registers.md) · [Topics](../index.md) · [Reactive dictionaries →](reactive-dictionaries.md)

# 12. Reactive sets

`ReactiveSet<T>` is a mutable set that publishes immutable pictures of its members. Use the set to add, remove and test membership. Use a `Version` to enumerate what is there now, keep that picture across reaction runs, and compare it with a later picture.

A common way to expose both is a field for the set and a property for the current picture:

```csharp
public ReactiveSet<string> Tags = new ReactiveSet<string>();
public ReactiveSet<string>.Version TagValues => Tags.Value;
```

`Tags` is where you call `Add`, `Remove`, `Clear` and `Contains`. `TagValues` (or any other read of `Value`) is the picture you enumerate or pass to `Diff`.

`ReactiveSet<T>` derives from `Register` and implements `IEnumerable<T>`. Creating one does not need a scheduler.

## Creating a set

```csharp
var empty = new ReactiveSet<int>();
var seeds = new ReactiveSet<int>(new[] { 1, 2, 3 });
var names = new ReactiveSet<string>(StringComparer.OrdinalIgnoreCase);
var mixed = new ReactiveSet<string>(
    new[] { "Ada", "Byron" }, StringComparer.OrdinalIgnoreCase);
```

- `new ReactiveSet<T>()` starts empty and uses `EqualityComparer<T>.Default`.
- The constructor that takes `IEnumerable<T>` stores those items. A null sequence throws `ArgumentNullException`.
- `Comparer` returns the comparer the set was created with.
- Membership uses that comparer's `Equals` and `GetHashCode`. Hash codes must stay stable while an element is in the set.
- A null element is a valid member when `T` can be null.

The constructors store the items without triggering reactions. `Add` later returns `false` for an item that is already present; `Remove` returns `false` for an item that is not.

```csharp
names.Add("Ada");          // returns true
names.Add("ada");          // returns false: already present under OrdinalIgnoreCase
names.Contains("ADA");     // true
names.Remove("aDa");       // returns true
names.Clear();             // drops every member; a second Clear does nothing
```

`Add`, `Remove` and `Clear` can be called from any thread. An add or remove that does not change membership triggers nothing.

## Two kinds of dependency

### One element: `Contains`

```csharp
reactor.Add(() =>
{
    badge.Visible = tags.Contains("urgent");
});
```

Inside a reaction, `Contains` subscribes only to that element's presence. The reaction runs again when that element is added or removed (including when `Clear` removes it). Adding or removing a different element does not rerun it.

Outside a reaction, or inside `Reaction.Pause`, `Contains` just reports the current picture and records no dependency ([Pausing](pausing.md)).

### The whole set: `Value`, `Count` and enumeration

```csharp
reactor.Add(() =>
{
    countLabel.Text = tags.Value.Count.ToString();
});
```

`Value` records a read on the set itself, then returns the current `Version`. The reaction runs again after any membership change: an `Add` or `Remove` that returns `true`, or a `Clear` that had something to drop.

`Count` records the same kind of read. So does enumerating the set (`foreach (var tag in tags)`), because that enumerator is `Value`'s enumerator.

`Version` itself is a snapshot. `version.Contains`, `version.Count` and `foreach` over a version you already hold do not subscribe further.

## Pictures: `Version`

Each successful change publishes a new `Version`. Unchanged structure is shared with the picture it was built from. A version does not point back at the older one, so a picture nobody holds can be collected. The public surface of a picture is:

```csharp
public sealed class Version : IEnumerable<T>
{
    public int Count { get; }
    public bool Contains(T item);
    public Change[] Diff(Version previous);
}
```

Hold a previous picture in a variable that lives across runs:

```csharp
var numbers = new ReactiveSet<int>();
var applied = new List<string>();
ReactiveSet<int>.Version seen = null;

reactor.Add(() =>
{
    var current = numbers.Value;
    foreach (var change in current.Diff(seen))
        applied.Add(change.Kind + ":" + change.Item);
    seen = current;
});
```

`Diff` compares two pictures and returns the net `Added`, `Removed` and `Replaced` items. It does not mark either picture as seen; storing `seen = current` is what remembers the picture you applied. A null previous picture is treated as empty.

Writes that happen before the reaction next runs are coalesced into one run ([When reactions run](when-reactions-run.md)). `Diff(seen)` then reports the net change since the last stored picture: adding `1` and `2` and then removing `1` appears as `Added: 2`.

If a run returns before it updates `seen`, the next run still diffs against that older picture and applies everything that has happened since.

```csharp
public enum ChangeKind { Added, Removed, Replaced }

public readonly struct Change
{
    public ChangeKind Kind { get; }
    public T Item { get; }
    public T Previous { get; }
}
```

`Item` is the added instance, the removed instance, or the instance now stored for `Replaced`. `Previous` is the instance that was stored for `Replaced`, and `default(T)` for `Added` and `Removed`. `Replaced` is reported when both pictures hold an equal element as different instances of a reference type: `Item` is the instance in the newer picture, `Previous` the one in the picture you kept.

When the two pictures come from the same set, they share the branches that did not change. `Diff` returns immediately when the structure is identical, and otherwise walks only the branches that differ. Two unrelated pictures with the same members still yield an empty `Diff`, without that shared-structure shortcut.

To show a set in a WPF `ItemsControl` or `ListBox`, use `ObservableCollectionConverter` ([Showing a reactive set in WPF](observable-collections.md)).

---

*Membership reads follow register recording (§7.1, cs 12.8–12.11); versions and `Diff` follow cs 12.12–12.15; construction follows cs 12.23; queued runs coalesce as usual (§4.4). (§ = spec/semantics.md 0.10, cs = spec/semantics-cs.md 0.6)*

[← Signal registers and shadow registers](signal-registers.md) · [Topics](../index.md) · [Reactive dictionaries →](reactive-dictionaries.md)
