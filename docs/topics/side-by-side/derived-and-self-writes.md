[← Untracked reads](untracked-reads.md) · [Side by side](index.md) · [Errors →](errors.md)

# Side by side: Derived values and self-writes

Two scenarios: a derived value `full = first + " " + last`, and a reaction that writes a register it reads (`if (x < 5) x = x + 1`).

**Fluorine**

```csharp
reactor.Add(() => full.Value = first.Value + " " + last.Value);

reactor.Add(() =>
{
    if (x.Value < 5)
        x.Value = x.Value + 1;   // queues the next step
});
```

A derived value is a reaction writing a register; it is up to date after its scheduler turn. A self-write triggers the reaction again through the queue, one step per run ([Derived values](../derived-values.md)).

**The others**[^versions]

```js
const full = computed(() => first.get() + ' ' + last.get());          // MobX
autorun(() => { const v = x.get(); if (v < 5) x.set(v + 1); });       // MobX self-write
createEffect(() => { const v = x(); if (v < 5) setX(v + 1); });       // Solid self-write
Tracker.autorun(() => full.set(first.get() + ' ' + last.get()));      // Tracker derived
```

```csharp
var full = Signal.Computed(() => first.Value + " " + last.Value);     // SignalsDotnet
```

**Observed: derived values**

| | Construct | Behaviour |
|---|---|---|
| MobX | `computed` | evaluated on each read outside a reaction; cached inside an autorun |
| Vue | `computed` | lazy, cached |
| Solid | `createMemo` | eager, cached |
| Knockout | `ko.pureComputed` | cached |
| SignalsDotnet | `Signal.Computed` | cached |
| Rx.NET | `CombineLatest(...).Replay(1).RefCount()` | shared |
| Tracker | an autorun writing a `Var` | up to date after the flush, as in Fluorine |

**Observed: self-writes**

| | Result |
|---|---|
| Fluorine | reruns, each step a queued run |
| MobX, Vue, Knockout | the reaction did not rerun on its own write (stayed at `x=1`) |
| Solid | reran up to `x=5`, nested on the stack; the unguarded variant ended in "Maximum call stack size exceeded" |
| Tracker | reran within the same flush up to `x=5`; unguarded: 2000 runs (the demo's cap) within 200 ms |
| SignalsDotnet | reran up to `x=5` at a constant stack depth; unguarded: ran to the cap |
| Rx.NET | recursive `OnNext`, stack growing with each step |

[← Untracked reads](untracked-reads.md) · [Side by side](index.md) · [Errors →](errors.md)

[^versions]: Demos run on 2026-10-19 (Node 20.19.2, .NET SDK 8.0.426): MobX 7.0.6, `@vue/reactivity` 3.5.44 (reactivity package only), Solid 1.9.17 (client build), Knockout 3.5.3, Meteor Tracker from `meteor-standalone-tracker` 1.0.97 (an unofficial 2022 republish of an older Tracker; the demos add a small `Var` on `Tracker.Dependency` with `===` equality), SignalsDotnet 3.4.1, System.Reactive 7.0.0. Sources and outputs: `research/side-by-side/` in the Fluorine workspace. The Fluorine columns describe the specification (semantics.md 0.9, semantics-cs.md 0.5).
