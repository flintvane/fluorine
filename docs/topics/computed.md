[← Derived values, self-writes and cycles](derived-values.md) · [Topics](../index.md) · [Errors and logging →](errors-and-logging.md)

# 13. Computed values

A computed value is a register that holds the result of a function. It triggers its readers only when that result changes.

## Why

A reaction that only cares whether a list is empty would otherwise read the count, and run on every change of it. With a computed value it reads `isEmpty` instead, and runs only when the list becomes empty or stops being empty.

```csharp
var count   = new Register<int>();
var isEmpty = new Register<bool>(true);

reactor.Computed(isEmpty, () => count.Value == 0);
reactor.Add(() => emptyHint.Visible = isEmpty.Value);
```

Writes to `count` run the computation. The hint reaction runs only when `isEmpty` changes.

## Reactor.Computed

```csharp
Reaction Computed<T>(Register<T> register, Func<T> func, bool disposeOld = false)
```

`Computed` adds one reaction to the reactor and returns it, as `Add` does (cs 8.1). That reaction calls `func` and writes the result into `register`.

- **The register is yours.** It is an ordinary `Register<T>` that you create and own. The write is a normal write, so its readers are triggered only when the result differs from the stored value by `object.Equals` (cs 8.2, [Equality](equality.md)).
- **The first run is queued.** Until it has run, the register keeps the value it had (cs 8.3). Give the register a sensible initial value, as `true` above.
- **The function is fixed.** It is the lambda of the reaction. A null function writes `default(T)` (cs 8.4). For another computation, call `Computed` again.
- **Exceptions** from the function are handled like any exception of a reaction: logged, and the register is left unchanged (cs 8.5, [Errors and logging](errors-and-logging.md)).
- **Lifetime.** Disposing the returned reaction, or its reactor, stops the computation; the register keeps its last value. On a disposed reactor `Computed` creates nothing and returns `null` (cs 8.7).

## Disposable results

When the function creates a new object on each run, pass `disposeOld: true`. When the value is replaced, the old value is disposed if it implements `IDisposable`, after the new value is stored and the readers have been triggered (cs 8.6).

```csharp
var thumbnail = new Register<Bitmap>();
reactor.Computed(thumbnail, () => Render(image.Value, size.Value), disposeOld: true);
```

## Computed values and derived values

`Computed` is the short form of the pattern in [Derived values](derived-values.md): a reaction that writes a register. Like any derived value, it is up to date after its scheduler turn (§5.4).

---

*Spec references: §5.4; cs 8. (§ = spec/semantics.md 0.9, cs = spec/semantics-cs.md 0.5)*

[← Derived values, self-writes and cycles](derived-values.md) · [Topics](../index.md) · [Errors and logging →](errors-and-logging.md)
