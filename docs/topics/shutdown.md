[← Errors and logging](errors-and-logging.md) · [Topics](../index.md) · [Async code inside a reaction →](async.md)

# 14. Shutting down

A shutdown lets a scheduler finish its queued work, disposes its reactors, and returns when that is done.

## One scheduler

```csharp
io.Shutdown();
```

`Scheduler.Shutdown()` (cs 3.8, §11.15, §11.16):

1. runs every reaction and action that was already queued;
2. ignores work that arrives after the shutdown began, including work queued by the reactions being drained;
3. disposes all reactors of the scheduler, which disconnects their reactions from their registers;
4. returns.

It can be called from any thread. On the scheduler's own thread, outside a reaction, the call processes what is still open itself. A second call waits for the same shutdown to finish.

A `ThreadScheduler` thread ends after its shutdown (cs 4.3). A WinForms scheduler leaves the message loop running, because the application owns it (cs 6.3).

## Every scheduler at once

```csharp
Reactive.Shutdown();
```

`Reactive.Shutdown()` tells every scheduler to finish, all at once, and returns when all of them are done (cs 3.11, §11.17). This is the call for the end of an application:

```csharp
protected override void OnExit(ExitEventArgs e)   // WPF App
{
    Reactive.Shutdown();
    base.OnExit(e);
}
```

> **TODO** (cs 3.6, 3.8, 5.4): the spec does not yet say whether `DispatcherScheduler.Shutdown()` from the dispatcher thread inside `OnExit` interacts with the WPF shutdown; verify in the implementation.

## After a shutdown

- `Scheduler.Current` is `null` on that thread (cs 3.6).
- Work offered to the scheduler is dropped silently (cs 3.2, 3.3).
- A reactor created on it behaves as disposed: `Add` returns `null` (cs 2.4).

## Shutdown from inside a reaction

A reaction cannot wait for its own loop to finish. Calling a shutdown from a reaction logs a programming error and throws `ReactiveException` (cs 3.8). Queue the shutdown as ordinary work, or call it from outside the loop.

---

*Spec references: §11.15-11.17; cs 2.4, 3.6, 3.8, 3.11, 4.3, 6.3. (§ = spec/semantics.md 0.9, cs = spec/semantics-cs.md 0.5)*

[← Errors and logging](errors-and-logging.md) · [Topics](../index.md) · [Async code inside a reaction →](async.md)
