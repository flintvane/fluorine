[← Comparison](../comparison.md) · [Side by side](index.md) · [Dynamic dependencies →](dynamic-dependencies.md)

# Side by side: First reaction

The scenario: a reaction prints `first + " " + last`. Then `last = "Byron"`, `first = "Augusta"`, and `first = "Augusta"` once more (an equal value).

**Fluorine**

```csharp
var first = new Register<string>("Ada");
var last  = new Register<string>("Lovelace");
var reactor = new Reactor(loop);
reactor.Add(() => Console.WriteLine(first.Value + " " + last.Value));

last.Value  = "Byron";
first.Value = "Augusta";
first.Value = "Augusta";   // equal: nothing happens
```

The first run is queued on `loop`. The two writes queue one run, which prints *Augusta Byron*. The writer continues immediately ([When reactions run](../when-reactions-run.md)).

**The others**[^versions]

```js
// MobX
const dispose = autorun(() => log(`${first.get()} ${last.get()}`));
// Vue
const runner = effect(() => log(`${first.value} ${last.value}`));
// Knockout
const c = ko.computed(() => log(`${first()} ${last()}`));
// Meteor Tracker
const comp = Tracker.autorun(() => log(`${first.get()} ${last.get()}`));
```

```csharp
// SignalsDotnet
using var eff = new Effect(() => Log($"{first.Value} {last.Value}"));
// Rx.NET: dependencies are declared by composition
first.DistinctUntilChanged()
     .CombineLatest(last.DistinctUntilChanged(), (f, l) => f + " " + l)
     .Subscribe(full => Log(full));
```

**Observed**

| | Creation run | Runs after the 3 writes | Where reruns ran |
|---|---|---|---|
| Fluorine | queued on its scheduler | 1 more (*Augusta Byron*) | the reactor's scheduler |
| MobX, Vue, Knockout | synchronous | 3 in total | inside the write |
| Solid | at the end of `createRoot` | 3 in total | inside the write |
| Tracker | synchronous | 2 in total; *Ada Byron* skipped | at the next flush |
| SignalsDotnet | synchronous | 3 in total | inside the write, on the writer's thread |
| Rx.NET | on `Subscribe` | 3 in total | on the writer's thread |

Every framework ignored the equal write; in the Rx demo that comes from `DistinctUntilChanged`.

[← Comparison](../comparison.md) · [Side by side](index.md) · [Dynamic dependencies →](dynamic-dependencies.md)

[^versions]: Demos run on 2026-10-19 (Node 20.19.2, .NET SDK 8.0.426): MobX 7.0.6, `@vue/reactivity` 3.5.44 (reactivity package only), Solid 1.9.17 (client build), Knockout 3.5.3, Meteor Tracker from `meteor-standalone-tracker` 1.0.97 (an unofficial 2022 republish of an older Tracker; the demos add a small `Var` on `Tracker.Dependency` with `===` equality), SignalsDotnet 3.4.1, System.Reactive 7.0.0. Sources and outputs: `research/side-by-side/` in the Fluorine workspace. The Fluorine columns describe the specification (semantics.md 0.10, semantics-cs.md 0.6).
