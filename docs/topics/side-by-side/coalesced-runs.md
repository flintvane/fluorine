[← Dynamic dependencies](dynamic-dependencies.md) · [Side by side](index.md) · [Untracked reads →](untracked-reads.md)

# Side by side: Coalesced runs

The scenario: three writes in a row to `x`. How many runs follow?

**Fluorine**

```csharp
reactor.Add(() => Console.WriteLine("x=" + x.Value));
x.Value = 1; x.Value = 2; x.Value = 3;   // one run, sees x=3
```

A reaction is queued at most once, and the run sees the latest value. Writes made on the reaction's own loop are seen together ([When reactions run](../when-reactions-run.md)).

**The others**[^versions]

```js
runInAction(() => { x.set(4); x.set(5); x.set(6); });                // MobX batch
autorun(fn, { scheduler: run => setTimeout(run, 0) });                // MobX deferred
batch(() => { setX(4); setX(5); setX(6); });                          // Solid
ko.computed(fn).extend({ deferred: true });                           // Knockout
```

```csharp
x.Skip(1).ObserveOn(eventLoop).Subscribe(v => Log(v));               // Rx.NET
```

**Observed**

| | 3 plain writes | Batched or deferred |
|---|---|---|
| Fluorine | 1 run, on its scheduler | the default |
| MobX | 3 runs | `runInAction`: 1 run; `setTimeout` scheduler: 1 run after a tick |
| Vue (`@vue/reactivity`) | 3 runs | a user scheduler with `queueMicrotask`: 1 run |
| Solid | 3 runs | `batch()`: 1 run |
| Knockout | 3 runs | `deferred: true`: 1 run; `rateLimit: 20`: 1 run |
| Tracker | 1 run, at the flush | `Tracker.flush()` runs it at once |
| SignalsDotnet | 3 runs | `Signal.Batch` or `Effect.AtomicOperation`: 1 run; with a loop `TimeProvider`: 1 run on the loop thread |
| Rx.NET | 3 deliveries | `ObserveOn`: every value delivered (4 of 4); `Throttle(30 ms)`: 1 delivery |

[← Dynamic dependencies](dynamic-dependencies.md) · [Side by side](index.md) · [Untracked reads →](untracked-reads.md)

[^versions]: Demos run on 2026-10-19 (Node 20.19.2, .NET SDK 8.0.426): MobX 7.0.6, `@vue/reactivity` 3.5.44 (reactivity package only), Solid 1.9.17 (client build), Knockout 3.5.3, Meteor Tracker from `meteor-standalone-tracker` 1.0.97 (an unofficial 2022 republish of an older Tracker; the demos add a small `Var` on `Tracker.Dependency` with `===` equality), SignalsDotnet 3.4.1, System.Reactive 7.0.0. Sources and outputs: `research/side-by-side/` in the Fluorine workspace. The Fluorine columns describe the specification (semantics.md 0.10, semantics-cs.md 0.6).
