[← When reactions run](when-reactions-run.md) · [Topics](../index.md) · [Reactors and lifetimes →](reactors.md)

# 6. Equality: what counts as a change

A write only counts as a change when the new value differs from the stored one. A write that changes nothing triggers nothing (§3.2).

## The comparison

The comparison is `object.Equals(value, stored)` (cs 1.16). That calls the `Equals(object)` override of the type; implementing `IEquatable<T>` alone is not enough.

```csharp
var name = new Register<string>("Ada");
name.Value = "Ada";      // equal: nothing stored, nothing triggered
name.Value = "Augusta";  // different: stored, reactions triggered
```

## Choosing value types

Equality decides whether reactions run, so it has to say what you mean (§3.3):

- **Immutable values with value equality** work best: strings, numbers, enums, and your own immutable classes that override `Equals` and `GetHashCode`.
- **Reference equality** is usually too weak. Changing a field inside an object stored in a register is invisible to the register; give the object its own registers instead (§14.2).
- **Floating point:** `double.Equals` treats `NaN` as equal to itself, unlike `==`. That is the comparison Fluorine uses.

```csharp
sealed class Range : IEquatable<Range>
{
    public readonly int From, To;
    public Range(int from, int to) { From = from; To = to; }
    public bool Equals(Range o) => o != null && o.From == From && o.To == To;
    public override bool Equals(object o) => Equals(o as Range);
    public override int GetHashCode() => From * 397 ^ To;
}

var selection = new Register<Range>(new Range(0, 0));
selection.Value = new Range(0, 0);   // equal by value: nothing happens
```

## When Equals throws

The comparison is user code and runs outside any lock. An exception from it reaches the code that wrote; nothing is stored and nothing is triggered (cs 1.17, §3.1c).

## Racing writers

Two threads writing the same register at once can cause at most one extra run, or a write that compares equal to a value that is being replaced. That is equivalent to the writes happening in the other order. No change is lost (§3.1b).

`ReactiveSet<T>` uses an `IEqualityComparer<T>` for membership ([Reactive sets](reactive-sets.md)). Hash codes must stay stable while an element is in the set.

`ReactiveDictionary<TKey, TValue>` uses an `IEqualityComparer<TKey>` for its keys and `object.Equals` for its values ([Reactive dictionaries](reactive-dictionaries.md)). A key's hash code must stay stable while the key is present. An equal value is not stored again.

`ReactiveSortedSet<T>` uses an `IComparer<T>` for membership ([Reactive sorted sets](reactive-sorted-sets.md)). A comparison result must stay stable while the element is in the set. `ReactiveSortedDictionary<TKey, TValue>` uses an `IComparer<TKey>` for its keys and `object.Equals` for its values ([Reactive sorted dictionaries](reactive-sorted-dictionaries.md)). A comparison result must stay stable while the key is present.

---

*Spec references: §3, §14.2; cs 1.16, 1.17. (§ = spec/semantics.md 0.10, cs = spec/semantics-cs.md 0.6)*

[← When reactions run](when-reactions-run.md) · [Topics](../index.md) · [Reactors and lifetimes →](reactors.md)
