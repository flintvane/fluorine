[← Reactors and lifetimes](reactors.md) · [Topics](../index.md) · [Threads and consistency →](threads-and-consistency.md)

# 8. Schedulers

A scheduler is an event loop that runs reactions, one at a time, on its own thread. Schedulers are long-lived: typically one per loop thread, for the life of the application (cs 3.10).

## The three schedulers

| Scheduler | For | How you get one |
|---|---|---|
| `ThreadScheduler` | a dedicated thread with its own loop | `ThreadScheduler.CreateNew(name, initial)` |
| `DispatcherScheduler` | a WPF `Dispatcher` | `DispatcherScheduler.AttachToCurrent()` or `DispatcherScheduler.CreateNew(name, initial)` |
| `WinFormsScheduler` | the WinForms message loop | `WinFormsScheduler.AttachToCurrent()` |

All derive from the abstract class `Scheduler` (cs 3.1).

### A loop thread

```csharp
Scheduler io = ThreadScheduler.CreateNew("io", () =>
{
    // Runs on the new thread, with Scheduler.Current already set.
    ioReactor = new Reactor();
});
```

`CreateNew` starts a thread with the given name, so logs and debuggers show which thread is which, and installs the scheduler there. The name must be non-empty; a null or empty name throws `ReactiveException` before any thread exists (cs 5.2).

The optional action is the first message the new scheduler runs. Nothing runs before it, including work from `Reactive.Shutdown`. `CreateNew` returns the scheduler once the action has finished, and the loop keeps running on that thread (cs 4.2, 5.2).

If the action throws, the exception goes unwrapped to `Reactive.UnhandledException`, and `CreateNew` still returns the scheduler. When the thread or its scheduler cannot be brought up, for example because the platform refuses a dispatcher, you receive a `ReactiveException` with the original exception as its `InnerException`. Nothing is logged, and no scheduler remains for that thread (cs 5.2).

### WPF

```csharp
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherScheduler.AttachToCurrent();
    }
}
```

`AttachToCurrent` attaches a scheduler to the calling thread's dispatcher; calling it again returns the same scheduler (cs 5.3). `DispatcherScheduler.CreateNew` starts a new dispatcher thread, like `ThreadScheduler.CreateNew` (cs 5.2).

### WinForms

```csharp
// On the UI thread, once the WinForms context is installed (e.g. in the form's constructor):
WinFormsScheduler.AttachToCurrent();
```

The calling thread must have the WinForms `SynchronizationContext`; otherwise the call throws `ReactiveException` (cs 6.1). The application keeps owning its message loop (cs 6.3).

## Finding the scheduler of a thread

- `Scheduler.Current`: the scheduler of the calling thread, or `null` (cs 3.6).
- `Scheduler.ForThread(thread)`: the scheduler of another thread, or `null` (cs 3.13).

A thread has at most one scheduler, of any type, and it stays the same object for the life of the thread (cs 3.13, 3.14). Creating or attaching a second scheduler on a thread that has one throws `ReactiveException`; `AttachToCurrent()` of the same type returns the existing one.

## Queueing your own work

```csharp
io.Queue(() => File.WriteAllText(path, text.Value));
```

`Scheduler.Queue(Action)` runs the action on the loop, in order with the reaction runs (cs 3.3). It never runs inline and raises no exception of its own (§1.8). The action is not a reaction, so its reads subscribe nothing; an exception from it is logged. `Reactor.Queue(Action)` does the same through a reactor.

## Schedulers side by side

Schedulers never block or delay each other (§11.7a). Any reactor can use any scheduler (cs 3.12), so a background reactor can compute and a UI reactor can display:

```csharp
var compute = new Reactor(io);
var ui      = new Reactor();      // on the UI thread

compute.Add(() => result.Value = Expensive(input.Value));
ui.Add(() => resultLabel.Text = result.Value);
```

## Writing your own scheduler

`Scheduler` is abstract and can be derived from (cs 3.1). A custom scheduler:

- is constructed on the thread it owns: the protected constructor installs it there, so `Scheduler.Current` and `Scheduler.ForThread` find it;
- implements `protected abstract void Schedule(Action action)`, which hands the work to its loop and never runs it inline. `Queue(Reaction)` and `Queue(Action)` both go through it;
- takes part in shutdown through the overridable `OnShutdownRequested()`, `DrainOnOwnerThread()` and `OnShutdownFinished()`, and uses `AcceptWork()` and `CompleteShutdown()` to follow the rules of [Shutting down](shutdown.md);
- runs user actions through `RunUserAction(Action)`, which logs their exceptions.

The scheduler runs a reaction through the protected `Run(Reaction)` of the base class, synchronously, on its own thread (cs 3.4). `Queue(Reaction)` passes exactly that call to `Schedule`. The loop of a scheduler created by `CreateNew` runs from its creation on. A scheduler that attaches to an existing loop uses that loop (cs 3.7). `ThreadScheduler` in the source is a compact example of all of these.

---

*Spec references: §11.1-11.4, §11.7a; cs 3, 4, 5, 6. (§ = spec/semantics.md 0.10, cs = spec/semantics-cs.md 0.6)*

[← Reactors and lifetimes](reactors.md) · [Topics](../index.md) · [Threads and consistency →](threads-and-consistency.md)
