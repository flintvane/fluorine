[← Derived values and self-writes](derived-and-self-writes.md) · [Side by side](index.md) · [Dispose →](dispose.md)

# Side by side: Errors

The scenario: a reaction reads `x` and throws when `x == 1`, before reading `y`. Then `y` is written, then `x = 2`.

**Fluorine**

```csharp
Reactive.UnhandledException += ex => Trace.TraceError(ex.ToString());

reactor.Add(() =>
{
    if (x.Value == 1) throw new InvalidOperationException("boom");
    Console.WriteLine(y.Value);
});
```

The exception is logged through `Reactive.UnhandledException`, the writer continues, and the reaction keeps the registers it read before the exception: here `x`. A write to `x` runs it again ([Errors and logging](../errors-and-logging.md)).

**The others**[^versions]

```js
onReactionError((e, r) => log('onReactionError hook: ' + e.message));  // MobX
autorun(() => { const xv = x.get(); if (xv === 1) throw new Error('boom'); log(y.get()); });
```

```csharp
// SignalsDotnet
using var eff = new Effect(() => { var xv = x.Value; if (xv == 1) throw new InvalidOperationException("boom"); Log(y.Value); });
```

**Observed**

| | Writer sees the exception | Reported | Write to `y` reruns | Write to `x` reruns |
|---|---|---|---|---|
| Fluorine | no | `Reactive.UnhandledException` | no | yes |
| MobX | no | console and `onReactionError` | no | yes |
| Vue | yes (the value was stored) | to the writer | no | yes |
| Solid | yes; with `catchError`, the handler | to the writer or the handler | no | yes |
| Knockout | yes | to the writer | no | yes |
| Tracker | no | console | no | yes |
| SignalsDotnet | no | not reported | yes (earlier subscriptions kept) | yes |
| Rx.NET, in `onNext` | yes | to the writer | — | yes |

Two further observations: in Tracker, a throw in the *first* run reached the caller of `autorun()`; in SignalsDotnet, an effect whose first run threw before any read never ran again. In Rx.NET, an exception inside an operator terminated the pipeline through `OnError`.

[← Derived values and self-writes](derived-and-self-writes.md) · [Side by side](index.md) · [Dispose →](dispose.md)

[^versions]: Demos run on 2026-10-19 (Node 20.19.2, .NET SDK 8.0.426): MobX 7.0.6, `@vue/reactivity` 3.5.44 (reactivity package only), Solid 1.9.17 (client build), Knockout 3.5.3, Meteor Tracker from `meteor-standalone-tracker` 1.0.97 (an unofficial 2022 republish of an older Tracker; the demos add a small `Var` on `Tracker.Dependency` with `===` equality), SignalsDotnet 3.4.1, System.Reactive 7.0.0. Sources and outputs: `research/side-by-side/` in the Fluorine workspace. The Fluorine columns describe the specification (semantics.md 0.10, semantics-cs.md 0.6).
