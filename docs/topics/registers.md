[← Your first reaction](first-reaction.md) · [Topics](../index.md) · [Reactions and dependencies →](reactions.md)

# 3. Registers

A register holds one piece of program state and remembers which reactions read it.

## Creating a register

```csharp
var count = new Register<int>();          // holds default(int), which is 0
var title = new Register<string>("Home"); // holds "Home"
```

- The empty constructor stores `default(T)` (cs 1.6).
- The constructor with a value stores it without triggering anything (cs 1.7).

## Reading and writing

`Value` is a property (cs 1.8).

```csharp
title.Value = "Settings";   // a write
string t = title.Value;     // a read
```

- **Reading** inside a reaction subscribes that reaction to the register, before the value is returned (§7.1). Outside a reaction, a read just returns the value.
- **Writing** compares the new value with the stored one. When they differ, the new value is stored and then every subscribed reaction is triggered (§7.2). When they are equal, nothing happens ([Equality](equality.md)).

Both can be done from any thread (cs 1.13).

## SetValue: knowing whether a write changed something

```csharp
bool changed = title.SetValue("Settings");
```

`SetValue` writes exactly like the setter and returns `true` when the value changed (cs 1.14).

A second overload also returns the previous value:

```csharp
var image = new Register<Bitmap>();

Bitmap old;
if (image.SetValue(LoadBitmap(path), out old) && old != null)
    old.Dispose();
```

`oldValue` is set also when the method returns `false`. `SetValue` records no read, so `oldValue` is meant for releasing the old value, and not for decisions inside a reaction (cs 1.15).

## Names

```csharp
title.Name = "page title";
```

`Name` is a settable property used in logs and diagnostics (cs 1.9).

> **TODO** (cs 1.10): names "need not be retained" outside diagnostics mode, and diagnostics mode is not yet defined (cs 7.5).

## Deriving from a register

`Register<T>` can be inherited. `Value`, `RecordRead()` and `TriggerReactions()` keep their behaviour. The backing value is the protected property `StoredValue`, available to inheritors that specialize further (cs 1.11, 1.12). It is read and written without tearing; it records no read and triggers nothing.

## Registers without a value

`Register` (without a type argument, created with `new Register()`) is the base class: a register that only remembers its readers and can be triggered. It is covered in [Signal registers and shadow registers](signal-registers.md).

---

*Spec references: §3.1a, §7, §12.8; cs 1.1-1.15. (§ = spec/semantics.md 0.10, cs = spec/semantics-cs.md 0.6)*

[← Your first reaction](first-reaction.md) · [Topics](../index.md) · [Reactions and dependencies →](reactions.md)
