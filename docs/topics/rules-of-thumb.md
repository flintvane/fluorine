[← Async code inside a reaction](async.md) · [Topics](../index.md) · [AutoNotify: notifying WPF →](autonotify.md)

# 19. Rules of thumb

A short checklist, with the reasons behind each rule.

## One entry, one exit

A reaction is a plain call that starts, works and ends (§10.1). It never runs a nested message pump: no modal dialogs (`ShowDialog`), no `Application.DoEvents`, no `Dispatcher.PushFrame` (§10.3).

A nested pump runs other code in the middle of the reaction. Its writes are then mixed with other work, and reads from the other code are attributed to the running reaction (§14.6). To show a dialog from a reaction, queue it:

```csharp
reactor.Add(() =>
{
    if (error.Value != null)
    {
        string message = error.Value;
        reactor.Queue(() => MessageBox.Show(message));   // runs after the reaction
    }
});
```

## One reaction per thread

At most one reaction runs per thread (§10.4, cs 3.15). A reaction starting while another is running is a fatal error: it means a nested pump was run. The second reaction does not start, and a `ReactiveException` is thrown (§10.4, cs 3.16).

## Checklist

- Create a scheduler for every loop thread before creating reactors ([Schedulers](schedulers.md)).
- Give every reactor an owner that disposes it ([Reactors and lifetimes](reactors.md)).
- Store immutable values with value equality ([Equality](equality.md)).
- Put state that must stay consistent into one aggregate register ([Threads and consistency](threads-and-consistency.md)).
- Tolerate glitches across threads; trust the run guarantee ([When reactions run](when-reactions-run.md)).
- Pause around code whose reads are not dependencies ([Pausing](pausing.md)).
- Create objects with their own reactions once, and reuse them ([Derived values](derived-values.md)).
- In `async` lambdas, read first and await later ([Async](async.md)).
- Attach a handler to `Reactive.UnhandledException` ([Errors and logging](errors-and-logging.md)).
- End with `Reactive.Shutdown()` ([Shutting down](shutdown.md)).

---

*Spec references: §10, §14.6; cs 3.15, 3.16. (§ = spec/semantics.md 0.10, cs = spec/semantics-cs.md 0.6)*

[← Async code inside a reaction](async.md) · [Topics](../index.md) · [AutoNotify: notifying WPF →](autonotify.md)
