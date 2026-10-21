<p align="center"><img src="logo.png" alt="Fluorine" width="200"></p>

# Fluorine for C#: topics

The master list of the user documentation. Each line is one topic, in reading order. Edit this list to add, remove or reorder topics; the pages in `topics/` follow it (see [README.md](maintaining.md)).

Format: `N. [Title](topics/slug.md): description`

1. [What Fluorine is](topics/introduction.md): Registers hold state, reactions follow it. The one idea behind the library, and when to use it.
2. [Your first reaction](topics/first-reaction.md): A complete program: a scheduler, two registers, a reactor and a reaction that keeps a full name up to date.
3. [Registers](topics/registers.md): `Register<T>`, its `Value` property, `SetValue`, initial values and names.
4. [Reactions and dependencies](topics/reactions.md): How a reaction discovers what it reads, and how its dependencies change from run to run.
5. [When reactions run](topics/when-reactions-run.md): Queued runs, coalescing, the run guarantee and manual triggers.
6. [Equality: what counts as a change](topics/equality.md): `object.Equals`, writes that change nothing, and how to choose sound value types.
7. [Reactors and lifetimes](topics/reactors.md): Reactors own reactions; disposing a reactor or a reaction ends it cleanly.
8. [Schedulers](topics/schedulers.md): `ThreadScheduler`, `DispatcherScheduler` and `WinFormsScheduler`, `Scheduler.Current`, and queueing your own work.
9. [Threads and consistency](topics/threads-and-consistency.md): Writing from any thread, what reactions on other threads see, and aggregate values for consistent state.
10. [Pausing: reads that do not subscribe](topics/pausing.md): `Reaction.Pause` in its three shapes, and `Reaction.Current` / `Reaction.IsRecording`.
11. [Signal registers and shadow registers](topics/signal-registers.md): A `Register` without a value, `TriggerReactions()`, and wrapping controls that hold their own data.
12. [Derived values, self-writes and cycles](topics/derived-values.md): Reactions that write registers, reactions that follow a state, and reactions that create reactions.
13. [Computed values](topics/computed.md): `Reactor.Computed` writes the result of a function into a register, which triggers only when the result changes.
14. [Errors and logging](topics/errors-and-logging.md): Exceptions in reactions, `Reactive.UnhandledException`, `ReactiveException` and programming errors.
15. [Shutting down](topics/shutdown.md): `Scheduler.Shutdown()` and `Reactive.Shutdown()`: drain, dispose, return.
16. [Async code inside a reaction](topics/async.md): What part of an `async` lambda belongs to the run, and how to write one safely.
17. [Rules of thumb](topics/rules-of-thumb.md): One reaction per thread, one entry and one exit, and a short checklist.
18. [Comparison with other frameworks](topics/comparison.md): How Meteor Tracker, Compose, MobX, Knockout, Vue, the signals libraries and the .NET options map onto registers, recorded dependencies and scheduled runs. Includes [Side by side](topics/side-by-side/index.md): eight scenarios run in seven frameworks.
