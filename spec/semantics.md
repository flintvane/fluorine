# Fluorine: Semantic Framework (DRAFT 0.9)

Author: Werner Van Belle, with help from the latest xAI LLM models.

Status: working draft 0.9 (2026-10-18), not published.
Scope: platform-neutral rules every Fluorine implementation must follow. Language-specific mappings (memory model, scheduler, member names, naming of the pause construct, how errors are logged) are left to each platform. The rules declare outcomes; they do not prescribe implementation mechanisms.

## Purpose

Fluorine is a reactive programming library, to be provided in the same form on a variety of platforms. All implementations share one basic idea and one set of semantics.

**The idea.** Program state lives in registers. Code that needs to stay in step with that state, such as updating a screen, a view model or an output, is written as a reaction. A reaction does not declare what it depends on. Whatever registers it reads while it runs become its dependencies, and when any of them changes, the reaction runs again. The execution is performed asynchronously, on the event loop of the reaction, after the current execution. The dependencies are rediscovered on every run, so a branch that is no longer taken stops causing runs.

**What it is for.** Keeping derived or dependent state consistent with changing state, without hand-written event wiring, subscription bookkeeping or manual refreshes, in programs built around event loops, such as desktop UIs.

**Goals.**
- A small, precise and platform-neutral set of rules that behaves the same on every platform.
- Predictable asynchronous behaviour: changes are never lost, and reactions run on their own loop, after the current execution.
- Deterministic lifetimes through explicit ownership.
- Works across several event loops and threads without deadlocks.

**Origin.** Fluorine is based on an idea from Autograd, experience with asynchronous event loops, and Petri nets to represent state diagrams. A Java version existed for BpmDj, and a specialized WPF version was made for Glatt.

## 0. Vocabulary

- **Register**: remembers which reactions have read its content. It typically holds a value of a specific type. When only signalling is needed it holds no value at all (12.8).
- **Reaction**: a unit of code that reads registers. The code it contains is called its **lambda**. A reaction runs once at creation, and again after any register it read has been modified.
- **Reactor**: a group of reactions that are disposed together. It owns its reactions and creates them. A reactor is associated with exactly one scheduler (12.4).
- **Owner**: whoever holds a reactor and is responsible for disposing it (section 12).
- **Run**: one execution of a reaction's lambda.
- **Pause**: a scope inside which reads do not subscribe.

## 1. Assumed threading model

1.1 The model assumes platforms with one or more **event loops**: a thread that processes queued work items one at a time (a UI thread, an event loop thread, or a dedicated loop thread). Each loop is a **scheduler**.
1.2 A reaction is **pinned** to the scheduler of the thread that created it. All of its runs happen on that scheduler, one at a time, never in parallel with themselves.
1.3 Registers are **not** pinned. Any thread may read or write a register. This is why the ordering rules in section 7 matter.
1.4 Triggering a reaction does not run it. It **queues** a run to the reaction's scheduler. A reaction that is already waiting for a run is not queued again until that run has started (coalescing).
1.5 Different reactions on different schedulers run in parallel. Reactions on the same scheduler are interleaved with other work of that loop, one run at a time.
1.6 A run is synchronous: it starts, performs the assigned work, during which it can read and write registers at any moment, and then it ends, all on a plain call stack (see section 10).
1.7 Details of scheduling, ordering, liveness, shutdown and nested pumps are in section 11.
1.8 **Triggering or queueing never throws an exception.** Callers would otherwise have to handle that possibility everywhere, which is not the point of the framework. A refused run (for example because a loop is shutting down) is silent.
1.9 **Tracking is per thread.** The reaction to which reads are attributed is the reaction running on the reading thread (the current reaction), provided it is not paused. Code that runs on another thread (for example through an invoke on another event loop) never subscribes the calling reaction. If that other thread is itself running a reaction, its reads belong to that reaction.

## 2. A normal control flow

Below we explain the concept in pseudocode, assuming that explicit classes for `Register`, `Reactor` and `Reaction` exist. This might of course differ between implementations.

Example: a label on screen shows a person's full name. `first` and `last` are registers. The UI thread runs an event loop.

1. **Creation of the registers.** On the UI thread, code creates the registers and gives them a value: `first.Value = "Ada"` and `last.Value = "Lovelace"`.
2. **Creation of the reactor and the reaction.** On the same thread, code creates a reactor, which captures the scheduler of the UI loop, and adds a reaction to it:
   `Reaction r = reactor.Add(() => label.Text = first.Value == "" ? "Must have a first name" : first.Value + " " + last.Value);`
   Adding the reaction queues its first run to the UI loop.
3. **First run.** The loop picks up the run. The reaction reads `first`: it is subscribed to `first` before the value is returned. Because `first` is not empty, it also reads `last`, with the same effect. The label shows "Ada Lovelace". At the end of the run nothing is stale, so both subscriptions stay.
4. **A write from another thread.** A worker thread sets `last.Value = "Byron"`. The value differs, so the new value is stored, and then the reaction is triggered. The trigger queues a run to the UI loop and returns immediately. The worker never waits for the UI.
5. **Coalescing.** Before the UI loop gets to it, the worker also sets `first.Value = "Augusta"`. The reaction already has a run waiting, so nothing more is queued.
6. **Second run.** The loop now runs the reaction once. It reads the current values of both registers and shows "Augusta Byron". It skipped an intermediate state (such as "Ada Byron"), and that is allowed (section 4).
7. **No change.** The worker sets `first.Value = "Augusta"` again. The value is the same as before, so nothing happens: no store of a new state, no trigger.
8. **Changing dependencies.** The worker sets `first.Value = ""`. In the next run the reaction reads only `first`, because the lambda reads `last` only when `first` is not empty. The label shows "Must have a first name". After that run `last` is still marked stale, so the reaction is unsubscribed from it, and writes to `last` no longer trigger anything. As soon as `first` is written with a non-empty value, the reaction runs again and reads `last` again.
9. **Disposal.** When the owner is done, it disposes the reaction or its reactor. No further runs happen, and the subscriptions to the registers it accessed are removed.

## 3. Equality

3.1 A write changes a register only if the new value is not equal to the current one, according to the platform's natural (default) equality for the value: `object.Equals(value, stored)` in C#, `equals` in Java, and so on. An implementation may choose another default and may offer an equality per register; the core spec does not provide one.
3.1a Before its first write a register holds the platform's natural default value (`default(T)` in C#, null for references, zero for numbers), or it is initialized immediately with a value of choice.
3.1b The equality check is user code and never runs inside a register's critical section. A write compares first, outside any lock; if the values differ it stores the new value (no compare-and-swap, no re-check), and only then triggers (7.2). The trigger's lock provides the memory barrier, so no reaction can miss the new value. Racing writers can cause at most an extra run, or a write dropped because it compared equal to a stale value, which is equivalent to that write having happened just before the other. Nothing is lost.
3.1c If the equality check throws, the exception reaches the caller of the write. Nothing is stored and nothing is triggered. This is a programming error, and it is not covered for.
3.2 When a write changes nothing, no reaction is triggered.
3.3 The user is responsible for a sound equality. Reference equality is too weak in practice, because object graphs are never trees.
3.4 Equality may differ per language. What is required is that "no change" means "no signal".

## 4. Run guarantee

4.1 After the last modification of a register, every reaction that read it will run at least once afterwards, as long as its event loop is not shutting down.
4.2 Intermediate values may be skipped. Runs triggered by several writes may be coalesced into one.
4.3 There is no "see every value" mode. What is guaranteed is a reaction to the last state: a reaction that read a register runs at least once after the last modification of that register (4.1), so it ends up having seen the final values.
4.4 A reaction is queued at most once. Any number of triggers before its run starts lead to exactly one run. A trigger that arrives while the run is in progress queues one further run.
4.5 A register can be triggered manually. This acts as if its value had changed: all reactions connected to it are triggered, regardless of equality, and its value is not changed. The purpose of the manual trigger is shadow registers (see section 14).
4.6 A reaction can be triggered manually, which queues it like any other trigger.

## 5. Consistency and derived values

5.1 Fluorine is glitchable. A reaction may observe registers from different moments. Only each register on its own is consistent.
5.2 Reaction code must tolerate inconsistent combinations of values. This is a deliberate pressure towards robust programs.
5.3 State that must be consistent as a whole is modelled explicitly: put it in one aggregate value held by a single register and replace it as a whole.
5.4 A derived value is a reaction that writes a register. It is correct only after its scheduler turn. Asynchrony is embraced.
5.5 Glitches can only be observed across thread boundaries (see 11.10).

## 6. Self-writes and cycles

6.1 A reaction that writes a register it also reads triggers itself. This is intended.
6.2 There is no cycle detection. A reaction can trigger itself endlessly, or if multiple reactions interact they might form an execution loop if no fixpoint exists in the state. Programs rely on endless re-triggering to follow a state. This is safe only because the scheduler gives other work its turn (11.9a).
6.3 Pausing applies to reads only. Triggering cannot be paused.
6.4 A reaction may create reactions while it runs. They are ordinary reactions and are not treated differently: no implicit parent-child ownership, no automatic disposal, no re-creation per run. The reactor they are added to owns them (12.3). **Warning:** every created reaction is queued. If a reaction creates reactions on every run, and nothing reuses the earlier ones, new reactions are created and triggered forever (see 14.3).

## 7. Subscription and trigger protocol

7.1 **Read:** the reading reaction is subscribed before the value is returned. A write landing between subscribing and returning will therefore still trigger it.
7.2 **Write:** the new value is stored first, and only then are the subscribed reactions triggered. A triggered reaction that re-reads sees the new value or a newer one.
7.3 **Dependency maintenance per run:**
  1. At the start of a run, every register read in the previous run is marked as a deletion candidate.
  2. A read of a new register subscribes it at once. A read of an existing one removes its deletion mark.
  3. After the run, registers still marked are unsubscribed.
7.3a A read inside a pause (section 9) is not a read access. If the same register is read both normally and inside a pause during one run, the normal read subscribes and the paused read changes nothing. A register read only inside a pause has no tracked read in that run, so it is unsubscribed at the end of the run like any other unread dependency.
7.4 A reaction never runs re-entrantly on itself.
7.5 Triggering never reads or subscribes anything.
7.6 Memory visibility (multithreaded): a value that has been written must be visible to another thread by the time that thread's reaction runs. Where the platform's own locks already imply the needed barriers (for example C#), nothing more is required. Platforms with weaker models (C, C++, Rust atomics, Java without synchronisation) must add the barriers themselves. The subscription must also be visible to writers before the value is read (7.1).

## 8. Exceptions

8.1 An exception thrown by a reaction ends the run. It is captured, logged and otherwise ignored, because there is no stack frame to propagate it on. The reaction stays connected to every register it read up to the exception. Retriggering happens only through the normal mechanism: a register that it read changes, or a manual trigger. That also means that if the exception is thrown before any read happened, the reaction will not rerun without a manual trigger.
8.2 A reaction must never stop or crash the event loop.
8.3 Every exception that reaches the event loop is logged as a programming error. It never stops the loop.
8.4 Where "logged" goes is left to the platform. Triggering and queueing never throw (1.8).

## 9. Pause (untracked reads)

9.1 Every platform provides a way to run code in which reads do not subscribe (called pausing the reaction/recording). It exists in whichever shapes suit the language: a scoped block, a function wrapper, or a function returning a value. It is needed, for example, when raising UI events from inside a reaction triggers layout that reads many registers and would subscribe the reaction to all of them.
9.2 Pause scopes are reentrant.
9.2a A paused read does not subscribe and does not keep an existing subscription alive (7.3a).
9.3 The pause counter belongs to the running reaction. If a reaction finishes with an unbalanced pause counter, the counter ends with it, so that a reaction never ends up recording nothing any more.

## 10. Execution model and async

10.1 A reaction is a plain synchronous run on a simple call stack, in a multithreaded control flow, with one entry and one exit.
10.2 Constructs that do not fit this model, such as async/await, re-running parts of a method through a language state machine, and jump-style control flow (such as `longjmp`), are discussed in the specification for each individual platform. A reaction's lambda is a synchronous call. Asynchronous work started inside it is ordinary threading and neither extends the run nor adds subscriptions.
10.3 Nested message pumps are prohibited. A reaction never runs a message pump or any other nested event loop, such as a modal dialog, `DoEvents` or a nested dispatcher frame (see 14.6 for the reason).
10.4 At most one reaction runs per thread (11.8). If a second reaction wants to start on a thread while one is running, a nested pump was run inside the running reaction, which is a programming error. It is logged (15.2), the second reaction is not started, and it is queued again once the running reaction has finished. An exception is thrown to the caller of the run.

## 11. Scheduling and threading

**Affinity**
11.1 A reaction stays on the thread (scheduler) it started on, for its whole life. It never moves.
11.2 By default a reactor uses the scheduler of the creating thread. That scheduler is captured when the reactor is created. An explicit scheduler may be given to use another loop. A reaction runs on the scheduler of its reactor (12.4).
11.3 Every platform must supply the necessary schedulers. A scheduler can queue work, tell whether the current thread is its own, and report that it is shutting down. The user can also queue arbitrary work of their own on a scheduler. Such work is not a reaction: it records no reads, and an exception from it is logged as in 8.3. Platforms without a single loop must provide their own. How a thread finds its scheduler, and the types and members involved, are left to the platform.
11.4 Schedulers are created explicitly, never implicitly: if no scheduler was set up in advance, reactors, and therefore reactions, cannot be created. Creating a reactor on a thread without a scheduler, and without being given one, is a programming error. It is first logged (15.2), and then an exception is thrown to the creator.

**Start-up ordering**
11.5 Same loop: when a reaction is created on the creator's own loop, the creator's current run finishes before the new reaction's first run starts. Initialisation can therefore be written safely: create all reactions, write the variables, and nothing runs in between.
11.6 Other loop: the new reaction may already be running before the creator's run has finished. There is no ordering guarantee, by design. In multithreaded code the programmer must be aware of this.

**Execution**
11.7 Two reactions on the same event loop never run concurrently. Each run completes before the next starts.
11.7a Schedulers work together and never interfere with each other. A scheduler does not block, delay or starve another scheduler, and the work of one loop never depends on another loop being free.
11.8 No two reactions ever run inside each other. While one reaction is running on a loop, no other reaction on that loop starts.
11.9 Queued reactions run first come, first served. Because run requests coalesce (4.4), the actual position of a reaction in the queue can differ from the position its latest trigger would suggest. There are no priorities.
11.9a Liveness: no triggered reaction is parked indefinitely. Every reaction waiting for a run will eventually get it. A scheduler must also not let reactions starve the other work of its loop (for example UI messages).

**Visibility of writes**
11.10 Writes take effect immediately and are never delayed. A reaction sees its own writes at once. There is no transactional isolation and there are no local copies.
11.11 Writes to several registers made on one event loop are all seen together by reactions on that loop, because none can run in between.
11.12 There is no such guarantee across thread boundaries. A reaction on another thread may see some of the writes and not others.
11.13 There is no atomic multi-register write. Explicit atomicity is expressed with an aggregate value in one register (5.3).

**Nested message pumps**
11.14 Nested message pumps are prohibited (10.3).

**Shutdown**
11.15 When a loop is shut down, first all reactions already queued on the loop are run. Reactions queued after the shutdown began, including those queued by the reactions that are being drained, are no longer queued. The same holds for work the user queued (11.3): work already queued runs, and work that arrives after the shutdown began is ignored. Then all reactors associated with the loop are disposed. Disposing a reactor disconnects its reactions from their registers, and they never run again. This does not depend on a reaction being triggered, because a reaction that only waits on a register is never triggered. No new work is accepted. A refused run, including the first run of a newly created reaction, is silent and never throws (1.8), and a reaction whose run was refused simply never runs. How the loop tracks its reactions is up to the implementation.
11.16 A shutdown is synchronous: the call returns only when all queued reactions have run and all reactors of the scheduler have been disposed (11.15). A shutdown can be requested from any thread. It cannot be requested from inside a reaction, because that reaction would have to wait for itself. This is a programming error, which is logged (15.2) and then an exception is thrown to the caller. When the shutdown is requested on the thread of the scheduler itself, outside a reaction, that call processes what is still open before it returns.
11.17 The platform offers one call that shuts down every scheduler it knows. It first tells all schedulers to finish, without waiting for one to complete before informing the next, and then waits until every one of them has reached the finished state. It returns when all of them are done. The rules of 11.16 apply to it.

## 12. Lifetimes and ownership

12.1 Registers hold strong references to their reactions. Reactions hold strong references to their registers.
12.2 Every reaction is created by, and belongs to, exactly one reactor. A reaction cannot exist without a reactor. The owner of the reactor is responsible for disposing it. A single reaction can also be disposed individually, which removes it from the reactor.
12.3 A reactor groups reactions, and disposing the reactor disposes all of them.
12.4 A reactor is associated with exactly one scheduler. All reactions in a reactor belong to that scheduler. A reactor offers the same queueing interface as a scheduler, so a reaction has no scheduler of its own and runs on the scheduler of its reactor. Reactions that must run on different schedulers belong to different reactors. The user can queue arbitrary work of their own through a reactor as well, and it runs on the reactor's scheduler. Work queued through a disposed reactor is dropped silently.
12.5 Disposing a reaction removes all its subscriptions. Once disposal has completed, the reaction's lambda never executes again.
12.6 In detail: (a) a run that is already queued but has not started does not execute the originally assigned lambda; (b) a trigger that arrives later, from a register or a manual trigger (4.5, 4.6), has no effect; (c) a run that is already in progress is not interrupted and completes, but it does not cause a further run, and reads it makes after disposal create no subscriptions; (d) a reaction may dispose itself or be disposed from another thread, and the rules (a) to (c) hold in both cases; (e) disposing twice is harmless.
12.7 Disposing a reactor applies 12.5 to every reaction it holds.
12.8 A register does not have to hold a value. A register without a value is a pure signal: it can be read, which subscribes the reaction, and it can be triggered manually, which triggers every connected reaction. It has no equality. Such a register can sit next to an existing variable (see 14.1).
12.9 A null action is treated as no action, and not an error.
12.10 The lambda assigned to a reaction can never be changed by the user. It is fixed at creation.
12.11 It is not expected that the lambda passed to a reaction is executed solely as that reaction. The same code may also be called as an ordinary call, outside any reaction, in which case nothing is recorded.
12.12 Adding a reaction to a reactor that has been disposed creates nothing and yields no reaction. It is not an error, because a loop may dispose all the reactors it knows at shutdown, and an add may coincide with that cleanup.

## 13. Synchronisation and lock ordering

**Shared state**
- R1. A register's value (written and read from any thread).
- R2. A register's listener set (changed by subscribing during a read, and by unsubscribing after a run or on disposal).
- R3. A reaction's set of registers read in its previous run.
- R4. A reaction's "queued" state.
- R5. A scheduler's work queue (queued to from any thread).

**Rules**
13.1 Atomic values: reading and writing a register's value must not tear. Where the platform replaces only a reference this is automatic. Where the value type is larger than what the platform can replace atomically (for example a large struct), the implementation must make the replacement atomic. A platform may treat value types differently from references for this, for example by performing the assignment under a lock.
13.2 Register-local atomicity: for one register, subscribe, unsubscribe and trigger never overlap each other. Every register has its own critical section.
13.3 Queued state: a reaction's queued state is an atomic test-and-set. This is what makes "queued at most once" (4.4) hold when several threads trigger the same reaction.
13.4 The read set (R3) belongs to the reaction and is only accessed by its scheduler.
13.5 **Lock ordering.** A trigger takes the register's critical section and, while inside it, queues the reaction to a scheduler's queue. This order is fixed: register, then queue. The reverse is forbidden. A scheduler queue must never acquire a register's critical section while it holds its own lock. In particular, queueing and dequeuing must not call into registers while the queue is locked.
13.6 The same rule covers several loops. Triggering a reaction that lives on another loop queues to that loop's queue in the same order, so loops interacting with each other cannot form a cycle.
13.7 No lambda runs under a lock. A reaction's run starts only after it has been taken off the queue.
13.8 Writers never wait for a reaction to run. A trigger only queues work. At most it briefly waits for the locks of the register and of the queue, which are never held while a lambda runs (13.7).

## 14. Discussions

14.1 **Shadow registers are the purpose of the manual trigger.** A control such as a text box holds the data itself. Next to it sits a register without a value (12.8). The control's change event (for example text changed) triggers that register. The read accessor first performs an explicit read of that register, which subscribes the reaction, and then returns the value from the control. Anyone reading the value through the accessor is therefore notified when the control changes.
14.2 **Not a way to publish in-place changes.** The manual trigger is not meant to tell a parent about a change inside a sub-object. Objects should provide proper registers. A sub-object that uses its own registers needs no manual trigger.
14.3 **Creating reactions inside a reaction.** The typical case is a reaction that creates an object with its own reactor and registers and then reads from that object. Every run creates a new object, whose reactions are queued and triggered, and the reading subscribes the creating reaction to the new object's registers. Unless the previously created object is cached and reused, the program ends in an endless loop.

14.4 **Registers with and without a value.** The natural set-up is a base register without a value, which only remembers its readers and can be triggered, and a register of type T that derives from it and adds the value and the equality. The rules in 12.8 do not name two register kinds, so an implementation is free to choose this structure.

14.5 **Null as a signal for disposal.** Because the lambda of a reaction cannot be changed (12.10), setting it to null is a potential signal for disposal. Disposing a reaction can set the lambda to null and then queue the reaction as a normal run. A run that was already queued finds no lambda and does nothing. The run with the null lambda reads nothing, so the dependency maintenance of 7.3 detaches the reaction from all its registers by itself. A separate "disposed" mark and a separate disconnect step are then not needed.

14.6 **Why nested pumps are prohibited.** A nested pump runs other code on the same stack in the middle of a reaction: event handlers, other reactions, and queued work. Three things then no longer hold. The writes made by the reaction can no longer be processed as one, because other code runs between them. Reads made by that other code are attributed to the running reaction. And the reaction is not a simple call with one entry and one exit (10.1). Detecting every nested pump was investigated, with hooks on the message loop, a synchronisation context, and a depth counter that every pump has to increment. None of them catches all pumps, and the counter only works if the programmer remembers to mark each pump. Prohibiting them is the only rule that can be stated without exceptions. The check in 10.4 reports the cases that become visible because a second reaction starts.

## 15. Logging and monitoring

15.1 All unhandled exceptions from reactions must be logged. An exception that reaches the event loop is logged as a programming error. Where the log goes is left to the platform.
15.2 Creating a reactor without a scheduler (11.4), requesting a shutdown from inside a reaction (11.16), and starting a reaction while another one is running (10.4), must be logged as programming errors, before the exception is thrown.
15.3 An implementation may provide an execution counter per reaction (for example in debug mode). Its only purpose is to show how often something ran. It never stops anything.
15.4 Reactions and registers may be named, for logs and diagnostics.
15.5 Ending a pause more often than it was started is logged as a programming error (9.3).
