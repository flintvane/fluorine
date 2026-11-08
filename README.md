<p align="center"><img src="branding/fluorine-logo.png" alt="Fluorine logo" width="320"></p>

# Fluorine

A reactive programming library, provided in the same form on a variety of platforms.

Program state lives in registers. Code that must stay in step with that state is written as a reaction. A reaction does not declare its dependencies: whatever registers it reads while it runs become its dependencies, and when one changes, the reaction runs again.

Status: the platform-neutral semantic specification is being written. No implementation yet.

## Documentation: using Fluorine from C#

Read the topics in order; each one builds on the ones before it.

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
12. [Derived values, self-writes and cycles](docs/topics/derived-values.md): Reactions that write registers, reactions that follow a state, and reactions that create reactions.
13. [Computed values](docs/topics/computed.md): `Reactor.Computed` writes the result of a function into a register, which triggers only when the result changes.
14. [Errors and logging](docs/topics/errors-and-logging.md): Exceptions in reactions, `Reactive.UnhandledException`, `ReactiveException` and programming errors.
15. [Shutting down](docs/topics/shutdown.md): `Scheduler.Shutdown()` and `Reactive.Shutdown()`: drain, dispose, return.
16. [Async code inside a reaction](docs/topics/async.md): What part of an `async` lambda belongs to the run, and how to write one safely.
17. [Rules of thumb](docs/topics/rules-of-thumb.md): One reaction per thread, one entry and one exit, and a short checklist.
18. [AutoNotify: notifying WPF](docs/topics/autonotify.md): Derive a model from `AutoNotify`, mark properties, and raise `PropertyChanged` when their getters change.
19. [The Reactive attribute and the source generator](docs/topics/reactive-attribute.md): `AddReactiveMethods` turns marked methods into reactions; the generator writes registers for partial properties.
20. [Binding a model to WinForms](docs/topics/winforms-binding.md): `FormsReactor` binds text, visibility and enabled state, with `TextConverter` for typed values.
21. [PersonEditor: one model in two windows](docs/topics/person-editor.md): A demo that shows the same `Person` in WinForms and WPF, and how to build and run it.

## Other frameworks

How Meteor Tracker, Compose, MobX, Knockout, Vue, the signals libraries and the .NET options map onto registers, recorded dependencies and scheduled runs. Start at [Comparison with other frameworks](docs/topics/comparison.md), then compare topic by topic:

- [First reaction](docs/topics/side-by-side/first-reaction.md)
- [Dynamic dependencies](docs/topics/side-by-side/dynamic-dependencies.md)
- [Coalesced runs](docs/topics/side-by-side/coalesced-runs.md)
- [Untracked reads](docs/topics/side-by-side/untracked-reads.md)
- [Derived values and self-writes](docs/topics/side-by-side/derived-and-self-writes.md)
- [Errors](docs/topics/side-by-side/errors.md)
- [Dispose](docs/topics/side-by-side/dispose.md)
- [Async in a reaction](docs/topics/side-by-side/async.md)
