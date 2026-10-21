[← Dispose](dispose.md) · [Side by side](index.md)

# Side by side: Async in a reaction

The scenario: a reaction reads `a`, awaits, then reads `b`. Then `b` is written, then `a`.

**Fluorine**

```csharp
reactor.Add(async () =>
{
    int av = a.Value;       // recorded
    await Task.Delay(20);   // the run ends here
    Console.WriteLine($"a={av} b={b.Value}");   // not recorded
});
```

The part up to the first suspending `await` is the run. A write to `a` runs the reaction again; a write to `b` does not ([Async code inside a reaction](../async.md)).

**The others**[^versions]

```js
autorun(async () => { const av = a.get(); await null; log(av, b.get()); });   // MobX
effect(async () => { const av = a.value; await null; log(av, b.value); });    // Vue
```

```csharp
// SignalsDotnet async effect
new Effect(async ct => { var av = a.Value; await Task.Delay(20); Log($"{av} {b.Value}"); },
           ConcurrentChangeStrategy.ScheduleNext);
```

**Observed**

- **MobX, Vue, Solid, Knockout, Tracker:** the write to `b` caused no run, the write to `a` did. Tracker reported `Tracker.active=false` after the `await`.
- **SignalsDotnet**, async `Effect(Func<CancellationToken, ValueTask>)`: the write to `b` also caused a run, so reads after the `await` were tracked. The runs were on a pool thread. With an `async` lambda passed as `Action`, the read after the `await` was not tracked.
- **Rx.NET:** the asynchronous step is written as an operator (`Select(... FromAsync ...).Concat()`); continuations ran on pool threads.

[← Dispose](dispose.md) · [Side by side](index.md)

[^versions]: Demos run on 2026-10-19 (Node 20.19.2, .NET SDK 8.0.426): MobX 7.0.6, `@vue/reactivity` 3.5.44 (reactivity package only), Solid 1.9.17 (client build), Knockout 3.5.3, Meteor Tracker from `meteor-standalone-tracker` 1.0.97 (an unofficial 2022 republish of an older Tracker; the demos add a small `Var` on `Tracker.Dependency` with `===` equality), SignalsDotnet 3.4.1, System.Reactive 7.0.0. Sources and outputs: `research/side-by-side/` in the Fluorine workspace. The Fluorine columns describe the specification (semantics.md 0.10, semantics-cs.md 0.6).
