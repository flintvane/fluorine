[← Equality: what counts as a change](equality.md) · [Topics](../index.md) · [Schedulers →](schedulers.md)

# 7. Reactors and lifetimes

A reactor owns a group of reactions. Its owner disposes it when they are no longer needed, and all its reactions end together.

## Creating a reactor

```csharp
var reactor = new Reactor();                 // the scheduler of this thread
var worker  = new Reactor(backgroundLoop);   // an explicit scheduler
reactor.Name = "customer view";
```

- `Reactor()` captures `Scheduler.Current` (cs 2.2). On a thread without a scheduler this is a programming error: it is logged and a `ReactiveException` is thrown.
- `Reactor(Scheduler)` uses the scheduler you pass (cs 2.3).
- All reactions of a reactor run on its scheduler (§12.4). Reactions for another loop go into another reactor.

## Ownership

Lifetimes are explicit (§12). The typical owner is the object whose state the reactions keep up to date:

```csharp
class CustomerView : IDisposable
{
    readonly Reactor reactor = new Reactor();

    public CustomerView(Customer customer, Label name, Label balance)
    {
        reactor.Add(() => name.Text = customer.Name.Value);
        reactor.Add(() => balance.Text = customer.Balance.Value.ToString("C"));
    }

    public void Dispose() => reactor.Dispose();
}
```

Registers and reactions refer to each other strongly (§12.1). A reaction lives until it, or its reactor, is disposed.

> **TODO** (cs 2.11, 2.17): the spec says "disposing"; that `Reactor` and `Reaction` implement `IDisposable` is the choice of the run-3 build (try3 GAPS 18) and should be confirmed in semantics-cs.md.

## What disposal does

Disposing a reaction removes all its subscriptions, and its lambda never executes again (§12.5, cs 2.17):

- a run that is queued but not started does not execute the lambda;
- later triggers have no effect;
- a run in progress completes, and causes no further run;
- disposing from another thread, from inside the reaction itself, or twice, is fine (§12.6).

Disposing a reactor disposes every reaction in it (cs 2.11). Disposing a single reaction removes it from its reactor (cs 2.12).

## A disposed reactor

`Add` on a disposed reactor creates nothing and returns `null` (cs 2.9). This is not an error; it can happen when an add coincides with a shutdown. `Queue(Action)` on a disposed reactor does nothing (cs 2.1). A reactor created on a scheduler that is shutting down behaves as a disposed reactor (cs 2.4).

## Queueing work through a reactor

```csharp
reactor.Queue(() => statusBar.Text = "Saved");
```

`Reactor.Queue(Action)` runs the action on the reactor's scheduler (cs 2.1). It is ordinary work: it records no reads. See [Schedulers](schedulers.md).

---

*Spec references: §12; cs 2.1-2.17. (§ = spec/semantics.md 0.9, cs = spec/semantics-cs.md 0.5)*

[← Equality: what counts as a change](equality.md) · [Topics](../index.md) · [Schedulers →](schedulers.md)
