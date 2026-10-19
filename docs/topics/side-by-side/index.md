[← Comparison with other frameworks](../comparison.md) · [Topics](../../index.md)

# Side by side

The same eight small programs, written in Fluorine and in MobX, Vue, Solid, Knockout, Meteor Tracker, SignalsDotnet and Rx.NET, and run. Each page shows the Fluorine C# version, short excerpts from the others, and what the runs showed.

1. [First reaction](first-reaction.md)
2. [Dynamic dependencies](dynamic-dependencies.md)
3. [Coalesced runs](coalesced-runs.md)
4. [Untracked reads](untracked-reads.md)
5. [Derived values and self-writes](derived-and-self-writes.md)
6. [Errors](errors.md)
7. [Dispose](dispose.md)
8. [Async in a reaction](async.md)

The Fluorine column on each page describes the specification; the other columns describe the real runs.[^versions]

[^versions]: Demos run on 2026-10-19 (Node 20.19.2, .NET SDK 8.0.426): MobX 7.0.6, `@vue/reactivity` 3.5.44 (reactivity package only), Solid 1.9.17 (client build), Knockout 3.5.3, Meteor Tracker from `meteor-standalone-tracker` 1.0.97 (an unofficial 2022 republish of an older Tracker; the demos add a small `Var` on `Tracker.Dependency` with `===` equality), SignalsDotnet 3.4.1, System.Reactive 7.0.0. Sources and outputs: `research/side-by-side/` in the Fluorine workspace. The Fluorine columns describe the specification (semantics.md 0.9, semantics-cs.md 0.5).
