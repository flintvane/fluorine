[← Shutting down](shutdown.md) · [Topics](../index.md) · [Rules of thumb →](rules-of-thumb.md)

# 15. Async code inside a reaction

A reaction's lambda is a synchronous call (§10.2). An `async` lambda is still allowed, and its first part belongs to the run (cs 8.1).

## Which part is the run

```csharp
reactor.Add(async () =>
{
    string q = query.Value;              // recorded: part of the run
    var results = await SearchAsync(q);  // the run ends here
    resultList.ItemsSource = results;    // a continuation, outside the reaction
});
```

- Everything up to the first `await` that actually suspends is part of the run: reads are recorded and pauses apply.
- Everything after it is ordinary code. It records no reads, and `Reaction.Current` is `null` there.
- The continuation resumes on the captured synchronization context, usually the UI thread.

This is useful: the reaction is the trigger, and the real work runs on its own.

## Writing it safely

1. **Read all dependencies before the first `await`.** Reads after it are not recorded.
2. **Handle exceptions after the first `await` yourself.** The lambda is `async void`; an exception there goes to the synchronization context and can end the application.
3. **Expect timing to matter.** An `await` on a task that has already completed continues inside the run, so its later reads are then recorded.

```csharp
reactor.Add(async () =>
{
    string q = query.Value;            // all reads first
    try
    {
        var results = await SearchAsync(q);
        searchResults.Value = results; // a write is fine anywhere
    }
    catch (Exception ex)
    {
        searchError.Value = ex.Message;
    }
});
```

Writing a register after the `await` is ordinary threading: it triggers the reactions that read it, as any write does. A display reaction reading `searchResults` then shows the result.

---

*Spec references: §10.2; cs 8.1. (§ = spec/semantics.md 0.9, cs = spec/semantics-cs.md 0.5)*

[← Shutting down](shutdown.md) · [Topics](../index.md) · [Rules of thumb →](rules-of-thumb.md)
