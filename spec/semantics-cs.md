# Fluorine: C# Realisation (DRAFT 0.5)

Author: Werner Van Belle, with help from the latest xAI LLM models.

Status: draft (2026-10-19). This document refines `semantics.md` for C# / .NET. Rule numbers in parentheses, such as (3.1b), refer to `semantics.md`. Everything in `semantics.md` applies; this document only adds detail and stricter rules.

The target framework is .NET Framework 4.8.

## 1. Registers

**Types**
The classes below are the standard realisation of a register. Other storage models are allowed, for example a register that is a struct holding an index into a large matrix, as long as they obey the same rules: reading records the read, a change triggers the connected reactions, values do not tear, and every register has its own critical section.

1.1 `Register` is the base class for a register without a value. It remembers which reactions have read it (12.8).
1.2 `Register<T>` derives from `Register`. It adds the typed value and the equality.

**Reading and triggering**
1.3 `Register.RecordRead()` subscribes `Reaction.Current` (2.18) to the register. If `Reaction.IsRecording` is false, it does nothing.
1.4 `Register.TriggerReactions()` triggers every reaction connected to the register. It can also be called by the user.
1.5 `RecordRead()` and `TriggerReactions()` can both be called from any thread (13.2).

**Register<T>**
1.6 `Register<T>` has an empty constructor. It initializes the backing value with `default(T)` (3.1a).
1.7 `Register<T>` has a constructor that takes an initial value. The value is stored without triggering anything.
1.8 `Value` is a property. The getter calls `RecordRead()` and then returns the stored value. The setter performs a write as described in 3.1b.
1.9 `Register.Name` is a settable property, for logs and diagnostics (15.4). There is no constructor that takes a name.
1.10 The name need not be retained when the library does not run in diagnostics mode.
1.11 Registers can be inherited. `Value`, `RecordRead()` and `TriggerReactions()` are not virtual.
1.12 The backing value of the `Value` property is protected. It is only available to inheritors that want to specialize further.
1.13 The constructors, and the getter and setter of `Value`, can be called from any thread.
1.14 `bool SetValue(T newValue)` writes like the setter and returns true if the value was changed.
1.15 `bool SetValue(T newValue, out T oldValue)` does the same, and `oldValue` holds the value the register had before. It is set also when the method returns false. The method records no read: `oldValue` is an untracked read, intended for disposing or releasing the old value, not for control flow in a reaction.

**Equality**
1.16 Equality is `object.Equals(value, stored)` (3.1). `IEquatable<T>` alone does not count.
1.17 An exception thrown by the equality check reaches the caller of the write. Nothing is stored and nothing is triggered (3.1c).

**Atomic values**
1.18 A value of a value type is assigned without tearing (13.1). Where the type is larger than what the platform replaces atomically, the assignment can be performed under the same lock as the register has. For smaller types, a compare-and-swap can be used. Boxing every write is a valid but expensive alternative and is not recommended.

## 2. Reactors and reactions

**Reactors**
2.1 A reactor is associated with exactly one scheduler. It offers the same queueing interface as a scheduler (12.4). `Reactor.Queue(Action)` queues the action on the reactor's scheduler (3.3). On a disposed reactor it does nothing.
2.2 `Reactor()` creates a reactor on `Scheduler.Current`, captured at creation (11.2). When the calling thread has no scheduler, it logs a programming error and throws `ReactiveException` (11.4, 15.2).
2.3 `Reactor(Scheduler scheduler)` creates a reactor on the given scheduler. Passing null logs a programming error and throws `ReactiveException` (15.2).
2.4 A reactor created on a scheduler that has started its shutdown or has shut down is created without an error, and it behaves as a disposed reactor: `Add` creates nothing and returns null (2.9, 11.15).
2.5 `Reactor.Name` is a settable property, for logs and diagnostics. There is no constructor that takes a name.

**Creating reactions**
2.6 Reactions are only created through a reactor. `Reaction` has no public constructor.
2.7 `Reactor.Add(lambda)` and `Reactor.Add(name, lambda)` create a reaction, queue its first run on the scheduler of the reactor, and return the `Reaction`.
2.8 A reaction never runs inline, also not when the caller is on the scheduler thread.
2.9 `Add` on a disposed reactor creates nothing and returns null. It is not an error and is not logged (12.12).
2.10 A reaction always has a reactor (12.2). "The scheduler of a reaction" means the scheduler of the reactor the reaction belongs to.
2.11 Disposing the reactor disposes all its reactions (12.3).
2.12 Disposing a reaction removes it from its reactor (12.2).

**Lambda**
2.13 The lambda of a reaction is fixed at creation (12.10). A null lambda is treated as a lambda with an empty code block (12.9).
2.14 It is not expected that the lambda is executed solely as that reaction (12.11). The same code may be called as an ordinary call, in which case nothing is recorded.

**Reaction**
2.15 `Reaction.Trigger()` queues a run on the scheduler of the reaction and returns immediately (4.6). It never throws, and on a disposed reaction it does nothing (12.6).
2.16 `Reaction.Name` is the name given to `Add` (15.4). The name need not be retained when the library does not run in diagnostics mode.
2.17 `Reaction.Dispose()` sets the lambda to null and queues the reaction as a normal run. The run with the null lambda reads nothing, so dependency maintenance (7.3) detaches the reaction from all its registers (14.5). It can be called from any thread, and disposing twice is harmless (12.6).
2.18 `Reaction.Current` is a public static property. It returns the reaction that is running on this thread, whether it is paused or not, or null when none is running. Reads are attributed to it while `Reaction.IsRecording` is true (1.3).

**Pause**
2.19 `Reaction.PauseRecording()` increments the pause count of the running reaction on this thread. While the count is above zero, the reaction is paused and reads on that thread are not recorded (1.3, 9.1).
2.20 `Reaction.ContinueRecording()` decrements the count. Recording resumes when the count is zero again. Calling it while the count is zero logs a programming error (9.3, 15.5) and leaves the count at zero.
2.21 The pause count is logically associated with a reaction. Outside a running reaction there is no pause count, so `PauseRecording()` and `ContinueRecording()` have no effect there.
2.22 `Reaction.IsRecording` is a public static property. It returns true when a reaction is running on this thread and its pause count is zero, that is, when a read on this thread is recorded now. It returns false outside a reaction and while the pause count is above zero.
2.23 The count ends with the running reaction (9.3).
2.24 The count is wrapped in the following forms: `Reaction.Pause()` returns an `IDisposable` that continues recording when disposed; `Reaction.Pause(Action)` and `Reaction.Pause<T>(Func<T>)` pause, run the delegate and continue in a `finally` block. Pauses are reentrant (9.2).

## 3. Schedulers

3.1 `Scheduler` is an abstract class. Every scheduler, whether supplied by the library or written by a user, derives from it. Schedulers and reactors offer a common queueing interface (12.4).
3.2 `Scheduler.Queue(Reaction)` queues one run of the reaction and returns nothing. It never runs the reaction inline (13.5, 13.7) and never throws (1.8). After the shutdown has begun, the run is not queued, silently (11.15).
3.3 `Scheduler.Queue(Action)` queues an arbitrary action of the user, in the same order as the reaction runs. It returns nothing, never runs the action inline and never throws. Actions queued before the shutdown began still run; an action that arrives after the shutdown began is ignored, silently (11.15). The action is not a reaction: no reads are recorded. Every exception thrown by the action is logged (7.1).
3.4 The scheduler runs a queued reaction on its own thread by calling the protected method `Scheduler.RunReaction(Reaction)`. This is the only way for a scheduler to run a reaction. `Reaction.Run()` is internal.
3.5 `Reaction.Run()` runs the lambda, during which all reads are recorded. Afterwards, the reaction is disconnected from all registers that it no longer read (7.3). An exception from the lambda is handled as in 8.1.
3.6 The scheduler of a thread is a thread-static, `Scheduler.Current`. A scheduler installs itself on its own thread (11.3). When its shutdown has finished, it removes itself from that thread, so `Scheduler.Current` is null there afterwards. During the shutdown it stays installed, because the reactions that are drained still run on it.
3.7 Starting the event loop is done explicitly by the user. A scheduler that owns its loop offers its own `Run()`, and a Dispatcher-based scheduler defers to the Dispatcher. A scheduler that joins an existing loop has no `Run()`. The base class has no `Run()`.
3.8 `Scheduler.Shutdown()` returns when all queued reactions and all queued actions have run, and all reactors of the scheduler have been disposed (11.15, 11.16), so that their reactions are disconnected (2.11). It can be called from any thread. Called from inside a reaction, paused or not, it logs a programming error and throws `ReactiveException` (15.2). Called on the thread of the scheduler itself, outside a reaction, it processes what is still open before it returns. A second call to `Shutdown()` on the same scheduler starts nothing new and returns when the shutdown has finished.
3.9 The base class `Scheduler` announces every scheduler to the static class `Reactive`. The user does not register schedulers manually.
3.10 Schedulers are not light-weight. Their lifetime is intended to be the lifetime of the application, and one scheduler may serve many reactors.
3.11 The static class `Reactive` knows all schedulers. A scheduler whose shutdown has finished is removed from that list. `Reactive.Shutdown()` informs every scheduler that it should finish, without waiting between them, and then waits until all of them have reached the finished state (11.17). The restrictions of `Scheduler.Shutdown()` apply to it (3.8).
3.12 Schedulers work together and never interfere with each other (11.7a). Any reactor can be given any scheduler.
3.13 `Scheduler.ForThread(Thread)` returns the scheduler of that thread, or null when it has none or its scheduler has shut down. A scheduler that has shut down is no longer kept in the list of schedulers per thread. `Scheduler.Current` is the scheduler of the calling thread (3.6). Attaching or creating a scheduler on a thread that already has a scheduler of another type logs a programming error and throws a `ReactiveException` (15.2).
3.14 The scheduler of a thread is the same object for as long as the thread lives (3.13, 5.3, 6.1).

**Nesting**
3.15 Nested message pumps are prohibited on every scheduler. A reaction runs no message pump (10.3). At most one reaction runs per thread (10.4).
3.16 `Scheduler.RunReaction` on a thread where another reaction is running does not start the reaction, logs a programming error (10.4, 15.2), queues the reaction again once the running reaction has finished, and throws `ReactiveException` to its caller.

## 4. Thread scheduler

4.1 `ThreadScheduler` is the scheduler for a plain thread with its own event loop. It has no public constructor. It has the same members as `DispatcherScheduler` (section 5), except that it has no `AttachToCurrent()`: a plain thread has no event loop of its own to attach to.
4.2 `ThreadScheduler.CreateNew(Action initial = null)` behaves as `DispatcherScheduler.CreateNew` (5.2): it creates a new thread, installs the scheduler on it (3.6), runs the action there, returns the scheduler as soon as the action has finished, and runs the event loop of the scheduler on that thread afterwards. If the action throws, the exception is logged and rethrown to the caller, the thread ends and no scheduler exists.
4.3 `Queue` of a `ThreadScheduler` never blocks (3.2). Its `Shutdown()` follows 3.8, and the thread ends afterwards.

## 5. WPF Dispatcher scheduler

5.1 `DispatcherScheduler` is the scheduler for a WPF `Dispatcher`. It has no public constructor.
5.2 `DispatcherScheduler.CreateNew(Action initial = null)` creates a new thread, installs a scheduler on it (3.6), executes the action on that thread, and then runs the dispatcher on it. It returns the scheduler as soon as the action has finished, so that the initialisation of the thread and of its scheduler is complete when the caller continues. If the action throws, the exception is logged and rethrown to the caller of `CreateNew`. The new thread ends without running the dispatcher, and the scheduler does not exist afterwards.
5.3 `DispatcherScheduler.AttachToCurrent()` attaches a scheduler to the dispatcher of the calling thread. A thread has at most one scheduler: when one is already attached, that one is returned.
5.4 `Queue` of a `DispatcherScheduler` hands the run to the dispatcher without blocking. Its `Shutdown()` follows 3.8.

## 6. WinForms scheduler

6.1 `WinFormsScheduler` is the scheduler for the WinForms message loop of a thread. It has no public constructor, and `WinFormsScheduler.AttachToCurrent()` attaches it to the `SynchronizationContext` of the calling thread, which must be the WinForms context. A thread has at most one scheduler: when one is already attached, that one is returned. When the calling thread has no WinForms context, the call logs a programming error and throws `ReactiveException`.
6.2 A `WinFormsScheduler` uses the synchronisation context that WinForms has installed on the thread. A context passed in by the user is not supported, and a scheduler cannot be created for a context that is not the WinForms one.
6.3 `Queue` of a `WinFormsScheduler` never blocks (3.2). Its `Shutdown()` follows 3.8. It does not end the message loop, because the application owns that loop.

## 7. Logging and monitoring

7.1 "Logged" (15.1) means that the error is raised through `Reactive.UnhandledException`, a public static event of type `Action<Exception>`. When no handler is attached, a warning is written to the console. When a handler itself throws, a warning with both the original error and the handler's exception is written to the console, and the loop continues, so a faulty handler cannot stop a loop.
7.2 Every error raised through `Reactive.UnhandledException` is a `ReactiveException`. It derives from `Exception` and has the usual constructors: `ReactiveException()`, `ReactiveException(string message)` and `ReactiveException(string message, Exception inner)`. An exception thrown by a reaction or by a queued action is passed as the inner exception.
7.3 The following are logged as programming errors: an unhandled exception from a reaction (15.1), creating a reactor without a scheduler (15.2), attaching a scheduler to a thread that already has one of another type (15.2), requesting a shutdown from inside a reaction (15.2), starting a reaction while another one is running (15.2), and calling `ContinueRecording()` with a pause count of zero (15.5).
7.4 `Reaction.ExecutionCount` returns how often the reaction has run (15.3). It is for monitoring only: it can be read at any time, and it never changes how anything behaves. The library may compile it only into debug builds (`#if DEBUG`), so code that must also build against a release library does not rely on it.
7.5 What diagnostics mode is, and how it is switched on, is up to the implementation.

## 8. Computed values

A computed value is a register that holds the result of a function and only triggers when that result changes. Reactions that depend on a derived fact, for example whether a list is empty, then read that register instead of the list's count, and they no longer run on every change of the count.

8.1 `Reactor.Computed<T>(Register<T> register, Func<T> func, bool disposeOld = false)` adds one reaction to the reactor and returns it, as `Add` does (2.7). The reaction calls the function and writes the result into the register.
8.2 The register is an ordinary `Register<T>` owned by the caller. The write is a normal write (1.14, 3.1b), so the reactions connected to the register are triggered only when the result differs from the stored value (1.16).
8.3 As with every reaction, the first run is queued and never runs inline (2.8). Until it has run, the register keeps the value it had.
8.4 The function is the lambda of the reaction and is fixed (2.13). A null function writes `default(T)`. Another computation needs a new call to `Computed`.
8.5 An exception thrown by the function is handled as an exception of the reaction (8.1). The register is left unchanged.
8.6 When `disposeOld` is true and the value is replaced, the old value is disposed after the new value is stored and the connected reactions have been triggered, if it implements `IDisposable` (1.15).
8.7 On a disposed reactor, `Computed` creates nothing and returns null (2.9). Disposing the reaction or its reactor stops the computation, and the register keeps its last value.

## 9. Discussions

9.1 **Asynchronous code inside a reaction.** A reaction is a synchronous run (10.1). An `async` lambda runs synchronously on the caller's stack up to the first `await` that actually suspends, and then returns to its caller. Everything that executes up to that point is part of the run: its reads are recorded and its pauses apply. Everything after it is a continuation that runs later, outside the reaction, and is ordinary code: it records no reads, and its exceptions are not handled by the reaction (8.1).
This can be useful, because the reaction forms the trigger or precondition, after which the real work starts without being tied to the reaction. The consequence for the programmer is to read the registers the reaction depends on before the first `await`.
When the awaited task is already complete, the code after the `await` continues on the same stack, inside the run, so which reads are recorded can vary with timing.
After the first suspension an exception is no longer handled by the reaction. In an `async void` lambda it goes to the synchronisation context and can crash the application.
The continuation resumes on the captured synchronisation context, usually the UI thread, in a separate message dispatch. No reaction is running there, so `Reaction.Current` is null.

9.2 **Detaching the reactions of a shutdown.** Disposing a reaction normally queues a run with a null lambda, and that run detaches the reaction from its registers (2.17). During a shutdown no new runs are queued (3.2), yet the shutdown disposes all reactors (3.8). This is not a contradiction. The reactors are disposed only after everything that was queued has run, so nothing is left to do except cut the links to the registers. The shutdown therefore detaches those reactions from their registers directly, without a queued run.
