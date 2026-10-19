[← Threads and consistency](threads-and-consistency.md) · [Topics](../index.md) · [Signal registers and shadow registers →](signal-registers.md)

# 10. Pausing: reads that do not subscribe

Sometimes a reaction must read a register without depending on it. Pausing makes reads inside a scope untracked (§9).

## The three shapes

```csharp
// A using block
using (Reaction.Pause())
{
    log.Write("selected: " + selected.Value);   // not a dependency
}

// An action
Reaction.Pause(() => RaiseSelectionChanged());

// A function with a result
int limit = Reaction.Pause(() => maxItems.Value);
```

`Pause()` returns an `IDisposable` that continues recording when disposed. `Pause(Action)` and `Pause<T>(Func<T>)` pause, call the delegate and continue in a `finally` block (cs 2.24). Pauses nest (§9.2).

## A typical use

Raising UI events from inside a reaction can trigger layout code that reads many registers. Without a pause, the reaction would depend on all of them (§9.1):

```csharp
reactor.Add(() =>
{
    var items = filteredItems.Value;              // the real dependency
    Reaction.Pause(() => listBox.DataSource = items); // layout reads stay untracked
});
```

## Precise rules

- A paused read does not subscribe, and does not keep an existing subscription alive. A register read *only* inside a pause during a run is unsubscribed after that run (§7.3a, §9.2a).
- A register read both normally and paused in one run stays a dependency.
- Triggering cannot be paused: writes inside a pause trigger as usual (§6.3).

## The low-level calls

`Reaction.PauseRecording()` and `Reaction.ContinueRecording()` raise and lower the pause count of the running reaction (cs 2.19, 2.20). Prefer the shapes above, which always balance.

- Calling `ContinueRecording()` at count zero logs a programming error and leaves the count at zero (cs 2.20).
- The count belongs to the run and ends with it, so a forgotten `ContinueRecording()` never leaks into the next run (§9.3, cs 2.23).
- Outside a reaction both calls have no effect (cs 2.21).

## Checking the state

`Reaction.Recording` is `true` when a reaction is running on this thread and is not paused, that is, when a read would be recorded now (cs 2.22). `Reaction.Current` returns the running reaction, paused or not (cs 2.18).

*Elsewhere: the same scenario in MobX, Vue, Solid, Knockout, Tracker, SignalsDotnet and Rx.NET: [Side by side: Untracked reads](side-by-side/untracked-reads.md).*

---

*Spec references: §6.3, §7.3a, §9; cs 2.18-2.24. (§ = spec/semantics.md 0.9, cs = spec/semantics-cs.md 0.5)*

[← Threads and consistency](threads-and-consistency.md) · [Topics](../index.md) · [Signal registers and shadow registers →](signal-registers.md)
