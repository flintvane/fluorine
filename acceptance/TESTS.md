# Fluorine acceptance tests (spec-derived, independent of Grok Build)

Sources: `spec/semantics-cs.md` draft 0.6 (primary, incl. commits 857afb4, 42ecd91, 2113878/85fd70d) and `spec/semantics.md` draft 0.10, plus Werner's decisions of 2026-10-25 (usage errors thrown, not logged; errors with no caller go to `Reactive.UnhandledException`, including anything escaping a scheduler-loop work item; platform (.NET) and user-code exceptions, incl. an overridden `Schedule`, pass through unwrapped, `ReactiveException` only for Fluorine usage errors (cs7.2, d716df1); cs3.16 refused reaction state undefined); spec 775e8f1 (cs3.7 no explicit scheduler `Run()`, moved to ideas.md; cs3.16 path of an uncaught nested-start exception undefined; cs9.3 throwing `Dispose` is ordinary reaction user code) and ded6a98 (cs10 Errors lists every `ReactiveException` case; cs7.2 points to it). Updated 2026-10-25. Earlier re-checked 2026-10-21 against the spec in repo flintvane/fluorine @ a6b9cbd (exception changes of 2026-10-21, `IsRecording`, s14.7, cs8 Computed, async moved to cs9.1, cs9.2). Non-goals from `spec/ideas.md` are not tested (no flush/wait API, no listener hooks, no pool scheduler, no cycle detection, no nested-pump support, no reactor pause, no WinForms reattach after shutdown).

IDs: `csN.M-xx` = rule in semantics-cs.md; `sN.M-xx` = rule only in semantics.md. IDs are stable: never renumber. Retire a test by marking it `retired` and keep the row.
Kind: **P** positive, **N** negative/error, **E** edge/ordering/concurrency.

## Harness conventions (public API only)

- **S** = a scheduler made with `ThreadScheduler.CreateNew(name)` unless stated. **T(S)** is its thread.
- **Barrier(S)**: `S.Queue(() => evt.Set())`, then wait on `evt` with a timeout (5 s). Because actions and reaction runs share one FIFO (cs3.3, s11.9) this proves everything queued earlier has run. It is a test helper, not a library flush API.
- **Barrier2(S)**: Barrier twice, for runs queued by runs.
- **Log**: a handler on `Reactive.UnhandledException` collecting exceptions. "logged" = exactly one exception arrives there (cs7.1). Only errors with no caller are logged (cs7.3, s15.1): exceptions from a reaction, a queued action or the `CreateNew` initial action, which arrive as the thrown exception itself, unwrapped (cs7.2); and anything else escaping a scheduler-loop work item (Werner 2026-10-25). Errors in how the library is used are thrown to the caller as `ReactiveException` and are **not** logged (cs7.2, s15.2): "throws, not logged" = the caller catches it and nothing arrives at Log. (changed 2026-10-25)
- **TS(S)**: a test subclass of `Scheduler` that implements the protected abstract `Schedule(Action)` by putting the item in its own queue, which the test pumps on one thread; it can call the protected `Run(Reaction)` (cs3.4). Used only where protected members are needed.
- **Console**: capture `Console.Out` and `Console.Error` together (stream is unspecified, see SPEC-GAPS G12).
- Run counts are observed through a counter incremented inside the lambda, not through `ExecutionCount` (an independent check; `ExecutionCount` always exists per cs7.4 @ 4f5118f and may also be used).
- Every test ends with `Reactive.Shutdown()` unless the test itself shuts down.
- Concurrency tests (marked E, "stress") run ≥10 000 iterations; a failure in any iteration fails the test.

## Results

| ID | Title | Kind | Setup | Action | Expected | Status |
|---|---|---|---|---|---|---|
| **cs1 Registers** |||||||
| cs1.1-01 | Value-less register exists and subscribes | P | S; `var sig = new Register()`; reactor R on S | `R.Add(() => { sig.RecordRead(); n++; })`; Barrier; `sig.TriggerReactions()`; Barrier | n == 2 | not yet checked |
| cs1.2-01 | Register<T> is a Register | P | – | `new Register<int>() is Register` | true | not yet checked |
| cs1.3-01 | RecordRead outside a reaction does nothing | E | `sig = new Register()` on test thread (no reaction) | `sig.RecordRead()`; then `sig.TriggerReactions()` | no exception, nothing logged, nothing runs | not yet checked |
| cs1.3-02 | RecordRead while paused does not subscribe | P | reaction does `using (Reaction.Pause()) sig.RecordRead(); n++` | Barrier; `sig.TriggerReactions()`; Barrier | n == 1 | not yet checked |
| cs1.4-01 | Manual trigger runs all connected reactions | P | two reactions on S both read `sig` | Barrier; `sig.TriggerReactions()`; Barrier | each ran twice | not yet checked |
| cs1.4-02 | Manual trigger on Register<T> ignores equality and keeps value | P | `reg = new Register<int>(5)`; reaction reads reg | Barrier; `reg.TriggerReactions()`; Barrier | reaction ran again; `reg.Value == 5` (s4.5) | not yet checked |
| cs1.4-03 | Trigger on register without readers | E | fresh register | `TriggerReactions()` | no exception, nothing logged | not yet checked |
| cs1.5-01 | RecordRead/TriggerReactions from foreign threads | E | reaction on S reads `sig` | call `TriggerReactions()` from 8 threads concurrently | no exception; reaction ran ≥1 more time | not yet checked |
| cs1.6-01 | Empty ctor yields default(T) | P | – | `new Register<int>().Value`, `new Register<string>().Value` | 0, null | not yet checked |
| cs1.7-01 | Initial-value ctor stores value | P | – | `new Register<int>(7).Value` | 7 | not yet checked |
| cs1.7-02 | Initial-value ctor triggers nothing | E | reaction on S reads `a` | inside a reaction run create `new Register<int>(3)`, then Barrier | no extra runs anywhere; nothing logged | not yet checked |
| cs1.8-01 | Getter subscribes current reaction | P | reaction reads `reg.Value` | Barrier; `reg.Value = 1`; Barrier | reaction ran twice | not yet checked |
| cs1.8-02 | Setter with different value triggers | P | reaction reads reg (0) | `reg.Value = 1`; Barrier | ran again, saw 1 | not yet checked |
| cs1.8-03 | Setter with equal value does not trigger | N | reg = 1; reaction reads reg | Barrier; `reg.Value = 1`; Barrier | no extra run (s3.2) | not yet checked |
| cs1.9-01 | Register.Name settable | P | – | `reg.Name = "x"` | no exception; no ctor `Register(string)` / `Register<T>(string, …)` exists (reflection) | not yet checked |
| cs1.9-02 | Register.Name read-back | P | – | set then get Name | "x" (names always kept, cs1.10 @ 4f5118f) | not yet checked |
| cs1.11-01 | Register not sealed; members non-virtual | P | reflection | inspect `Register`, `Register<T>` | not sealed; `Value`, `RecordRead`, `TriggerReactions` not virtual | not yet checked |
| cs1.12-01 | Backing value is protected | P | reflection / subclass | subclass reads backing field | backing member is `protected`; no public setter bypass | not yet checked |
| cs1.13-01 | Ctor and Value from any thread | E | – | create, read, write registers on 8 non-scheduler threads | no exception; final value equals one written value | not yet checked |
| cs1.14-01 | SetValue returns true on change | P | reg = 1 | `SetValue(2)` | true; Value 2; readers triggered | not yet checked |
| cs1.14-02 | SetValue returns false on equal | N | reg = 2, reader | `SetValue(2)`; Barrier | false; no run | not yet checked |
| cs1.15-01 | SetValue(out old) reports old on change | P | reg = 1 | `SetValue(2, out o)` | true; o == 1 | not yet checked |
| cs1.15-02 | SetValue(out old) sets old on no change | E | reg = 2 | `SetValue(2, out o)` | false; o == 2 | not yet checked |
| cs1.15-03 | SetValue(out old) records no read | P | reaction (only access) calls `reg.SetValue(v, out _)` with v = its own constant | Barrier; another thread writes reg to other value; Barrier | reaction did not rerun from that write | not yet checked |
| cs1.16-01 | Equality uses object.Equals | P | type T overrides `Equals` to say equal for distinct instances | write distinct-but-Equal instance | no trigger; old instance still stored | not yet checked |
| cs1.16-02 | IEquatable<T> alone does not count | N | struct/class implementing only `IEquatable<T>.Equals` = true, `object.Equals` = false | write | treated as change: triggers | not yet checked |
| cs1.16-03 | Null handling | E | Register<string> = null | write null; write "a"; write null | null→null no trigger; others trigger | not yet checked |
| cs1.17-01 | Throwing equality reaches writer | N | T whose `Equals` throws X | `reg.Value = other` | X propagates to caller; Value unchanged; no reader runs; nothing logged | not yet checked |
| cs1.18-01 | Large struct does not tear | E stress | `Register<Big>` (e.g. 4×long, all fields equal) | 4 writer threads write (k,k,k,k); readers read | every read has all fields equal | not yet checked |
| **cs2 Reactors and reactions** |||||||
| cs2.1-01 | Reactor.Queue(Action) runs on scheduler | P | R on S | `R.Queue(() => tid = CurrentThread)`; Barrier | tid == T(S) | not yet checked |
| cs2.1-02 | Reactor.Queue on disposed reactor does nothing | N | R disposed | `R.Queue(a)`; Barrier | a never runs; no exception, nothing logged | not yet checked |
| cs2.1-03 | Reactor.Queue(Action) shares FIFO with reactions | E | R on S | `R.Add(r1)`, `R.Queue(a)`, `R.Add(r2)`; Barrier | order r1, a, r2 | not yet checked |
| cs2.2-01 | Reactor() captures Scheduler.Current | P | inside S (queued action) | `new Reactor()`; Add reaction | reaction runs on T(S) | not yet checked |
| cs2.2-02 | Reactor() without scheduler throws, not logged | N | thread with no scheduler | `new Reactor()` | ReactiveException thrown to caller; nothing logged (cs7.2, s15.2) | changed 2026-10-25; not yet checked |
| cs2.2-03 | Captured at creation | E | reactor created on T(S1), later used from T(S2) | `Add` from T(S2) | reaction runs on T(S1) | not yet checked |
| cs2.3-01 | Reactor(scheduler) uses given scheduler | P | test thread (no scheduler) | `new Reactor(S)`; Add | runs on T(S) | not yet checked |
| cs2.3-02 | Reactor(null) throws, not logged | N | – | `new Reactor(null)` | ReactiveException thrown; nothing logged | changed 2026-10-25; not yet checked |
| cs2.4-01 | Reactor on shut-down scheduler acts as disposed | E | S shut down | `new Reactor(S)` | no exception; `Add(...)` returns null; `Queue(a)` never runs a; nothing logged | not yet checked |
| cs2.4-02 | Reactor on scheduler during shutdown acts as disposed | E | inside a queued action on S that runs during S.Shutdown() drain | `new Reactor(S).Add(f)` | returns null; f never runs | not yet checked |
| cs2.5-01 | Reactor.Name settable, no name ctor | P | – | set Name; reflection on ctors | works; only `Reactor()` and `Reactor(Scheduler)` public | not yet checked |
| cs2.6-01 | Reaction has no public constructor | P | reflection | – | `typeof(Reaction)` has no public ctor | not yet checked |
| cs2.7-01 | Add returns reaction and queues first run | P | R on S | `var r = R.Add(f)`; Barrier | r != null; f ran once | not yet checked |
| cs2.7-02 | Add(name, lambda) | P | – | `R.Add("n", f).Name` | "n"; f ran | not yet checked |
| cs2.8-01 | Never inline, even on scheduler thread | E | inside an action on S | `R.Add(() => ran = true)`; check `ran` immediately | false right after Add; true after Barrier | not yet checked |
| cs2.9-01 | Add on disposed reactor returns null silently | N | R disposed | `R.Add(f)`; Barrier | null; f never runs; nothing logged | not yet checked |
| cs2.11-01 | Reactor dispose disposes reactions | P | R with 3 reactions reading reg | Barrier; `R.Dispose()`; Barrier; write reg; Barrier | none rerun | not yet checked |
| cs2.12-01 | Reaction dispose removes it from reactor | P | R with r1, r2 on reg | `r1.Dispose()`; write reg; Barrier; `R.Dispose()` | r1 doesn't run, r2 ran; R.Dispose fine and r2 stops | not yet checked |
| cs2.13-01 | Null lambda is empty block | E | R on S | `var r = R.Add(null)`; Barrier; `r.Trigger()`; Barrier | r != null; no exception, nothing logged | not yet checked |
| cs2.13-02 | Lambda not changeable | P | reflection | – | no public settable lambda member on Reaction | not yet checked |
| cs2.14-01 | Lambda as ordinary call records nothing | P | `Action f = () => reg.Value…`; | call `f()` on test thread; write reg | no exception; nothing runs | not yet checked |
| cs2.15-01 | Trigger queues run and returns | P | r on S, idle | `r.Trigger()`; Barrier | ran once more | not yet checked |
| cs2.15-02 | Trigger never inline | E | from inside an action on S | `r.Trigger()`; check flag | not run until after the action | not yet checked |
| cs2.15-03 | Trigger on disposed reaction | N | r disposed | `r.Trigger()`; Barrier | no run, no throw, nothing logged | not yet checked |
| cs2.16-01 | Reaction.Name equals name given to Add | P | – | `R.Add("abc", f).Name` | "abc" | not yet checked |
| cs2.17-01 | Dispose detaches from all registers | P | r reads a, b | `r.Dispose()`; Barrier; write a, b; Barrier | no further runs | not yet checked |
| cs2.17-02 | Dispose from foreign thread | E | r on S | `r.Dispose()` from test thread | no exception; no further runs | not yet checked |
| cs2.17-03 | Dispose twice harmless | E | – | `r.Dispose(); r.Dispose()` | no exception, nothing logged | not yet checked |
| cs2.17-04 | Queued run after dispose does not run lambda | E | S blocked by an action waiting on gate; `r.Trigger()` queued | `r.Dispose()`; open gate; Barrier | lambda did not run (s12.6a) | not yet checked |
| cs2.17-05 | Self-dispose mid-run | E | lambda: read a; `self.Dispose()`; read b; n++ | Barrier; write a; write b; Barrier | run completes (n==1); no further runs (s12.6c,d) | not yet checked |
| cs2.17-06 | Dispose during run from other thread | E | lambda blocks on gate after reading a | dispose from test thread, open gate, Barrier2, write a, write b read after dispose | run completes; no further run; post-dispose reads did not subscribe | not yet checked |
| cs2.18-01 | Reaction.Current inside run | P | – | lambda captures `Reaction.Current` | equals the returned reaction | not yet checked |
| cs2.18-02 | Reaction.Current null outside | P | in queued action and on test thread | read `Reaction.Current` | null | not yet checked |
| cs2.18-03 | Reaction.Current while paused | E | inside `Reaction.Pause(...)` | read Current | still the reaction | not yet checked |
| cs2.19-01 | PauseRecording stops recording | P | lambda: `PauseRecording(); reg.Value; ContinueRecording()` | write reg | no rerun | not yet checked |
| cs2.20-01 | ContinueRecording resumes | P | lambda: Pause; Continue; read reg | write reg | reruns | not yet checked |
| cs2.20-02 | Extra ContinueRecording throws, count stays 0 | N | lambda: `try { ContinueRecording(); } catch (ReactiveException) { caught = true; }`; then read reg | Barrier; write reg; Barrier | caught; nothing logged; `Reaction.IsRecording` true after the catch; read after it IS recorded (reruns) (cs2.20, s15.5) | changed 2026-10-25; not yet checked |
| cs2.21-01 | Pause/Continue outside reaction no effect | E | test thread / queued action | `PauseRecording(); ContinueRecording(); ContinueRecording()` | no exception; nothing logged; `Reaction.IsRecording` false (cs2.21; G8 resolved) | changed 2026-10-25; not yet checked |
| cs2.22-01 | IsRecording true in unpaused run | P | – | read `Reaction.IsRecording` in lambda | true | changed 2026-10-21; not yet checked |
| cs2.22-02 | IsRecording false paused / outside | P | – | inside Pause; in action; on test thread | false, false, false | changed 2026-10-21; not yet checked |
| cs2.23-01 | Unbalanced pause ends with run | E | lambda run 1: `PauseRecording()` only, reads nothing; triggered manually; run 2 reads reg | write reg after run 2 | run 3 happens (run 2 recorded) | not yet checked |
| cs2.24-01 | Pause() IDisposable | P | `using (Reaction.Pause()) reg.Value` | write reg | no rerun; `IsRecording` true after using | changed 2026-10-21; not yet checked |
| cs2.24-02 | Pause(Action) and Pause<T>(Func<T>) | P | – | `Reaction.Pause(() => a.Value)`; `var v = Reaction.Pause(() => b.Value)` | v correct; neither a nor b triggers | not yet checked |
| cs2.24-03 | Pause(Action) restores on throw | E | Pause(() => throw X) caught in lambda; then read reg | write reg | reruns (recording restored) | not yet checked |
| cs2.24-04 | Reentrant pauses | P | nested Pause twice; after inner end read c; after outer end read d | write c; then write d | c no rerun; d reruns | not yet checked |
| **cs3 Schedulers** |||||||
| cs3.1-01 | Scheduler abstract base of all | P | reflection | – | `Scheduler` abstract; ThreadScheduler, DispatcherScheduler, WinFormsScheduler derive from it | not yet checked |
| cs3.1-02 | User-derived scheduler possible | P | TS(S) | `new Reactor(TS).Add(f)`; pump | compiles with only `Schedule(Action)` overridden; f runs on the pumping thread via base `Queue(Reaction)` → `Schedule` → `Run(Reaction)` | changed 2026-10-25; not yet checked |
| cs3.2-01 | Queue(Reaction) never inline/never throws | P | via `Reaction.Trigger` and TS(S) | queue from scheduler thread | not run inline; no exception | changed 2026-10-25; not yet checked |
| cs3.2-02 | Queue(Reaction) after shutdown is silent | N | S shut down; reaction r of S captured earlier (disposed) | `S.Queue(r)` | no throw, no log, no run | not yet checked |
| cs3.3-01 | Queue(Action) runs on scheduler thread | P | – | `S.Queue(a)`; Barrier | ran on T(S) | not yet checked |
| cs3.3-02 | Actions and runs share FIFO | E | S gated | Add r1; Queue a1; trigger r2; Queue a2; open | order r1,a1,r2,a2 | not yet checked |
| cs3.3-03 | Action records no reads | P | action reads reg | write reg; Barrier | nothing runs; `Reaction.Current` null in action | not yet checked |
| cs3.3-04 | Action exception logged unwrapped, loop survives | N | `S.Queue(() => throw X)`; then Queue b | Barrier | exactly X (same instance, not a ReactiveException) logged once; b ran (cs7.2) | changed 2026-10-21; not yet checked |
| cs3.3-05 | Queue(Action) never inline | E | from within an action on S | `S.Queue(a)` then check | a not yet run | not yet checked |
| cs3.3-06 | Queue(null action) | E | – | `S.Queue((Action)null)` | no throw; nothing logged (s12.9) | not yet checked |
| cs3.3-07 | Actions queued before shutdown run | P | S gated; Queue a ×3 | `Shutdown()` from test thread (open gate) | all 3 ran before Shutdown returned | not yet checked |
| cs3.3-08 | Action queued after shutdown began ignored | N | action a1 queued; a1 queues a2 during drain | `S.Shutdown()` | a2 never runs; silent | not yet checked |
| cs3.4-01 | Run path visibility | P | reflection | – | no public `Run` on Reaction; `Scheduler.Schedule(Action)` is protected abstract; `Scheduler.Run(Reaction)` is protected; no `RunReaction` member | changed 2026-10-25; not yet checked |
| cs3.4-02 | Queue hands work to Schedule | P | TS(S) counting `Schedule` calls | `TS.Queue(a)`; `TS.Queue(r)` (r of a reactor on TS) | each leads to one `Schedule` call; nothing runs until the item is pumped; pumping runs a, then r's lambda synchronously inside `Run(Reaction)` | new 2026-10-25; not yet checked |
| cs3.4-03 | Run(Reaction) is synchronous and records | P | TS(S); r reads reg | test calls protected `Run(r)` directly on the pump thread; then writes reg | lambda ran before `Run` returned; `Reaction.Current` inside == r; write queues a new `Schedule` call | new 2026-10-25; not yet checked |
| cs3.5-01 | Dependencies rediscovered each run | P | lambda: `if (a.Value) b.Value` ; a=true | Barrier; a=false; Barrier; write b; Barrier | no run from b after branch dropped; writing a=true reattaches b | not yet checked |
| cs3.5-02 | Exception in lambda logged, keeps reads before | N | lambda: read a; throw X; read b | Barrier; write b; Barrier; write a; Barrier | X itself logged (unwrapped, cs7.2) each throwing run; b no rerun; a reruns (s8.1) | changed 2026-10-21; not yet checked |
| cs3.5-03 | Throw before any read → no rerun without trigger | E | lambda: throw first | write anything; `r.Trigger()` | no rerun until Trigger; Trigger reruns | not yet checked |
| cs3.6-01 | Scheduler.Current on own thread | P | – | read in action | == S | not yet checked |
| cs3.6-02 | Scheduler.Current null elsewhere | P | test thread | read | null | not yet checked |
| cs3.6-03 | Still installed during drain | E | action queued before Shutdown reads Current | Shutdown | action saw S | not yet checked |
| cs3.6-04 | Removed after shutdown finished | E | DispatcherScheduler/WinForms thread still alive after Shutdown (ThreadScheduler thread ends) | read Current on that thread after Shutdown | null | not yet checked |
| cs3.7-01 | Base class has no event-loop Run() | P | – | – | – | retired 2026-10-25 (775e8f1): asserted on scheduler/event-loop `Run()`; cs3.7 has no explicit Run (moved to ideas.md, non-goal). Protected `Run(Reaction)` stays, see cs3.4-01 |
| cs3.7-02 | Joining schedulers have no Run() | P | – | – | – | retired 2026-10-25 (775e8f1): asserted on `Run()` of joining/loop-owning schedulers; cs3.7 has no explicit Run (moved to ideas.md) |
| cs3.7-03 | Loops run from creation / use the existing loop | P | – | `ThreadScheduler.CreateNew(n)` and `DispatcherScheduler.CreateNew(n)`, then `S.Queue(a)`; Barrier, with no further call. `WinFormsScheduler.AttachToCurrent()` on a thread already in `Application.Run`; Queue b | a runs on T(S) without any start call; b runs on the existing message loop (cs3.7 @ 775e8f1; see also cs4.2-02, cs5.2-05, cs6.1-01) | new 2026-10-25; not yet checked |
| cs3.8-01 | Shutdown drains queued runs | P | S gated; 3 reactions triggered | `Shutdown()` | all 3 ran before return | not yet checked |
| cs3.8-02 | Shutdown disposes reactors | P | reaction reads reg | Shutdown; write reg | no run; `R.Add` returns null | not yet checked |
| cs3.8-03 | Runs queued during drain are dropped | N | r1 drained triggers r2 / writes reg read by r2 | Shutdown | r2 does not run again | not yet checked |
| cs3.8-04 | Waiting-only reaction is disposed | E | r reads reg, never triggered | Shutdown; write reg | r never runs again (s11.15) | not yet checked |
| cs3.8-05 | Shutdown from any thread | P | – | Shutdown from test thread and from T(S2) of another scheduler | returns; S finished | not yet checked |
| cs3.8-06 | Shutdown inside reaction throws, not logged | N | lambda calls `S.Shutdown()` in try/catch | Barrier | ReactiveException caught in lambda; nothing logged; S not shut down (still runs work) | changed 2026-10-25; not yet checked |
| cs3.8-07 | Shutdown inside paused reaction throws | N | same inside `Reaction.Pause(...)` | – | same as cs3.8-06 | changed 2026-10-25; not yet checked |
| cs3.8-08 | Shutdown inside reaction of another scheduler | N | lambda on S2 calls `S1.Shutdown()` in try/catch | – | ReactiveException caught; nothing logged; S1 not shut down | changed 2026-10-25; not yet checked |
| cs3.8-09 | Shutdown on own thread outside reaction | E | DispatcherScheduler: from a dispatcher message that is not a reaction (and see G4 for queued actions) with work queued | `S.Shutdown()` | processes open work then returns; no deadlock | not yet checked |
| cs3.8-10 | Second Shutdown waits | E | S drain blocked on gate; thread A calls Shutdown | thread B calls Shutdown; release gate | B does not return before A's shutdown finished; drained work ran once | not yet checked |
| cs3.8-11 | Shutdown after finished returns | E | S finished | `S.Shutdown()` again | returns promptly, no throw, no log | not yet checked |
| cs3.8-12 | First run of reaction created during shutdown refused | N | drained action does `R.Add(f)` on existing reactor | Shutdown | f never runs; returned value null or reaction that never runs | not yet checked |
| cs3.9-01 | Schedulers announced automatically | P | 2 schedulers, no registration | `Reactive.Shutdown()` | both finished | not yet checked |
| cs3.10-01 | One scheduler, many reactors | P | 100 reactors on S | add and trigger | all run on T(S) | not yet checked |
| cs3.11-01 | Reactive.Shutdown shuts all | P | S1, S2, S3 with queued work | `Reactive.Shutdown()` | all drained, all `ForThread` null | not yet checked |
| cs3.11-02 | Reactive.Shutdown informs without waiting between | E | S1 drain blocks on gate G1; S2 has queued action that sets E2 | Reactive.Shutdown on thread A; wait E2 before opening G1 | E2 set while S1 still blocked (S2 shutdown began independently); A returns only after G1 opened | not yet checked |
| cs3.11-03 | Reactive.Shutdown inside reaction throws | N | lambda calls `Reactive.Shutdown()` in try/catch | – | ReactiveException caught; nothing logged; no scheduler shut down | changed 2026-10-25; not yet checked |
| cs3.11-04 | Finished scheduler removed | E | S finished; then `Reactive.Shutdown()` | – | returns, does not hang or throw on S | not yet checked |
| cs3.12-01 | Blocked scheduler does not block another | E | S1 action blocks on gate | work on S2 and triggers from S1 regs to S2 reactions | S2 work completes while S1 blocked | not yet checked |
| cs3.12-02 | Cross-scheduler triggering no deadlock | E stress | r1 on S1 reads a writes b; r2 on S2 reads b writes a (bounded counters) | run 10 000 rounds | completes, no deadlock | not yet checked |
| cs3.13-01 | ForThread returns scheduler | P | – | `Scheduler.ForThread(T(S))` | S | not yet checked |
| cs3.13-02 | ForThread null without scheduler | N | plain thread | ForThread | null | not yet checked |
| cs3.13-03 | ForThread null after shutdown | E | S finished (thread alive for Dispatcher/WinForms) | ForThread | null | not yet checked |
| cs3.13-04 | Second scheduler type throws | N | WinForms thread with WinFormsScheduler attached (and WPF dispatcher present) | `DispatcherScheduler.AttachToCurrent()` | ReactiveException; nothing logged; ForThread still the WinForms one | changed 2026-10-25; not yet checked |
| cs3.13-05 | Reverse direction | N | DispatcherScheduler on thread with WinForms context | `WinFormsScheduler.AttachToCurrent()` | ReactiveException; nothing logged | changed 2026-10-25; not yet checked |
| cs3.13-06 | CreateNew cannot add a second scheduler | E | – | `ThreadScheduler.CreateNew(n)` / `DispatcherScheduler.CreateNew(n)` | each new thread gets exactly one scheduler; `ForThread(T(S)) == S` | new 2026-10-25; not yet checked |
| cs3.14-01 | Same object for thread life | P | – | `AttachToCurrent()` twice; ForThread; Current | all same instance | not yet checked |
| cs3.15-01 | Nested pump in reaction is detected | N | WPF thread: lambda runs a nested `DispatcherFrame` while r2 is queued | – | r2 does not start inside r1 (the refused start throws `ReactiveException`, cs3.16). Where that exception ends up is not asserted (undefined, cs3.16 @ 775e8f1); nothing asserted about r2 afterwards | changed 2026-10-25 (775e8f1: dropped 'arrives at Log'); not yet checked |
| cs3.16-01 | Run(Reaction) with running reaction throws | N | TS(S); inside r1's lambda the test subclass calls protected `Run(r2)` in try/catch | pump | r2's lambda does not start; `ReactiveException` reaches the caller of `Run`; nothing logged by Fluorine for it; r1 completes. Nothing asserted about r2 after the throw (state undefined, cs3.16) | changed 2026-10-25; not yet checked |
| cs3.16-02 | Refused nested start adds no run | E | – | – | – | retired 2026-10-25: asserted r2's later run count, but cs3.16 (857afb4) makes the refused reaction's state undefined |
| cs3.16-03 | Nested start from a loop is reported, loop continues | N | – | – | – | retired 2026-10-25 (775e8f1): asserted that an uncaught nested-start exception reaches the log; cs3.16 makes where it ends up undefined. The defined part (thrown to the caller of `Run`) is cs3.16-01 |
| **cs4 Thread scheduler** |||||||
| cs4.1-01 | No public ctor, no AttachToCurrent | P | reflection | – | none | not yet checked |
| cs4.2-01 | CreateNew runs initial on new thread first | P | – | `CreateNew(name, () => {t=CurrentThread; cur=Scheduler.Current; done=true;})` | done == true when CreateNew returns; t ≠ caller; t.Name == name; cur == returned S | changed 2026-10-25; not yet checked |
| cs4.2-02 | CreateNew loop runs after initial | P | initial adds reaction | Barrier | reaction ran on T(S), after CreateNew returned | not yet checked |
| cs4.2-03 | CreateNew(name) with no action | E | – | `CreateNew(name)` | valid scheduler | not yet checked |
| cs4.2-04 | Initial throws: unhandled, scheduler continues | N | initial throws X | `CreateNew(name, ...)` | CreateNew returns S; X itself is logged once, unwrapped; a later Queue runs; Shutdown then ends the thread; `Reactive.Shutdown()` returns promptly | changed 2026-10-25 |
| cs4.2-05 | Null/empty name throws, no thread | N | count threads named "" / scheduler list | `CreateNew(null)`, `CreateNew("")` | ReactiveException to caller; nothing logged; initial never runs; no scheduler created (`Reactive.Shutdown()` returns promptly) (cs4.2→5.2) | new 2026-10-25; not yet checked |
| cs4.2-06 | Initial action is the first message | E | initial blocks on gate G; meanwhile thread B calls `Reactive.Shutdown()` | open G | the initial action ran to completion before any drain/shutdown work of S; CreateNew returned S; Reactive.Shutdown then finishes S | new 2026-10-25; not yet checked |
| cs4.2-07 | Start-up failure throws ReactiveException | N | – | – | expected: caller receives `ReactiveException` with the original exception as `InnerException`; not logged; no scheduler remains (cs5.2, cs10 @ b910361). Only the initial action's exception goes unwrapped to `UnhandledException` (cs4.2-06/cs5.2-04). Not testable through the public API (no fault injection) | new 2026-10-25; not testable (no fault injection) |
| cs4.3-01 | Queue never blocks | E | S action blocked on gate | `S.Queue(a)` ×1000 from test thread | returns quickly | not yet checked |
| cs4.3-02 | Thread ends after Shutdown | P | – | Shutdown; `T(S).Join(5s)` | thread ended | not yet checked |
| **cs5 WPF Dispatcher scheduler** |||||||
| cs5.1-01 | No public ctor | P | reflection | – | none | not yet checked |
| cs5.2-01 | CreateNew initial then dispatcher | P | – | `CreateNew(name, init)`; Barrier | init on new thread named `name`, with `Dispatcher.CurrentDispatcher` and Scheduler.Current == S; queued work runs via dispatcher | not yet checked |
| cs5.2-02 | CreateNew initial throws | N | init throws X | CreateNew(name, ...) | CreateNew returns S; X itself logged once, unwrapped (cs7.3); a later Queue runs on the dispatcher; Shutdown ends the thread | changed 2026-10-25 |
| cs5.2-03 | Null/empty name throws, no thread | N | – | `DispatcherScheduler.CreateNew(null)`, `CreateNew("")` | ReactiveException to caller; nothing logged; initial never runs; no scheduler remains | new 2026-10-25; not yet checked |
| cs5.2-04 | Initial action is the first message | E | initial blocks on gate G; thread B calls `Reactive.Shutdown()` | open G | initial completes before any other work on that dispatcher; CreateNew returns S; Reactive.Shutdown then finishes S | new 2026-10-25; not yet checked |
| cs5.2-05 | Dispatcher keeps running after CreateNew | P | – | `CreateNew(n, init)`; `S.Queue(a)`; Barrier | a runs on T(S) after CreateNew returned | new 2026-10-25; not yet checked |
| cs5.2-06 | Start-up failure throws ReactiveException | N | – | – | expected: caller receives `ReactiveException` with the original exception as `InnerException`; not logged; no scheduler remains (cs5.2, cs10 @ b910361). Only the initial action's exception goes unwrapped to `UnhandledException` (cs4.2-06/cs5.2-04). Not testable through the public API (no fault injection) | new 2026-10-25; not testable (no fault injection) |
| cs5.3-01 | AttachToCurrent on dispatcher thread | P | STA thread with Dispatcher | `AttachToCurrent()`; queue work; run dispatcher | work runs on that thread | not yet checked |
| cs5.3-02 | AttachToCurrent idempotent | P | – | call twice | same instance | not yet checked |
| cs5.4-01 | Queue hands to dispatcher, non-blocking | E | dispatcher busy in a long message | `S.Queue(a)` from other thread | returns immediately; a runs later on dispatcher thread | not yet checked |
| cs5.4-02 | UI messages not starved | E | self-retriggering reaction (s6.2) | post `Dispatcher.BeginInvoke` input-priority item | item runs within 1 s (s11.9a) | KNOWN/ACCEPTED FAILURE: DispatcherScheduler posts at DispatcherPriority.Normal, kept by Werner's decision 2026-10-21 (priorities deferred); fails as expected until priorities are addressed. 1 s bound is a heuristic |
| **cs6 WinForms scheduler** |||||||
| cs6.1-01 | AttachToCurrent on WinForms thread | P | STA thread with `WindowsFormsSynchronizationContext` installed and `Application.Run` | attach; queue work | runs on that thread | not yet checked |
| cs6.1-02 | AttachToCurrent idempotent | P | – | twice | same instance | not yet checked |
| cs6.1-03 | No WinForms context throws, not logged | N | plain thread / thread with other SynchronizationContext | `WinFormsScheduler.AttachToCurrent()` | ReactiveException; nothing logged; no scheduler for the thread | changed 2026-10-25; not yet checked |
| cs6.2-01 | No user-context API | P | reflection | – | no public member accepting a `SynchronizationContext` | not yet checked |
| cs6.3-01 | Queue non-blocking | E | UI thread busy | Queue from other thread | returns immediately | not yet checked |
| cs6.3-02 | Shutdown does not end message loop | P | attached scheduler | Shutdown; then `form.BeginInvoke(x)` | x still runs; `Application.Run` still active | not yet checked |
| **cs7 Logging and monitoring** |||||||
| cs7.1-01 | UnhandledException is static event Action<Exception> | P | reflection | – | public static event of type `Action<Exception>` on `Reactive` | not yet checked |
| cs7.1-02 | No handler → console warning | N | no handler; capture console | reaction throws | something written to console; loop continues | not yet checked |
| cs7.1-03 | Throwing handler → console warning with both, loop continues | N | handler throws H | reaction throws X; then Queue b | console text mentions X and H (message or type); b runs | not yet checked |
| cs7.1-04 | Multiple handlers | E | two handlers | one error | each called once | not yet checked |
| cs7.2-01 | ReactiveException ctors | P | – | three ctors | Message/InnerException set; derives from Exception | not yet checked |
| cs7.2-02 | Reaction exception logged unwrapped | P | lambda throws X | – | handler gets X itself (same instance); no `ReactiveException` wrapper (cs7.2) | changed 2026-10-21; not yet checked |
| cs7.3-01 | Only user errors without a caller are logged | N | sub-cases: (a) reaction throws X; (b) queued action throws X; (c) CreateNew initial throws X; (d) each usage error of cs10.1 (see cs10.1-01) | – | (a)–(c): X logged once, unwrapped; (d): thrown to caller, nothing logged (cs7.2, cs7.3, s15.2) | changed 2026-10-25; not yet checked |
| cs7.4-01 | ExecutionCount counts runs | P | – (always present, cs7.4 @ 4f5118f) | 3 runs | ExecutionCount == 3 | not yet checked |
| cs7.4-02 | ExecutionCount readable from any thread, no side effects | E | – | read from test thread during runs | no exception; no subscription created | not yet checked |
| **cs8 Computed values** (cs8.1-01..03 retired, async now cs9.1) |||||||
| cs8.1-01 | Reads before first suspending await recorded | P | `async () => { a.Value; await Task.Delay(50); b.Value; }` | write a; then write b | a reruns; b does not | retired 2026-10-21: async moved to cs9.1, ID reused by section 8 Computed; see cs9.1-01 |
| cs8.1-02 | Continuation has no Current | E | as above, capture `Reaction.Current` after await | – | null | retired 2026-10-21: async moved to cs9.1, ID reused by section 8 Computed; see cs9.1-02 |
| cs8.1-03 | Exception after await not handled by reaction | E | Func<Task> lambda throws after await | – | no ReactiveException via UnhandledException from the reaction | retired 2026-10-21: async moved to cs9.1, ID reused by section 8 Computed; see cs9.1-03 |
| cs8.1-04 | Computed returns a reaction and writes the result | P | R on S; `dest = new Register<int>(0)`; `a = new Register<int>(2)` | `var r = R.Computed(dest, () => a.Value * 10)`; Barrier | r != null; `dest.Value == 20` | not yet checked |
| cs8.1-05 | Computed follows its inputs | P | as cs8.1-04 | `a.Value = 3`; Barrier2 | `dest.Value == 30`; func ran on T(S) | not yet checked |
| cs8.1-06 | Computed reaction is an ordinary reaction | P | as cs8.1-04 | `r.Trigger()`; Barrier; then `r.Dispose()` | func reruns once on Trigger; after Dispose, writes to a no longer change dest; `R.Dispose()` also stops it (s12.3) | not yet checked |
| cs8.2-01 | Equal result does not trigger downstream | P | `dest` computed as `a.Value > 0`; reaction d reads dest; count d runs | Barrier2; `a.Value = 5`; `a.Value = 7`; Barrier2 | d ran once (initial) + once for false→true only; `a = 7` does not rerun d | not yet checked |
| cs8.2-02 | Register stays caller-owned and writable | P | computed dest | user writes `dest.Value = 99` from test thread; then `a` changes; Barrier2 | 99 visible after the user write; next computation overwrites it with the new result | not yet checked |
| cs8.2-03 | Equality is object.Equals | E | T with `object.Equals` true for distinct instances; func returns a new equal instance each run | trigger input; Barrier2 | dest keeps the first instance; downstream not triggered (cs1.16) | not yet checked |
| cs8.3-01 | First run queued, not inline | E | inside an action on S; `dest = new Register<int>(5)` | `R.Computed(dest, () => 1)`; read dest immediately; Barrier | 5 right after Computed; 1 after Barrier | not yet checked |
| cs8.4-01 | Null function writes default(T) | E | `dest = new Register<int>(7)` | `R.Computed(dest, null)`; Barrier | returns non-null reaction; `dest.Value == 0`; nothing logged | not yet checked |
| cs8.4-02 | Function fixed | P | reflection | – | no public member on Reaction to replace the func; a second `Computed` call creates a second reaction | not yet checked |
| cs8.5-01 | Throwing function logged, register unchanged | N | `dest = new Register<int>(4)`; func reads a, throws X when a == 1 | `a.Value = 1`; Barrier2 | X logged once, unwrapped (cs7.2); `dest.Value == 4`; downstream not triggered | not yet checked |
| cs8.5-02 | Throwing function stays subscribed to reads before the throw | N | as cs8.5-01 | `a.Value = 2`; Barrier2 | func reran (s8.1); dest updated | not yet checked |
| cs8.6-01 | disposeOld disposes replaced IDisposable | P | `Register<D>` with D : IDisposable; computed with `disposeOld: true`; input switches value | switch; Barrier2 | old instance disposed once; new instance not disposed | not yet checked |
| cs8.6-02 | Dispose after store and trigger | E | as cs8.6-01; old D.Dispose() records `dest.Value` and whether a downstream reaction has been queued/run | switch; Barrier2 | at Dispose time dest already holds the new value and downstream was already triggered | not yet checked |
| cs8.6-03 | No dispose when unchanged, false, or not IDisposable | N | (a) disposeOld true, func returns equal value; (b) disposeOld false; (c) T not IDisposable | rerun func | (a) nothing disposed; (b) nothing disposed; (c) no exception | not yet checked |
| cs8.6-04 | Throwing Dispose of old value is ordinary reaction user code | N | as cs8.6-01; old D.Dispose() throws X | switch; Barrier2 | handled like any reaction exception (cs8.1, cs9.3): X logged once, unwrapped; no `ReactiveException`; nothing else special asserted (what Dispose left undone stays undone) | new 2026-10-25 (775e8f1, G23); not yet checked |
| cs8.7-01 | Computed on disposed reactor returns null | N | R disposed (and separately: R on shut-down scheduler, cs2.4) | `R.Computed(dest, f)`; Barrier | null; f never runs; dest unchanged; nothing logged | not yet checked |
| cs8.7-02 | Stopping keeps last value | P | computed dest = 10 | dispose reaction (and separately the reactor); change input; Barrier2 | dest stays 10 | not yet checked |
| **cs9 Discussions (informative)** ||||||| |
| cs9.1-01 | Reads before first suspending await recorded | P | `async () => { a.Value; await Task.Delay(50); b.Value; }` | write a; then write b | a reruns; b does not (was cs8.1-01) | not yet checked |
| cs9.1-02 | Continuation has no Current | E | as above, capture `Reaction.Current` after await | – | null (was cs8.1-02) | not yet checked |
| cs9.1-03 | Exception after await not handled by reaction | E | Func<Task> lambda throws after await | – | nothing arrives at UnhandledException from the reaction (was cs8.1-03) | not yet checked |
| cs9.2-01 | Shutdown detaches without a queued run | E | r reads reg; user calls `R.Dispose()` from a drained action during Shutdown (detach run refused) | Shutdown; then inspect via a new scheduler: write reg | r never runs; no exception (cs9.2, resolves G4) | not yet checked |
| **cs10 Errors** (every case of the cs10.1 table, @ ded6a98; each: misuse → `ReactiveException` thrown to the caller, not logged) |||||||
| cs10.1-01 | Coverage of the cs10.1 case list | N | – | cross-reference | reactor without scheduler: cs2.2-02, s15.2-01; null scheduler: cs2.3-02; ContinueRecording at zero: cs2.20-02, s15.2-01; Scheduler.Shutdown inside a reaction: cs3.8-06, cs3.8-07, cs3.8-08; second scheduler on a thread: cs3.13-04, cs3.13-05, cs10.1-02; nested start: cs3.16-01; null/empty thread name: cs4.2-05, cs5.2-03; WinForms without context: cs6.1-03 | new 2026-10-25; note row |
| cs10.1-02 | AttachToCurrent of another type after CreateNew | N | inside the initial action of `ThreadScheduler.CreateNew(n, init)`, with a WinForms context installed on that thread | `WinFormsScheduler.AttachToCurrent()` in try/catch | ReactiveException caught; nothing logged; `ForThread(T(S)) == S` (cs3.13, cs10.1 row 'second scheduler') | new 2026-10-25; not yet checked |
| **s1 Threading model** |||||||
| s1.2-01 | Runs never parallel with themselves | E stress | lambda increments in-flight counter, sleeps, decrements; triggered from 8 threads | – | in-flight never >1 | not yet checked |
| s1.4-01 | Coalescing | P | S gated; r reads reg | 100 writes; open; Barrier | r ran exactly once after the writes | not yet checked |
| s1.5-01 | Different schedulers in parallel | E | r1 on S1 waits on E set by r2 on S2 | – | both complete (no serialisation) | not yet checked |
| s1.8-01 | Trigger/queue never throws | N | after shutdown, on disposed, null action | Trigger/Queue/write | no exception of Fluorine's own anywhere (s1.8) | changed 2026-10-21; not yet checked |
| s1.8-02 | Exceptions from Schedule pass through unwrapped | N | TS(S) whose `Schedule(Action)` throws P | `r.Trigger()`, `reg.Value = x` for a register r reads, `TS.Queue(a)` | caller receives P itself (same instance, not a `ReactiveException`, no wrapping); nothing logged. A user `Schedule` override is user code, so it also passes through unwrapped (s1.8, cs7.2 @ d716df1) | changed 2026-10-25; not yet checked |
| s1.8-03 | Refused run is silent | N | S shut down; reactor R captured earlier, reaction r of R | `r.Trigger()`, `S.Queue(a)`, `R.Queue(a)` | no exception; nothing logged; nothing runs (s1.8, s11.15) | not yet checked |
| s1.9-01 | Tracking per thread | P | reaction on S1 synchronously invokes code on S2/plain thread reading reg (waits for it) | write reg | reaction on S1 does not rerun | not yet checked |
| **s2 Normal flow** |||||||
| s2-01 | End-to-end full-name example | P | spec §2 steps 1–9 | follow steps | label values and runs as described in each step | not yet checked |
| **s3 Equality** |||||||
| s3.1b-01 | Racing writers lose nothing | E stress | r reads reg; 4 threads write distinct values, last writer known by join | Barrier after joins | last run saw the final stored value | not yet checked |
| s3.1b-02 | Equality not under register lock | E | Equals of T calls `reg.RecordRead`/`TriggerReactions` on same register from another thread and waits | write | no deadlock | not yet checked |
| **s4 Run guarantee** |||||||
| s4.1-01 | Runs after last modification | P stress | random writes from threads | Barrier2 | last run saw final value | not yet checked |
| s4.4-01 | Trigger during run queues one more | E | lambda blocks on gate during run 2 | trigger 5× during it; open; Barrier2 | exactly one more run | not yet checked |
| s4.6-01 | Manual trigger queued like others | P | S gated, r triggered by write | `r.Trigger()` | still one run (coalesced) | not yet checked |
| **s5 Consistency** |||||||
| s5.4-01 | Derived value correct after its turn | P | r writes `sum = a+b` | write a,b on S; Barrier2 | sum correct | not yet checked |
| **s6 Self-writes** |||||||
| s6.1-01 | Self-write retriggers | P | lambda: `if (c.Value < 10) c.Value++` | Barrier ×12 | c == 10; 11 runs | not yet checked |
| s6.2-01 | Endless self-trigger does not starve other work | E | lambda `c.Value++` unbounded | Queue action | action runs within 1 s; Shutdown completes | not yet checked |
| s6.3-01 | Writes inside pause still trigger | P | r2 reads reg; r1 writes reg inside Pause | – | r2 reruns | not yet checked |
| s6.4-01 | Reaction creating reactions | P | r1 adds r2 to R | Barrier2; dispose R | r2 ran; disposing R stops r1 and r2; disposing r1 alone does not dispose r2 | not yet checked |
| **s7 Protocol** |||||||
| s7.2-01 | Triggered reaction sees new value | P stress | write from other thread | – | run sees new or newer value, never older | not yet checked |
| s7.3a-01 | Normal + paused read keeps subscription | P | lambda reads reg normally and paused | write | reruns | not yet checked |
| s7.3a-02 | Only-paused read drops old subscription | P | run 1 reads reg normally, run 2 only paused | after run 2 write reg | no rerun | not yet checked |
| s7.4-01 | Never re-entrant on itself | E | lambda triggers itself and writes its own reg | – | no nested execution (depth counter ≤1) | not yet checked |
| s7.5-01 | Trigger doesn't subscribe | P | r1 calls `sig.TriggerReactions()` / `r2.Trigger()` | then trigger sig | r1 not rerun | not yet checked |
| **s8 Exceptions** |||||||
| s8.2-01 | Throwing reaction does not stop loop | N | r throws every run | trigger 10×; queue a | a runs; 10 logs | not yet checked |
| **s11 Scheduling** |||||||
| s11.1-01 | Reaction stays on its thread | P | – | 100 runs triggered from various threads | all on T(S) | not yet checked |
| s11.5-01 | Same-loop start-up ordering | P | in action on S: Add r reading reg; then `reg.Value = 5` | Barrier | r's first run saw 5; no run happened before the action ended | not yet checked |
| s11.7-01 | No concurrent runs on one loop | E stress | 10 reactions on S share in-flight counter | trigger all from threads | counter never >1 | not yet checked |
| s11.9-01 | FIFO order | P | S gated | trigger r1, r2, r3 | run order r1, r2, r3 | not yet checked |
| s11.9-02 | Coalesced position stays at first trigger | E | S gated | trigger r1, r2, r1 | order r1, r2 (r1 once) | not yet checked |
| s11.10-01 | Own writes visible immediately | P | lambda writes then reads reg | – | reads written value | not yet checked |
| s11.11-01 | Same-loop multi-writes seen together | P | r reads a,b on S; action on S writes a then b | – | r never observes new a with old b | not yet checked |
| s11.15-01 | Disposed reactors' reactions never run again | P | covered with cs3.8-02 | – | – | not yet checked |
| **s12 Lifetimes** |||||||
| s12.6b-01 | Later trigger after dispose no effect | N | r disposed | write regs; `r.Trigger()` | no run | not yet checked |
| s12.9-01 | Null actions not errors | E | `Reaction.Pause((Action)null)`, `R.Queue(null)`, `S.Queue((Action)null)` | – | no throw, nothing logged | not yet checked |
| **s13 Synchronisation** |||||||
| s13.3-01 | Queued at most once under concurrency | E stress | S gated; 16 threads trigger r 1000× | open; Barrier | exactly 1 run | not yet checked |
| s13.5-01 | No deadlock between register and queue | E stress | lambdas write registers read by reactions on same and other schedulers while threads trigger | 30 s | no deadlock (watchdog) | not yet checked |
| s13.7-01 | No lambda under lock | E | lambda on S1 blocks until another thread writes/subscribes the same registers and queues to S1 | – | completes; no deadlock | not yet checked |
| s13.8-01 | Writers never wait for runs | E | reaction blocks 2 s on S | write its register from test thread | write returns within 100 ms | not yet checked |
| **s14 Discussions** |||||||
| s14.1-01 | Shadow register pattern | P | value-less sig next to a plain field; accessor reads sig then field; setter changes field then triggers sig | reaction reads via accessor; set | reaction reruns, sees new field value | not yet checked |
| s14.7-01 | Self-requeue goes to end of queue | E | S gated; r1 writes a register it reads (bounded to 3 self-triggers); r2, r3 triggered while r1 waits | open gate; Barrier2 | order r1, r2, r3, r1, …: each self-requeued r1 run comes after everything queued before its run started (s14.7, s1.4, s11.9) | not yet checked |
| **s15 Logging** |||||||
| s15.2-01 | Usage errors thrown, not logged | N | Log attached; Reactor() on no-scheduler thread; ContinueRecording at zero in a reaction | catch | ReactiveException caught each time; Log received nothing (s15.2, s15.5) | changed 2026-10-25; not yet checked |
| s15.1-01 | Loop work-item escape goes to UnhandledException | N | Log attached; no handler variant with console captured | reaction throws X | X at Log once; loop continues; without handler: console warning, process not ended (s15.1, cs7.1, Werner 2026-10-25) | new 2026-10-25; not yet checked |
