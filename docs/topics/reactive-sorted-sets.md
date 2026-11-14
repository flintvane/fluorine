[← Reactive dictionaries](reactive-dictionaries.md) · [Topics](../index.md) · [Reactive sorted dictionaries →](reactive-sorted-dictionaries.md)

# 14. Reactive sorted sets

`ReactiveSortedSet<T>` is a mutable set that publishes immutable pictures of its members, kept in comparer order. Use the set to add, remove and test membership. Use a `Version` to enumerate what is there now, keep that picture across reaction runs, and compare it with a later picture.

It follows the same rules as a [reactive set](reactive-sets.md). The difference is the order. Membership uses `IComparer<T>` instead of an equality comparer, and enumeration and `Diff` both run in that order. A picture shares unchanged structure with the picture it was built from.

A common way to expose both is a field for the set and a property for the current picture:

```csharp
public ReactiveSortedSet<string> Names = new ReactiveSortedSet<string>();
public ReactiveSortedSet<string>.Version NameValues => Names.Value;
```

`Names` is where you call `Add`, `Remove`, `Clear` and `Contains`. `NameValues` (or any other read of `Value`) is the picture you enumerate or pass to `Diff`.

`ReactiveSortedSet<T>` derives from `Register` and implements `IEnumerable<T>`. Creating one does not need a scheduler.

## Creating a set

```csharp
var empty = new ReactiveSortedSet<int>();
var seeds = new ReactiveSortedSet<int>(new[] { 3, 1, 2 });
var names = new ReactiveSortedSet<string>(StringComparer.OrdinalIgnoreCase);
var mixed = new ReactiveSortedSet<string>(
    new[] { "Ada", "Byron" }, StringComparer.OrdinalIgnoreCase);
```

- `new ReactiveSortedSet<T>()` starts empty and uses `Comparer<T>.Default`.
- The constructor that takes `IEnumerable<T>` stores those items. A null sequence throws `ArgumentNullException`.
- `Comparer` returns the comparer the set was created with. A null comparer means the default.
- Membership uses that comparer. Compare returning zero means the same element. A comparison result must stay stable while the element is in the set.
- A null element is a valid member when `T` can be null and the comparer accepts it. `Comparer<string>.Default` sorts null before every string.

The constructors store the items without triggering reactions. `Add` later returns `false` for an item the comparer already considers present; `Remove` returns `false` for an item that is not. An add that finds a match leaves the stored instance in place.

```csharp
names.Add("Ada");          // returns true
names.Add("ada");          // returns false: already present under OrdinalIgnoreCase
names.Contains("ADA");     // true
names.Remove("aDa");       // returns true
names.Clear();             // drops every member; a second Clear does nothing
```

`Add`, `Remove` and `Clear` can be called from any thread. An add or remove that does not change membership triggers nothing.

Enumerating the set yields members from low to high under the comparer. With `Comparer<int>.Default`, `1` comes before `2`. With `StringComparer.OrdinalIgnoreCase`, `"Ada"` comes before `"Byron"`.

## Two kinds of dependency

### One element: `Contains`

```csharp
reactor.Add(() =>
{
    badge.Visible = names.Contains("Ada");
});
```

Inside a reaction, `Contains` subscribes only to that element's presence. The reaction runs again when that element is added or removed (including when `Clear` removes it). Adding or removing a different element does not rerun it. The element is matched with the comparer, so a watch on `"Ada"` also sees `"ada"` when the comparer is `OrdinalIgnoreCase`.

Outside a reaction, or inside `Reaction.Pause`, `Contains` just reports the current picture and records no dependency ([Pausing](pausing.md)).

### The whole set: `Value`, `Count` and enumeration

```csharp
reactor.Add(() =>
{
    countLabel.Text = names.Value.Count.ToString();
});
```

`Value` records a read on the set itself, then returns the current `Version`. The reaction runs again after any membership change: an `Add` or `Remove` that returns `true`, or a `Clear` that had something to drop.

`Count` records the same kind of read. So does enumerating the set (`foreach (var name in names)`), because that enumerator is `Value`'s enumerator.

`Version` itself is a snapshot. `version.Contains`, `version.Count` and `foreach` over a version you already hold do not subscribe further. A held picture stays as it was when later writes publish a new one.

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
var names = new ReactiveSortedSet<string>();
var applied = new List<string>();
ReactiveSortedSet<string>.Version seen = null;

reactor.Add(() =>
{
    var current = names.Value;
    foreach (var change in current.Diff(seen))
        applied.Add(change.Kind + ":" + change.Item);
    seen = current;
});
```

`Diff` compares two pictures and returns the net `Added`, `Removed` and `Replaced` items, in comparer order. It does not mark either picture as seen; storing `seen = current` is what remembers the picture you applied. A null previous picture is treated as empty, so `Diff(null)` lists every current member as `Added`, still in order.

Writes that happen before the reaction next runs are coalesced into one run ([When reactions run](when-reactions-run.md)). `Diff(seen)` then reports the net change since the last stored picture: adding `"Ada"` and `"Byron"` and then removing `"Ada"` appears as `Added: Byron`.

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

---

*Membership reads follow register recording (§7.1, cs 12.8–12.11); versions and `Diff` follow cs 12.12–12.15 and cs 12.24–12.27; construction follows cs 12.23. (§ = spec/semantics.md 0.10, cs = spec/semantics-cs.md 0.6)*

[← Reactive dictionaries](reactive-dictionaries.md) · [Topics](../index.md) · [Reactive sorted dictionaries →](reactive-sorted-dictionaries.md)
