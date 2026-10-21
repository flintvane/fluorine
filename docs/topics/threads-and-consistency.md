[← Schedulers](schedulers.md) · [Topics](../index.md) · [Pausing: reads that do not subscribe →](pausing.md)

# 9. Threads and consistency

Registers can be read and written from any thread (§1.3). Reactions stay on their own scheduler, and a thread has at most one scheduler, of any type (cs 3.13). This page explains what a reaction can see.

## Writes from any thread

```csharp
Task.Run(() =>
{
    var data = Download();
    downloaded.Value = data;     // queues the UI reaction and continues
});
```

A writer never waits for a reaction (§13.8). The value written is visible to the reaction when it runs (§7.6).

## Same loop: writes are seen together

Writes made on one loop are seen together by reactions on that loop, because no reaction runs in between (§11.11):

```csharp
// On the UI loop:
first.Value = "Augusta";
last.Value  = "Byron";
// A UI reaction reading both sees both new values.
```

## Across threads: each register on its own

A reaction on another thread may see some writes and not yet others (§11.12). Each register on its own is always consistent; a combination of registers can be from different moments. This is called a *glitch*, and Fluorine allows it (§5.1). Reaction code tolerates such combinations; the run guarantee ensures that a run with the final values follows (§4.1).

## Consistent state: one aggregate value

When several values must always fit together, put them in one immutable value in one register, and replace it as a whole (§5.3, §11.13):

```csharp
sealed class Address : IEquatable<Address>
{
    public readonly string Street, City, Zip;
    public Address(string street, string city, string zip) { Street = street; City = city; Zip = zip; }
    public bool Equals(Address o) => o != null && o.Street == Street && o.City == City && o.Zip == Zip;
    public override bool Equals(object o) => Equals(o as Address);
    public override int GetHashCode() => (Street + "|" + City + "|" + Zip).GetHashCode();
}

var address = new Register<Address>();

// Any thread:
address.Value = new Address("Main St 1", "Berlin", "10115");
```

Every reader sees a complete address.

## Values do not tear

A read always returns a whole value, also for large structs and for 64-bit values in a 32-bit process (cs 1.18, §13.1).

---

*Spec references: §1.3, §5, §7.6, §11.10-11.13, §13; cs 1.18. (§ = spec/semantics.md 0.9, cs = spec/semantics-cs.md 0.5)*

[← Schedulers](schedulers.md) · [Topics](../index.md) · [Pausing: reads that do not subscribe →](pausing.md)
