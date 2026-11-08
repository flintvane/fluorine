[← PersonEditor: one model in two windows](person-editor.md) · [Topics](../index.md)

# 23. Comparison with other frameworks

Fluorine shares its central idea with several well-known libraries: dependencies are found by recording reads while code runs. This page maps each of them onto Fluorine's three parts:

- **Registers**: state that records its readers, written from any thread.
- **Reactions with recorded dependencies**: rediscovered on every run.
- **Queued runs on schedulers**: a change queues one run on the reaction's own event loop, which runs it after its current work.

## Fluorine in short

| Aspect | Fluorine (C#) |
|---|---|
| State | `Register<T>`, `Register` for pure signals |
| Dependencies | Every read during a run; rediscovered each run (§7.3) |
| When dependents run | Queued on the reaction's scheduler, coalesced, after the current work (§1.4, §4.4) |
| Threads | Registers on any thread; each reaction pinned to one scheduler (§1.2, §1.3) |
| Equality | `object.Equals`; a write that changes nothing triggers nothing (cs 1.16) |
| Consistency | Each register consistent on its own; consistent state in one aggregate register (§5) |
| Untracked reads | `Reaction.Pause` (cs 2.24) |
| Lifetime | Explicit: a reactor owns its reactions and is disposed by its owner (§12) |
| Errors | Logged through `Reactive.UnhandledException`; the reaction stays connected (§8.1, cs 7.1) |
| Self-writes | A reaction may trigger itself to follow a state (§6.1) |

## Closest by model

| Framework | Registers | Recorded dependencies | When dependents run |
|---|---|---|---|
| [Meteor Tracker](https://docs.meteor.com/api/tracker) | reactive data sources; `Tracker.Dependency` | cleared on invalidation, recorded again on rerun | rerun at a later flush |
| [Compose snapshot state](https://developer.android.com/develop/ui/compose/state) | `mutableStateOf` | read observers, re-armed per observation | the runtime's own snapshot notification |
| [MobX](https://mobx.js.org/understanding-reactivity.html) | observables | reads during a tracked function | by the MobX runtime |
| [Knockout](https://knockoutjs.com/documentation/computed-dependency-tracking.html) | observables | re-evaluated, stale subscriptions disposed | synchronous, deferral optional |
| [Vue 3](https://vuejs.org/guide/extras/reactivity-in-depth.html) | `ref`, reactive objects | Proxy `get` tracks | jobs queued, de-duplicated, flushed in a microtask |

**Meteor Tracker.** A `Tracker.autorun` computation is the counterpart of a reaction, and `Tracker.Dependency` (`depend`, `changed`) of a signal register. A change invalidates the computation, and it reruns later at a flush; `Tracker.nonreactive` corresponds to a pause and `stop()` to disposal. Tracker runs on the single JavaScript thread and leaves change detection to the data source; Fluorine compares values with `Equals` and accepts writes from any thread. In Tracker, a computation that throws on its first run is stopped; a Fluorine reaction stays connected to what it read.

**Compose snapshot state.** Snapshot state objects record their reads and carry an equality policy (`SnapshotMutationPolicy`), much like registers. `snapshotFlow` documents that its block "may run more than once for equal sets of inputs or only once after many rapid snapshot changes", which matches Fluorine's run guarantee. Compose makes several writes atomic with `Snapshot.withMutableSnapshot`; in Fluorine, state that must change together lives in one aggregate register.

**MobX.** MobX "reacts to any observable property that is read during the execution of a tracked function". `autorun` and `reaction` return a disposer, comparable to `Reaction.Dispose()`. Reads after an `await` are not tracked in MobX, as in Fluorine (cs 9.1).

**Knockout.** A computed observable re-runs its evaluator when something it read changes, and disposes subscriptions it no longer uses, the same per-run rediscovery as Fluorine. `peek` and `ko.ignoreDependencies` correspond to a pause. Updates are synchronous by default, with `deferUpdates` and `rateLimit` for deferral; Fluorine always queues the run.

**Vue 3.** Reads through a reactive Proxy are tracked; component and watcher jobs are queued, de-duplicated and flushed in a microtask (`Promise.resolve().then(flushJobs)` in the scheduler). This is the closest mainstream match to "queued at most once, run later" (§4.4). Vue limits recursive updates with a `RECURSION_LIMIT` in development builds; in Fluorine a self-triggering reaction is a normal way to follow a state, and each step is a separate queued run.

**Leptos** ([reactive_graph](https://book.leptos.dev/appendix_reactive_graph.html)) tracks dependencies dynamically, runs effects asynchronously on the next tick of the host async runtime, and stops effects when their `Owner` is disposed: asynchronous runs and explicit ownership, as in Fluorine.

## Signals

| Framework | Registers | Recorded dependencies | When dependents run |
|---|---|---|---|
| [Solid](https://docs.solidjs.com/concepts/signals) | signals | tracking scopes (`createEffect`, `createMemo`) | by Solid's scheduler |
| [Preact Signals](https://preactjs.com/guide/v10/signals/) | `signal` | reads in `effect` / `computed` | synchronously at the write, or at the end of a `batch` |
| [Angular Signals](https://angular.dev/guide/signals) | `signal` | dynamic, per evaluation | effects asynchronously |
| [TC39 proposal](https://github.com/tc39/proposal-signals) | `Signal.State` | `Signal.Computed` | left to the host via `Signal.subtle.Watcher` |

**Solid.** Effects and memos are tracking scopes. Ownership is a tree (`createRoot`, `onCleanup`) in which effects end with their owner; a Fluorine reactor is the explicit, flat counterpart.

**Preact Signals.** Effects run synchronously at the write unless the write is inside `batch()`; `untracked` and `.peek()` read without subscribing. Assigning an equal value is a no-op. `effect` returns a dispose function. Preact throws "Cycle detected" when a batch keeps re-triggering.

**Angular Signals.** The documentation states that computed dependencies are dynamic, added and removed per evaluation. `effect` runs asynchronously, `untracked` reads without subscribing, and equality is `Object.is` unless an `equal` function is given.

**TC39 Signals proposal.** `Signal.State` and `Signal.Computed` are the state and the tracked computation, with an `equals` option. Computeds are glitch-free. Writes are synchronous, a `Watcher`'s `notify` runs inside `set` and must not read or write signals, and scheduling effects is left to frameworks. Fluorine defines the scheduler as part of the library.

## .NET

| Library | Registers | Recorded dependencies | When dependents run |
|---|---|---|---|
| [Rx.NET](https://github.com/dotnet/reactive) | observables | declared by composing operators | on the subscriber's scheduler, e.g. `ObserveOn` |
| [ReactiveUI](https://www.reactiveui.net/documentation/binding/observing/) / [DynamicData](https://github.com/reactivemarbles/DynamicData) | `INotifyPropertyChanged` properties, observable collections | property paths declared in `WhenAnyValue` | through Rx |
| [SignalsDotnet](https://github.com/fedeAlterio/SignalsDotnet) | `Signal<T>` | reads of `.Value` in `Computed` / `Effect` | on the writing thread, or on a given scheduler |

**Rx.NET** composes event streams with operators; the dependencies are the streams you combine. `ObserveOn(dispatcher)` is the usual way to deliver results on the WPF thread. In Fluorine the dependencies come from the reads, and a reaction runs on its own scheduler by construction.

**ReactiveUI and DynamicData** build on Rx. `WhenAnyValue` observes a property path given as an expression; DynamicData provides reactive collections. Both suit applications already built around Rx.

Fluorine's own path to WPF is `AutoNotify`: marked getters raise `INotifyPropertyChanged` when their value changes, so an ordinary `DataContext` binding works ([AutoNotify](autonotify.md)). WinForms bindings go through `FormsReactor` ([Binding a model to WinForms](winforms-binding.md)). [PersonEditor](person-editor.md) shows one model displayed both ways.

**SignalsDotnet** (3.4.1) is the .NET library closest to Fluorine. In our test runs (research/dotnet-demo):

- tracking is implicit through `Signal.Value`, and stale subscriptions are dropped per run;
- by default, effects run synchronously on the writing thread; with a `TimeProvider` scheduler they run on that scheduler, and three quick writes coalesced into one run;
- equal writes cause no rerun; `Signal.Untracked` and `UntrackedValue` read without subscribing;
- exceptions in effects are caught and the effect keeps working;
- its README states that signals are not thread-safe; in our concurrent-writer test, 2000 writes gave 802 effect runs.

Fluorine registers accept writes from any thread, and reactions always run on their own scheduler (§1.3, §11.1).

## Side by side

Eight small programs, written in Fluorine and in seven of these frameworks and run, show the differences in practice: scheduling, self-writes, errors, dispose and async. See [Side by side](side-by-side/index.md).

---

*Spec references: §1–§12; cs 1.16, 2.24, 7.1, 8.1. (§ = spec/semantics.md 0.10, cs = spec/semantics-cs.md 0.6). Sources: research/landscape.md (2026-10-17), research/dotnet-demo/RESULT.md (2026-10-18).*

[← PersonEditor: one model in two windows](person-editor.md) · [Topics](../index.md)
