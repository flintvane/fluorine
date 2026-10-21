# Spec gaps found while deriving tests

Checked against semantics-cs.md draft 0.5 and semantics.md (header 0.9, edited 2026-10-19). All the changes Spec Clerk listed are present in the files. For each gap: the section, the quoted text, the problem, and the affected tests. Werner decides; tests stay neutral until then.

## Conflicts between semantics.md and semantics-cs.md

**G1. Reaction pinning (s1.2 vs s11.2, s12.4, cs2.3).**
s1.2: "A reaction is pinned to the scheduler of the thread that created it." s11.2 and cs2.3 say an explicit scheduler can be given, and the reaction runs on its reactor's scheduler. These disagree when a reactor is created with `Reactor(S2)` on thread T1. Tests follow the reactor (cs2.3-01). s1.2 needs rewording.

**G2. Scheduler capabilities (s11.3 vs cs3).**
s11.3: "A scheduler can queue work, tell whether the current thread is its own, and report that it is shutting down." semantics-cs.md names no members for the last two, so they have no test.

**G3. Header version.** semantics.md still says "DRAFT 0.9 … (2026-10-18)" although it has been edited since. That bump is still pending.

## Contradictions or ambiguities inside semantics-cs.md

**G4. Dispose during shutdown cannot detach (cs2.17 vs cs3.8, s11.15).**
cs2.17: "`Reaction.Dispose()` sets the lambda to null and queues the reaction as a normal run." cs3.2: "After the shutdown has begun, the run is not queued." cs3.8 disposes the reactors after the drain, so "that their reactions are disconnected". The detach run would be refused, so cs2.17 says the registers are never detached. This conflicts with the outcome in 3.8. Test cs3.8-02/04 only checks behaviour (no run). Registers keeping strong references (s12.1) cannot be checked through the public API.

**G5. Shutdown from inside a queued action on the scheduler thread (cs3.8).**
"Called from inside a reaction … throws. Called on the thread of the scheduler itself, outside a reaction, it processes what is still open." A queued action is outside a reaction but inside the loop. Should the call drain re-entrantly (a nested pump, which s10.3 forbids in spirit), deadlock, or throw? This is unspecified, so cs3.8-09 uses a dispatcher message rather than a queued action.

**G6. `Run()` vs CreateNew (cs3.7 vs cs4.2, cs5.2).**
cs3.7: "A scheduler that owns its loop offers its own `Run()`." cs4.2 has CreateNew run the loop automatically, and cs4.1 says ThreadScheduler has "the same members as DispatcherScheduler". Does ThreadScheduler have `Run()`? What does it do after CreateNew? Not tested.

**G7. Exception from CreateNew's initial action (cs4.2, cs5.2).**
"logged and rethrown to the caller". Is the caller given the original exception, or a ReactiveException wrapper (cs7.2 says every *logged* error is a ReactiveException)? Is the stack trace kept? cs4.2-04 accepts either.

**G8. ContinueRecording outside a reaction (cs2.20 vs cs2.21).**
cs2.20: at count zero it "logs a programming error". cs2.21: outside a reaction "there is no pause count, so … no effect". Is the outside case logged? cs2.21-01 does not assert either way.

**G9. Reaction.Name settability (cs2.16 vs cs1.9, cs2.5).**
Register.Name and Reactor.Name are "settable property". Reaction.Name "is the name given to `Add`". Is it read-only? Is it settable after creation?

**G10. Diagnostics mode undefined (cs1.10, cs2.16, cs7.5).**
"The name need not be retained when the library does not run in diagnostics mode" and "up to the implementation". Name read-back can only be tested if the build documents its switch (cs1.9-02, cs2.7-02, cs2.16-01).

**G11. ExecutionCount optional (cs7.4).** "may compile it only into debug builds". The test is conditional and other tests do not rely on it.

**G12. Console warning (cs7.1).** No stream (Out/Error/Trace) and no content is given. The tests only check that something is written to the console and that it mentions both exceptions.

**G13. The CreateNew-throws case and the WinForms-attach-without-context case are logged (cs4.2, cs5.2, cs6.1) but are missing from the 7.3 list.** This is a minor inconsistency that should be listed in 7.3.

**G14. Reactor() after shutdown on the scheduler's own thread (cs2.2 vs cs2.4, cs3.6).**
After shutdown `Scheduler.Current` is null, so `new Reactor()` there throws (cs2.2), while `new Reactor(S)` silently returns a disposed reactor (cs2.4). Is that intended? It may surprise users.

**G15. Second scheduler type: which call, and what about ThreadScheduler (cs3.13).**
"Attaching or creating a scheduler on a thread that already has a scheduler of another type". CreateNew always makes a new thread, so only AttachToCurrent can hit this. Can `DispatcherScheduler.AttachToCurrent()` on a ThreadScheduler thread (in its initial action) be reached? It is not specified. The second Shutdown call from inside a reaction while the shutdown is in progress also has no rule.

**G16. Reactive.Shutdown from inside a reaction (cs3.11).** Does it throw before informing any scheduler, or after partially informing them? cs3.11-03 only checks that it throws and logs.

**G17. Reactive has no observable scheduler list (cs3.9, cs3.11, cs5.2).** "the scheduler does not exist afterwards" and "removed from that list" can only be observed indirectly, through ForThread and through Reactive.Shutdown not hanging.

**G18. Nested-pump detection on WinForms/ThreadScheduler (cs3.15, cs3.16).** Detection happens only "because a second reaction starts" (s14.6). ThreadScheduler has no public nested pump, so cs3.16 can only be tested through a user subclass calling the protected RunReaction.

## Untestable or only statistically testable rules

- s3.1b, s7.6, s13.1–13.8, cs1.18: race freedom, barriers, lock ordering. These have stress tests only, and passing them does not prove the rule.
- s4.1 "at least once": the upper bound comes only from s4.4.
- s11.6: no ordering guarantee, by design. There is nothing to assert.
- s11.9a / s11.7a liveness, "never starve": tested with a 1 s heuristic. No bound is specified.
- s5.1, s5.2, s5.5, s11.12, s11.13: permissions (glitches allowed), not obligations.
- s12.1 strong references: this is internal and has no public observable.
- cs1.10 and cs2.16 "need not retain": optional behaviour.
- cs3.10 "not light-weight": advisory.
- cs8.1 and s10.2: a discussion, not normative (ideas.md parks async). Its tests are informative only.
- s10.3 "a reaction never runs a message pump": this is an obligation on the user, and only detection (s10.4) is testable.
