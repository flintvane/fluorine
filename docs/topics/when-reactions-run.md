[← Reactions and dependencies](reactions.md) · [Topics](../index.md) · [Equality: what counts as a change →](equality.md)

# 5. When reactions run

A change does not run a reaction. It **queues** a run on the reaction's scheduler, and the scheduler runs it after its current work (§1.4).

## Coalescing

A reaction that is already waiting for a run is not queued again (§4.4).

```csharp
// On a worker thread, in quick succession:
x.Value = 1;
x.Value = 2;
x.Value = 3;
```

A reaction reading `x` runs once and sees `3`. Any number of triggers before a run starts give exactly one run. A trigger that arrives while the run is in progress queues one further run.

## The run guarantee

After the last change of a register, every reaction that read it runs at least once (§4.1). Intermediate values may be skipped (§4.2). So a reaction always ends up having seen the final state, which is what UI code and derived values need.

## Order

Queued reactions run first come, first served (§11.9). Because runs coalesce, a reaction's place in the queue comes from its *first* pending trigger. A reaction that triggers itself during its run goes to the end of the queue (§14.7). Two reactions on the same scheduler never run at the same time, and one never runs inside another (§11.7, §11.8).

## Start-up on the same loop

When you create a reaction on its own loop, your current code finishes before the new reaction's first run (§11.5). Initialization can therefore be written in any order:

```csharp
// Running on the UI loop:
reactor.Add(() => total.Text = (price.Value * amount.Value).ToString());
price.Value = 12;
amount.Value = 3;
// The first run happens after this method returns and sees 12 and 3.
```

A reaction created on *another* loop may start while your code is still running (§11.6).

## Triggering by hand

```csharp
r.Trigger();   // queue a run of this reaction
```

`Reaction.Trigger()` queues a run and returns immediately (cs 2.15). Triggering a register by hand is covered in [Signal registers](signal-registers.md).

Triggering and queueing raise no exceptions of their own (§1.8). When a loop no longer accepts work, the run is dropped silently. Exceptions from the platform underneath a scheduler propagate.

## Liveness

A queued reaction always gets its run, and a scheduler gives other work of its loop (such as UI messages) its turn as well (§11.9a).

*Elsewhere: the same scenario in MobX, Vue, Solid, Knockout, Tracker, SignalsDotnet and Rx.NET: [Side by side: Coalesced runs](side-by-side/coalesced-runs.md).*

---

*Spec references: §1.4, §1.8, §4, §11.5-11.9a; cs 2.15. (§ = spec/semantics.md 0.9, cs = spec/semantics-cs.md 0.5)*

[← Reactions and dependencies](reactions.md) · [Topics](../index.md) · [Equality: what counts as a change →](equality.md)
