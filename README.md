<p align="center"><img src="branding/fluorine-logo.png" alt="Fluorine logo" width="320"></p>

# Fluorine

A reactive programming library, provided in the same form on a variety of platforms.

Program state lives in registers. Code that must stay in step with that state is written as a reaction. A reaction does not declare its dependencies: whatever registers it reads while it runs become its dependencies, and when one changes, the reaction runs again.

This repository holds the C# implementation: the base library, with schedulers and bindings for both WPF and WinForms.

## Documentation: using Fluorine from C#

The documentation is in four sections. Read the Guide in order; each topic builds on the ones before it. WPF and WinForms follow the Guide, then the comparison to other frameworks.

## Guide

1. [What Fluorine is](docs/topics/introduction.md): Registers hold state, reactions follow it. The one idea behind the library, and when to use it.
2. [Your first reaction](docs/topics/first-reaction.md): A complete program: a scheduler, two registers, a reactor and a reaction that keeps a full name up to date.
3. [Registers](docs/topics/registers.md): `Register<T>`, its `Value` property, `SetValue`, initial values and names.
4. [Reactions and dependencies](docs/topics/reactions.md): How a reaction discovers what it reads, and how its dependencies change from run to run.
5. [When reactions run](docs/topics/when-reactions-run.md): Queued runs, coalescing, the run guarantee and manual triggers.
6. [Equality: what counts as a change](docs/topics/equality.md): `object.Equals`, writes that change nothing, and how to choose sound value types.
7. [Reactors and lifetimes](docs/topics/reactors.md): Reactors own reactions; disposing a reactor or a reaction ends it cleanly.
8. [Schedulers](docs/topics/schedulers.md): `ThreadScheduler`, `DispatcherScheduler` and `WinFormsScheduler`, `Scheduler.Current`, and queueing your own work.
9. [Threads and consistency](docs/topics/threads-and-consistency.md): Writing from any thread, what reactions on other threads see, and aggregate values for consistent state.
10. [Pausing: reads that do not subscribe](docs/topics/pausing.md): `Reaction.Pause` in its three shapes, and `Reaction.Current` / `Reaction.IsRecording`.
11. [Signal registers and shadow registers](docs/topics/signal-registers.md): A `Register` without a value, `TriggerReactions()`, and wrapping controls that hold their own data.
12. [Reactive sets](docs/topics/reactive-sets.md): `ReactiveSet<T>` for add, remove and membership; `Version` to enumerate a picture and `Diff` it against one you kept.
13. [Reactive dictionaries](docs/topics/reactive-dictionaries.md): `ReactiveDictionary<TKey, TValue>` for add, replace and remove; `Version` to enumerate a picture and `Diff` it against one you kept.
14. [Derived values, self-writes and cycles](docs/topics/derived-values.md): Reactions that write registers, reactions that follow a state, and reactions that create reactions.
15. [Computed values](docs/topics/computed.md): `Reactor.Computed` writes the result of a function into a register, which triggers only when the result changes.
16. [Errors and logging](docs/topics/errors-and-logging.md): Exceptions in reactions, `Reactive.UnhandledException`, `ReactiveException` and programming errors.
17. [Shutting down](docs/topics/shutdown.md): `Scheduler.Shutdown()` and `Reactive.Shutdown()`: drain, dispose, return.
18. [Async code inside a reaction](docs/topics/async.md): What part of an `async` lambda belongs to the run, and how to write one safely.
19. [Rules of thumb](docs/topics/rules-of-thumb.md): One reaction per thread, one entry and one exit, and a short checklist.
20. [The Reactive attribute and the source generator](docs/topics/reactive-attribute.md): `AddReactiveMethods` turns marked methods into reactions; the generator writes registers for partial properties.

## WPF

1. [AutoNotify: notifying WPF](docs/topics/autonotify.md): Derive a model from `AutoNotify`, mark properties, and raise `PropertyChanged` when their getters change.

## WinForms

1. [Binding a model to WinForms](docs/topics/winforms-binding.md): `FormsReactor` binds text, visibility and enabled state, with `TextConverter` for typed values.
2. [PersonEditor: one model in two windows](docs/topics/person-editor.md): A demo that shows the same `Person` in WinForms and WPF, and how to build and run it.

## Comparison to other frameworks

1. [Comparison with other frameworks](docs/topics/comparison.md): How Meteor Tracker, Compose, MobX, Knockout, Vue, the signals libraries and the .NET options map onto registers, recorded dependencies and scheduled runs.

Then compare topic by topic:

- [First reaction](docs/topics/side-by-side/first-reaction.md)
- [Dynamic dependencies](docs/topics/side-by-side/dynamic-dependencies.md)
- [Coalesced runs](docs/topics/side-by-side/coalesced-runs.md)
- [Untracked reads](docs/topics/side-by-side/untracked-reads.md)
- [Derived values and self-writes](docs/topics/side-by-side/derived-and-self-writes.md)
- [Errors](docs/topics/side-by-side/errors.md)
- [Dispose](docs/topics/side-by-side/dispose.md)
- [Async in a reaction](docs/topics/side-by-side/async.md)
