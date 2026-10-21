[← Coalesced runs](coalesced-runs.md) · [Side by side](index.md) · [Derived values and self-writes →](derived-and-self-writes.md)

# Side by side: Untracked reads

The scenario: a reaction reads `a` normally and `b` untracked. Then `b` is written, then `a`.

**Fluorine**

```csharp
reactor.Add(() =>
{
    int av = a.Value;
    int bv = Reaction.Pause(() => b.Value);
    Console.WriteLine($"a={av} b={bv}");
});
```

See [Pausing](../pausing.md) for the `using` and `Action` forms.

**The others**[^versions]

| Framework | Untracked read |
|---|---|
| MobX | `untracked(fn)` |
| Vue | `pauseTracking()` / `resetTracking()` |
| Solid | `untrack(fn)` |
| Knockout | `obs.peek()`, `ko.ignoreDependencies(fn)` |
| Tracker | `Tracker.nonreactive(fn)` |
| SignalsDotnet | `Signal.Untracked(fn)`, `signal.UntrackedValue` |
| Rx.NET | `WithLatestFrom(b)`, `BehaviorSubject.Value` |

**Observed**

In every framework, the write to `b` caused no run, and the following write to `a` caused one run that showed the current `b`. This is the same outcome as a Fluorine pause.

[← Coalesced runs](coalesced-runs.md) · [Side by side](index.md) · [Derived values and self-writes →](derived-and-self-writes.md)

[^versions]: Demos run on 2026-10-19 (Node 20.19.2, .NET SDK 8.0.426): MobX 7.0.6, `@vue/reactivity` 3.5.44 (reactivity package only), Solid 1.9.17 (client build), Knockout 3.5.3, Meteor Tracker from `meteor-standalone-tracker` 1.0.97 (an unofficial 2022 republish of an older Tracker; the demos add a small `Var` on `Tracker.Dependency` with `===` equality), SignalsDotnet 3.4.1, System.Reactive 7.0.0. Sources and outputs: `research/side-by-side/` in the Fluorine workspace. The Fluorine columns describe the specification (semantics.md 0.10, semantics-cs.md 0.6).
