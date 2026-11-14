<p align="center"><img src="logo.png" alt="Fluorine" width="200"></p>

# Fluorine for C#: topics

The master list of the user documentation, in four sections: Guide, WPF, WinForms, and Comparison to other frameworks. Edit this list to add, remove or reorder topics; the pages in `topics/` follow it (see [README.md](maintaining.md)).

Read the Guide in order; each topic builds on the ones before it. WPF and WinForms follow the Guide, then the comparison to other frameworks.

Format, within each section: `N. [Title](topics/slug.md): description`

## Guide

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
12. [Reactive sets](topics/reactive-sets.md): `ReactiveSet<T>` for add, remove and membership; `Version` to enumerate a picture and `Diff` it against one you kept.
13. [Reactive dictionaries](topics/reactive-dictionaries.md): `ReactiveDictionary<TKey, TValue>` for add, replace and remove; `Version` to enumerate a picture and `Diff` it against one you kept.
14. [Reactive sorted sets](topics/reactive-sorted-sets.md): `ReactiveSortedSet<T>` for an ordered set; `Version` enumerates and `Diff` reports changes in comparer order.
15. [Reactive sorted dictionaries](topics/reactive-sorted-dictionaries.md): `ReactiveSortedDictionary<TKey, TValue>` for an ordered map; `Version` enumerates and `Diff` reports changes in comparer order.
16. [Derived values, self-writes and cycles](topics/derived-values.md): Reactions that write registers, reactions that follow a state, and reactions that create reactions.
17. [Computed values](topics/computed.md): `Reactor.Computed` writes the result of a function into a register, which triggers only when the result changes.
18. [Errors and logging](topics/errors-and-logging.md): Exceptions in reactions, `Reactive.UnhandledException`, `ReactiveException` and programming errors.
19. [Shutting down](topics/shutdown.md): `Scheduler.Shutdown()` and `Reactive.Shutdown()`: drain, dispose, return.
20. [Async code inside a reaction](topics/async.md): What part of an `async` lambda belongs to the run, and how to write one safely.
21. [Rules of thumb](topics/rules-of-thumb.md): One reaction per thread, one entry and one exit, and a short checklist.
22. [The Reactive attribute and the source generator](topics/reactive-attribute.md): `AddReactiveMethods` turns marked methods into reactions; the generator writes registers for partial properties.

## WPF

1. [AutoNotify: notifying WPF](topics/autonotify.md): Derive a model from `AutoNotify`, mark properties, and raise `PropertyChanged` when their getters change.
2. [Showing a reactive set in WPF](topics/observable-collections.md): `ObservableCollectionConverter` keeps a WPF `ObservableCollection` in step with a reactive set or dictionary.

## WinForms

1. [Binding a model to WinForms](topics/winforms-binding.md): `FormsReactor` binds text, visibility and enabled state, with `TextConverter` for typed values.
2. [PersonEditor: one model in two windows](topics/person-editor.md): A demo that shows the same `Person` in WinForms and WPF, and how to build and run it.

## Comparison to other frameworks

1. [Comparison with other frameworks](topics/comparison.md): How Meteor Tracker, Compose, MobX, Knockout, Vue, the signals libraries and the .NET options map onto registers, recorded dependencies and scheduled runs. Includes [Side by side](topics/side-by-side/index.md): eight scenarios run in seven frameworks.
