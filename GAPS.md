# Gaps and implementation choices

Sources: `semantics.md` (draft 0.9) and `semantics-cs.md` (draft 0.5). This file records places where those documents are silent, ambiguous, or contradictory, and the choice made so that the library could be built.

## Namespace, assembly, and surface

- **Section:** neither document. **Unclear:** namespace, assembly name, and whether `Reaction` / `Reactor` implement `IDisposable`. **Choice:** namespace `Fluorine`, assemblies `Fluorine` and `Fluorine.Tests`. `Reaction` and `Reactor` implement `IDisposable` because both documents specify `Dispose()`. `Scheduler` does not; lifetime is `Shutdown()`.

- **Section:** semantics-cs.md 2.1, 3.1, 12.4. **Unclear:** whether the “common queueing interface” is a C# `interface` type. **Choice:** both `Scheduler` and `Reactor` expose `Queue(Action)` as ordinary methods; no named interface type.

- **Section:** semantics-cs.md 3.2. **Unclear:** visibility of `Scheduler.Queue(Reaction)`. **Choice:** public, as written. `Reactor.Queue(Reaction)` is internal. A reaction queues through its reactor (2.10) and does not store a scheduler pointer. That internal queue still forwards after the reactor is disposed, so `Dispose` can queue the null-lambda detach run while the reactor is tearing down (2.17).

- **Section:** semantics-cs.md 3.4–3.6. **Unclear:** how a user-written scheduler installs itself on a thread. **Choice:** the protected `Scheduler` constructor installs on the calling thread. Custom schedulers must be constructed on the thread they own. Protected `Schedule(Action)` is the hook that posts work without running it inline.

## Diagnostics, names, monitoring

- **Section:** semantics-cs.md 1.10, 2.16, 7.4, 7.5; semantics.md 15.3–15.4. **Unclear:** what diagnostics mode is and how it is switched on; whether `Name` is kept in non-diagnostics builds; whether `ExecutionCount` exists only in DEBUG. **Choice:** names are always stored. `ExecutionCount` is always compiled so tests and release callers can use it.

- **Section:** semantics-cs.md 2.16 vs 1.9 / 2.5. **Unclear:** whether `Reaction.Name` is settable after `Add`. **Choice:** settable, initialized from the `Add` name argument.

## Equality, writes, backing store

- **Section:** semantics.md 3.1 vs semantics-cs.md 1.16. **Unclear:** the core spec allows another default equality and per-register equality; the C# spec requires `object.Equals`. **Choice:** follow the C# spec (`object.Equals`). `IEquatable<T>` alone does not count. No per-register comparer API.

- **Section:** semantics-cs.md 1.12. **Unclear:** the name and shape of the protected backing value. **Choice:** protected property `StoredValue` with tear-free get/set under the register’s own lock (`lock (this)`), with no private sync object. Direct writes to it do not record a read and do not trigger. The same choice applies to `Reactor` and `Scheduler`: their critical section is the instance (`lock (this)`), including `Monitor.Wait`/`Pulse` on a `ThreadScheduler`. The process-wide scheduler table still uses a dedicated static lock.

- **Section:** semantics-cs.md 1.15. **Unclear:** if equality throws, is `oldValue` still assigned? **Choice:** `oldValue` is assigned from a snapshot taken before equality; the exception still propagates and nothing is stored or triggered.

- **Section:** semantics.md 3.1b / semantics-cs.md 1.18. **Unclear:** lock vs compare-and-swap for small types. **Choice:** every typed store/load takes the register’s critical section. No CAS. Boxing on every write is not used.

## Subscriptions and exceptions

- **Section:** semantics.md 8.1 vs 7.3. **Unclear:** on an exception, “stays connected to every register it read up to the exception” together with “if the exception is thrown before any read happened, the reaction will not rerun without a manual trigger”. **Choice:** 7.3 still runs after an exception: unread previous dependencies are dropped; registers read before the throw stay connected. That matches the “no read ⇒ no rerun” clause.

- **Section:** semantics.md 12.6(c) vs semantics-cs.md 2.17. **Unclear:** if `Dispose` happens during a run, is the follow-up null-lambda run required when the in-progress run can already disconnect? **Choice:** `Dispose` nulls the lambda and requests a run. The in-progress run keeps a local copy of the original lambda and disconnects in `finally` if disposed. A later null-lambda run is a no-op if already disconnected. Reads after disposal create no subscriptions.

- **Section:** semantics.md 12.6(a). **Unclear:** interaction with coalescing when `Add` has already queued the first run and `Dispose` immediately follows. **Choice:** the queued flag coalesces both into one run; that run sees a null lambda and does not execute the original lambda.

## Pause

- **Section:** semantics-cs.md 2.20 vs 2.21. **Unclear:** does `ContinueRecording` at count zero outside a reaction log? **Choice:** outside a reaction there is no pause count, so both `PauseRecording` and `ContinueRecording` are no-ops and do not log. Inside a reaction, count zero logs a programming error and leaves the count at zero; it does not throw. The counter is thread-static, zeroed at the start and end of each run, and only mutated while `Current` is set (9.3, 2.21).

- **Section:** semantics-cs.md 2.24. **Unclear:** disposing `Reaction.Pause()` twice; null delegates; return type is written as `IDisposable`. **Choice:** `Pause()` returns `Reaction.PauseScope`, a struct that implements `IDisposable`, so `using (Reaction.Pause())` does not allocate or box. Dispose continues at most once on that copy. Null `Action`/`Func<T>` is treated as no action (12.9).

## Schedulers: creation, threads, attach

- **Section:** semantics-cs.md 4.2, 5.2. **Unclear:** apartment state, `IsBackground`, thread name. **Choice:** `ThreadScheduler` threads are MTA background threads. `DispatcherScheduler.CreateNew` uses STA background threads, because WPF dispatchers are used from STA. The thread name is the required `name` argument. A null or empty name throws `ReactiveException` and starts no thread.

- **Section:** semantics.md 3.7 vs semantics-cs.md 4.2 / 5.2. **Unclear:** the core spec says starting the loop is explicit via `Run()`; the C# spec’s `CreateNew` starts the loop after the initial action and the base class has no `Run()`. **Choice:** follow 4.2/5.2. `CreateNew` starts the loop. No public `Run()` on `ThreadScheduler` (4.1: same members as `DispatcherScheduler` except `AttachToCurrent`).

- **Section:** semantics.md 3.13–3.14 vs 5.3 / 6.1. **Unclear:** after shutdown, `Current` is null. May a new scheduler be attached to that thread? 3.14 says the scheduler of a thread is the same object for as long as the thread lives. **Choice:** after shutdown the thread has no scheduler. A later `AttachToCurrent` / `CreateNew` on a new thread may install a new scheduler. A second scheduler on a live thread that still has one is a programming error.

- **Section:** semantics-cs.md 5.3. **Unclear:** `AttachToCurrent` when the thread has no `Dispatcher` yet. **Choice:** `Dispatcher.CurrentDispatcher`, which returns the existing dispatcher or creates one. `FromThread` then `CurrentDispatcher` is the same call.

- **Section:** semantics-cs.md 6.1–6.2. **Unclear:** exact type check for “the WinForms one”. **Choice:** `SynchronizationContext.Current as WindowsFormsSynchronizationContext`. Derived contexts that do not inherit that type are rejected. No `CreateNew` (not specified).

- **Section:** semantics-cs.md 3.13. **Unclear:** constructing a second scheduler of the *same* type (not via `AttachToCurrent`). **Choice:** log and throw `ReactiveException`. `AttachToCurrent` of the same type returns the existing instance.

## Queueing, priorities, shutdown

- **Section:** semantics.md 11.9a. **Unclear:** dispatcher / WinForms invoke priority so reactions do not starve UI messages. **Choice:** `DispatcherPriority.Normal`. WinForms `SynchronizationContext.Post`.

- **Section:** semantics-cs.md 3.8 / 5.4 vs 6.3. **Unclear:** does `DispatcherScheduler.Shutdown` stop the dispatcher? WinForms explicitly does not end the message loop. `CreateNew` owns its thread. **Choice:** `CreateNew` dispatchers are shut down with `BeginInvokeShutdown` after work is drained so the thread can end. `AttachToCurrent` does not shut down the dispatcher. `ThreadScheduler.Shutdown` ends the thread (4.3). `WinFormsScheduler.Shutdown` does not call `Application.Exit`.

- **Section:** semantics-cs.md 3.8. **Unclear:** a second `Shutdown` from the owner thread while the first call is draining would wait for itself. **Choice:** nested owner-thread shutdown returns without waiting; the outer drain completes the shutdown.

- **Section:** semantics.md 11.15 vs semantics-cs.md 3.2. **Unclear:** if `Trigger` sets the queued flag and then `Queue` refuses because shutdown has begun, the flag stays set. **Choice:** acceptable; shutdown then disconnects directly (semantics-cs.md 8.2).

- **Section:** semantics-cs.md 3.3 / 12.9. **Unclear:** `Queue(Action)` with a null action. **Choice:** treated as no action, not an error.

- **Section:** semantics-cs.md 7.1. **Unclear:** console stream; multicast handler failures. **Choice:** `Console.WriteLine`. The multicast is invoked as one call. If it throws, both exceptions are written to the console and the scheduler loop continues. Remaining subscribers are not invoked.

- **Section:** semantics-cs.md 4.2 / 5.2 / 7.1–7.3. **Unclear:** how `CreateNew` reports failures, and where the initial action runs. **Resolved:** now specified in semantics-cs 4.2/5.2 (5d6ba24, 43f20a5). The initial action is the first loop message. Its exception is delivered unwrapped through `Reactive.UnhandledException`, and `CreateNew` returns the scheduler. Any other failure while bringing the scheduler up (for example the platform refusing a dispatcher) passes back to the caller unchanged and is not logged; no `ReactiveException` wrapping (cs5.2 @ 43f20a5). The thread is claimed before its queue or dispatcher is created.

## Logging messages and extra public API

- **Section:** semantics.md 15.x. **Unclear:** exception message text. **Choice:** short English sentences stating the programming error. All events carry `ReactiveException`.

- **Section:** neither document. **Unclear:** `Reactor` exposing its `Scheduler`. **Choice:** not public.

## Things in semantics-cs.md that cannot be built on net48 as written

Nothing in the C# realisation requires a surface that net48 lacks:

- WPF `Dispatcher` (`WindowsBase` / `PresentationFramework`) exists on net48.
- `WindowsFormsSynchronizationContext` and `System.Windows.Forms` exist on net48.
- `[ThreadStatic]`, `Interlocked`, `object.Equals`, and `Action<Exception>` exist on net48.
- `async` lambdas (discussion 8.1) are a C# language feature on net48; they are not a separate library API and are not intercepted.

The implementation therefore targets `net48` only, as specified. Optional newer APIs (`Dispatcher.InvokeAsync` as a `Task`, default interface methods, `volatile T` on an open generic) were not used.
