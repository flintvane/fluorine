[← First reaction](first-reaction.md) · [Side by side](index.md) · [Coalesced runs →](coalesced-runs.md)

# Side by side: Dynamic dependencies

The scenario: a reaction reads `useA ? a : b`. The demo writes the unread register, flips the branch, and then writes the register that is no longer read.

**Fluorine**

```csharp
reactor.Add(() => Console.WriteLine(useA.Value ? "a=" + a.Value : "b=" + b.Value));
```

After each run the reaction depends on exactly the registers that run read ([Reactions and dependencies](../reactions.md)).

**The others**[^versions]

```js
autorun(() => log(useA.get() ? 'a=' + a.get() : 'b=' + b.get()));    // MobX
effect(() => log(useA.value ? 'a=' + a.value : 'b=' + b.value));      // Vue
ko.computed(() => log(useA() ? 'a=' + a() : 'b=' + b()));             // Knockout
```

```csharp
// Rx.NET: the switch is written out
useA.Select(u => u ? a.Select(v => "a=" + v) : b.Select(v => "b=" + v)).Switch()
    .Subscribe(s => Log(s));
```

**Observed**

MobX, Vue, Solid, Knockout, Tracker and SignalsDotnet produced the same sequence: a write to a register that the last run did not read caused no run. Knockout reported two dependencies at both ends. Tracker reaches the same result by clearing all dependencies on invalidation and recording them again on the rerun. In Rx.NET, `Switch` gave the same counts, and after the flip `a.HasObservers` was `False` and `b.HasObservers` `True`.

[← First reaction](first-reaction.md) · [Side by side](index.md) · [Coalesced runs →](coalesced-runs.md)

[^versions]: Demos run on 2026-10-19 (Node 20.19.2, .NET SDK 8.0.426): MobX 7.0.6, `@vue/reactivity` 3.5.44 (reactivity package only), Solid 1.9.17 (client build), Knockout 3.5.3, Meteor Tracker from `meteor-standalone-tracker` 1.0.97 (an unofficial 2022 republish of an older Tracker; the demos add a small `Var` on `Tracker.Dependency` with `===` equality), SignalsDotnet 3.4.1, System.Reactive 7.0.0. Sources and outputs: `research/side-by-side/` in the Fluorine workspace. The Fluorine columns describe the specification (semantics.md 0.9, semantics-cs.md 0.5).
