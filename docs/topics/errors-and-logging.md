[← Computed values](computed.md) · [Topics](../index.md) · [Shutting down →](shutdown.md)

# 14. Errors and logging

A reaction never stops its event loop (§8.2). Errors are reported through one event.

## Exceptions in a reaction

```csharp
reactor.Add(() => preview.Image = Image.FromFile(path.Value));
```

If `Image.FromFile` throws, the run ends, the exception is logged, and the loop continues (§8.1). The reaction keeps the dependencies it read up to the exception, so the next change of `path` runs it again. A reaction that throws before reading anything runs again only on a manual `Trigger()`.

## Reactive.UnhandledException

Every logged error is raised through one static event (cs 7.1):

```csharp
Reactive.UnhandledException += ex =>
    Trace.TraceError(ex.ToString());
```

- The event is of type `Action<Exception>`.
- With no handler attached, a warning is written to the console.
- When a handler throws, both errors are written to the console, and the loop continues.

An exception thrown by a reaction or by a queued action is passed as it is, unwrapped. `ReactiveException` is the type of the errors Fluorine itself detects (cs 7.2).

## Programming errors

Errors in how the library is used throw `ReactiveException` to the caller, as ordinary exceptions. They are not logged; the caller receives them (cs 7.2, §15.2):

- `new Reactor()` on a thread without a scheduler, or `new Reactor(null)`;
- a second scheduler on a thread that already has one;
- `CreateNew` with a null or empty name, or a scheduler that fails to start (cs 5.2);
- `Shutdown()` from inside a reaction;
- a reaction starting while another runs on the same thread (see [Rules of thumb](rules-of-thumb.md));
- `ContinueRecording()` with a pause count of zero.

Logging through `Reactive.UnhandledException` is for exceptions from user code that has no caller to receive them: an unhandled exception from a reaction, from a queued action, or from a scheduler's `CreateNew` initial action (cs 7.3, 5.2).

## Writes and triggers

Triggering and queueing raise no exceptions of their own (§1.8). An exception from the platform underneath, such as the WPF dispatcher, propagates unchanged. The only exception a write can raise is one from the value's own `Equals` ([Equality](equality.md)).

## Execution count

`Reaction.ExecutionCount` returns how often a reaction has run, for monitoring (cs 7.4). It is always available and never changes how anything behaves.

*Elsewhere: the same scenario in MobX, Vue, Solid, Knockout, Tracker, SignalsDotnet and Rx.NET: [Side by side: Errors](side-by-side/errors.md).*

---

*Spec references: §8, §15; cs 7. (§ = spec/semantics.md 0.10, cs = spec/semantics-cs.md 0.6)*

[← Computed values](computed.md) · [Topics](../index.md) · [Shutting down →](shutdown.md)
