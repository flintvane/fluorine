[← Errors](errors.md) · [Side by side](index.md) · [Async in a reaction →](async.md)

# Side by side: Dispose

The scenario: dispose, then write. Also: dispose while a run is queued, and dispose a group.

**Fluorine**

```csharp
var reactor = new Reactor(loop);
var r = reactor.Add(() => Console.WriteLine(x.Value));

r.Dispose();        // one reaction
reactor.Dispose();  // all reactions of the reactor
```

After disposal the lambda never executes again, also when a run was already queued. A run in progress completes. Disposing twice is harmless ([Reactors and lifetimes](../reactors.md)).

**The others**[^versions]

```js
const d = autorun(fn); d();                                 // MobX
const scope = effectScope(); scope.run(() => { /* effects */ }); scope.stop();  // Vue group
ko.computed(fn, null, { disposeWhen: () => !alive() });     // Knockout lifetime link
comp.stop();                                                // Tracker
```

```csharp
eff.Dispose();                                              // SignalsDotnet
var group = new CompositeDisposable(); group.Dispose();     // Rx.NET group
```

**Observed**

- **All:** no runs after dispose or stop, and a second dispose was harmless.
- **Queued run, then dispose:** MobX (with a `setTimeout` scheduler), Tracker, SignalsDotnet (on a busy loop) and Rx.NET (`ObserveOn`) did not run it. In Vue, a call already queued by a user scheduler still executed the stopped effect's function.
- **Groups:** Vue `effectScope().stop()`, Solid's root disposal (which also ran `onCleanup`) and Rx.NET `CompositeDisposable` stopped every member.
- **Ownership:** in Tracker, an `autorun` created inside another one was stopped each time the outer one reran. A MobX autorun disposed itself inside its run without further runs. Knockout's `disposeWhen` disposed the computed when `alive` became false.

[← Errors](errors.md) · [Side by side](index.md) · [Async in a reaction →](async.md)

[^versions]: Demos run on 2026-10-19 (Node 20.19.2, .NET SDK 8.0.426): MobX 7.0.6, `@vue/reactivity` 3.5.44 (reactivity package only), Solid 1.9.17 (client build), Knockout 3.5.3, Meteor Tracker from `meteor-standalone-tracker` 1.0.97 (an unofficial 2022 republish of an older Tracker; the demos add a small `Var` on `Tracker.Dependency` with `===` equality), SignalsDotnet 3.4.1, System.Reactive 7.0.0. Sources and outputs: `research/side-by-side/` in the Fluorine workspace. The Fluorine columns describe the specification (semantics.md 0.10, semantics-cs.md 0.6).
