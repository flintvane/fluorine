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

The following are programming errors. Where noted, a `ReactiveException` is thrown to the caller (cs 7.2, 7.3, §15.2):

| Situation | Logged | Thrown to the caller |
|---|---|---|
| Unhandled exception in a reaction or queued action | yes | — |
| `new Reactor()` on a thread without a scheduler | yes | yes |
| Attaching a scheduler of another type to a thread that has one | yes | yes |
| `Shutdown()` from inside a reaction | yes | yes |
| A reaction starting while another runs on the same thread | fatal error, see [Rules of thumb](rules-of-thumb.md) | yes |
| `ContinueRecording()` with a pause count of zero | yes | — |

## Writes and triggers

Triggering and queueing raise no exceptions of their own (§1.8). An exception from the platform underneath, such as the WPF dispatcher, propagates unchanged. The only exception a write can raise is one from the value's own `Equals` ([Equality](equality.md)).

## Execution count

`Reaction.ExecutionCount` returns how often a reaction has run, for monitoring (cs 7.4). It may be compiled into debug builds only, so use it in diagnostics code, and keep it out of application logic.

*Elsewhere: the same scenario in MobX, Vue, Solid, Knockout, Tracker, SignalsDotnet and Rx.NET: [Side by side: Errors](side-by-side/errors.md).*

---

*Spec references: §8, §15; cs 7. (§ = spec/semantics.md 0.9, cs = spec/semantics-cs.md 0.5)*

[← Computed values](computed.md) · [Topics](../index.md) · [Shutting down →](shutdown.md)
