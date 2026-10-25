# Fluorine: Ideas list (not v1)

Ideas collected while writing the semantic spec (`semantics.md`). None of these are part of the spec. Things the spec deliberately does not do also live here.

- Pulse register (signals every write).
- Pilot wave for glitch handling.
- Per-reaction error monitoring.
- Re-running parts of a method via the language state machine.
- Deferred start across threads: even in a multithreaded context, delay starting newly created reactions on other schedulers until the creating reaction's run has finished, to give the same start-up ordering as on a single loop. To be investigated.
- Timed features (delayed runs, requeue after a time, timers) as an optional helper layer on top of the scheduler.
- Synchronous and asynchronous execution from one code base (the WPF version had `SetAndRun` and similar, which broke the rule that a reaction runs after the current execution).
- Observability and trace hooks.
- Code generator for property boilerplate (Visual Studio).
- Observer register built on the listener hooks.

- Reactor as subsystem (idea, 2026-10-18, not v1): all reactions of one reactor must finish before another reactor's reactions start; reactors get their own priority. Related open question: does one reactor imply one message loop?

- Lifetime link (idea, 2026-10-18, was rule 12.4 in draft 0.7, optional and platform-specific): dispose a reactor when another object (e.g. a UI control) is disposed or collected. Platforms choose the mechanism; a helper object with its own finalizer is simpler than a ConditionalWeakTable.

- Flush / wait for completion (idea, 2026-10-18, not v1; hard, was a nightmare to get right in the WPF version). Use case: sometimes one wants to wait until all side effects of a trigger have been processed. Notes:
  1. Synchronous wait for the completion of all side reactions of a trigger.
  2. Synchronous wait for the completion of a given set of reactions.
  3. Synchronous wait for an empty message loop (drawback: pulls in unrelated events that have nothing to do with the reactions of interest).
  4. All of the above, either on the own message loop (which induces the dreaded nested message pump) or on other message loops.
  Possible direction: with reactors, a register in the reactor stating that nothing is executing any more. Werner's view: an explicit "I am done" state is the correct design (a program design issue), yet waiting for side effects remains a real need.

- Nested message pumps (idea, 2026-10-18, not v1; v1 = nested pumps unsupported). Direction: each thread keeps a stack of running reactions; a reaction started while another is active is put on top and all reads go to the top one; the suspended reaction is suspended (not requeued) and keeps its pause depth. Hole: plain non-reaction code (event handlers) running inside the nested pump would have its reads attributed to the suspended reaction. Closing it needs a platform hook that signals when a new pump cycle starts, so the loop can suspend the active reaction and restore it afterwards; WinForms/WPF offer no general hook for modal loops or DoEvents.

## Moved from the spec in draft 0.9 (2026-10-18)

- Collections (observable collections, delta feed, snapshots, the UI bridge; group C, Q17 to 24). Two approaches were tried in the earlier versions and both had issues. Parked. Auxiliary register leaks (Q26) wait for collections.
- Derived values with glitch-freedom (a dependency-ordered derived node).
- Async/await inside reactions (per-platform discussion, see platform-notes.md).
- Cross-process and distributed use (the WCF part of the WPF version).
- Code generation and tooling (a Visual Studio generator is planned after the spec has been tried).
- Cycle detection (the spec has none).
- Pulse-on-equal-write (a pulse register that signals every write).
- Pilot wave: a derived reaction that runs inline. Parked until collections are dealt with again (2026-10-19).
- Per-reaction error monitoring (for example via a weak side table).
- Resuming part of a reaction (reusing the language's own state machine to re-run parts of a method).
- Observability and trace hooks (tracing must not change semantics, for example it must not subscribe anything).
- The synchronous listener hook under the register lock may not be viable long-term. A fully asynchronous variant did not work in the WPF version; the reason is to be looked up.
- Whether a reactor may later become a subsystem with priority (see "Reactor as subsystem" above). Decided in the spec: a reactor is associated with one event loop (12.4).
- Immediate "terminate now" shutdown of an event loop (implementation option, not in the spec).
- Pausing an entire reactor, for example when the user navigates to another page, and resuming it later. It would require large changes to the specification (queued runs, triggers during the pause, subscriptions).
- A diagnostics mode, for example to list the reactions of a reactor.
- Pool scheduler (thread-pool based, working name `PoolScheduler`, to avoid confusion with .NET's `TaskScheduler`; 2026-10-18). A pool has no thread of its own, so the scheduler keeps its own first-in first-out queue and runs at most one reaction at a time, borrowing a pool thread to drain it. `RunReaction` sets `Scheduler.Current` before the lambda starts and restores the previous value afterwards, so `Current` outside a reaction on a pool thread is null or what it was. Thread-static reaction state (active reaction, pause count) is cleared when the run ends, because pool threads are reused. Continuations after an `await` run on pool threads without a scheduler (see 14.6).
- Further target frameworks (2026-10-18): a `netstandard2.0` core with a WinForms layer for `net481` and `net10.0-windows`. The C# document targets .NET Framework 4.8 first.
- Listener hooks ("first listener connected" and "last listener disconnected" on a register; removed 2026-10-18). They did not work well. A reaction that checks a `HasListener` register and then reads it attaches itself to that register, so the listener is never released. In the earlier implementation (Glatt) everything was collected lazily, so even the `HasListeners` state depended on garbage-collection timing and could not be relied on.

- Per-run cleanup callbacks (consideration, 2026-10-19, from the landscape comparison; not v1). Other frameworks let a run register a cleanup action that is called just before the reaction's next run and when it is disposed (Solid `onCleanup`, Tracker `onInvalidate`, Vue `onEffectCleanup`, React `useEffect` returning a cleanup). Typical uses there: remove an event listener, clear an interval, unsubscribe a socket, abort a pending fetch. Werner's comments: he never had a use for it and it never occurred to him; in class-based C# a connection lives in an object with its own state, a reaction may use it or not, and cleanup happens when that object is disposed. Werner's working hypothesis: the JavaScript frameworks need it because components are plain functions without an object to hang state and `Dispose` on, so the effect is the only place left for cleanup. The one case that fits classes less well is a resource whose lifetime is a single run (e.g. cancelling a request when the input changes); in Fluorine that is a field with the previous subscription or a `CancellationTokenSource`, the same pattern as keeping an old value. Related: owner-scoped child reactions are effectively automatic cleanup of reactions created in a run. Werner's further comment (2026-10-19): even the single-run case does not justify it. Inside a C# lambda one should not set up expensive resources and leave them hanging; a `using` block solves resources that are used and released within the run. Remaining niche (research note): a resource that must outlive the run until the next run or disposal (e.g. an event subscription on the object a register currently points to, or an in-flight async request) is not covered by `using`; in Fluorine it is a field holding the previous subscription. Conclusion: no cleanup hook needed; downgraded from "high" to low for Fluorine.
- Experiment: have Grok Build implement the whole spec for JavaScript/TypeScript (idea, 2026-10-19). Question: would it struggle in the same places, and would it end up re-inventing per-run cleanup (and similar function-style helpers) because JavaScript code has no class/Dispose structure to hold that state? This would test both the platform independence of the spec and the hypothesis above.
- Owner-scoped child reactions (consideration, 2026-10-19, from the landscape comparison; conclusion: not needed in Fluorine). What it is: in Solid, Meteor Tracker, Vue (`effectScope`), Leptos and Jetpack Compose, a reaction created during a run of reaction A belongs to that run. It is NOT disposed when A's run ends; it is disposed just before A's next run starts, and when A is disposed. Flow: (1) A is created and queued; (2) A runs and creates B; (3) B is queued; (4) A ends; (5) B runs. Up to here this is identical to Fluorine today. The difference only appears when A runs again: (6) a register A read changes, A is queued; (7) before A's second run, B is disposed; (8) A runs and creates B'; (9) B' runs. Why they do it: A's second run executes the same code and creates a new B anyway; without step 7, B, B', B'' ... all stay alive and subscribed (the growth described in 14.3). Ownership simply means "what a run created is replaced by what the next run creates". Why Fluorine does not need it: this is a need of function-style UI code (components are plain functions with no object to hold state), not of JavaScript as a language; Rust (Leptos) and Kotlin (Compose) have the same ownership tree for the same reason. In Fluorine's class-based style, A creates B once behind a field check (`b ??= ...`) or disposes the previous B it keeps in a field, which is explicit and keeps the current rule 6.4. Implementation note if ever wanted: it would not require inline execution; record what a run creates (e.g. a child reactor per run) and dispose it before the next run.
- An explicit `Run()` on schedulers, so that all schedulers, services and reactions can be set up and bound before any reaction processes data. Left out until there is experience with the Dispatcher and WinForms schedulers.
