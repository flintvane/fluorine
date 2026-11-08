[← Reactive sets](reactive-sets.md) · [Topics](../index.md) · [Computed values →](computed.md)

# 13. Derived values, self-writes and cycles

Reactions may write registers. This is how derived values are made.

## Derived values

```csharp
var price  = new Register<decimal>();
var amount = new Register<int>();
var total  = new Register<decimal>();

reactor.Add(() => total.Value = price.Value * amount.Value);
reactor.Add(() => totalLabel.Text = total.Value.ToString("C"));
```

`total` is correct after its reaction has had its turn on the scheduler (§5.4). [Computed values](computed.md) offer the same pattern as one call, `reactor.Computed(total, () => price.Value * amount.Value)`. Until then it holds the previous total. Thanks to equality, a recomputation that gives the same total triggers nothing further.

## Self-writes

A reaction that writes a register it also reads triggers itself (§6.1). This is intended, and it is how a reaction follows a state step by step:

```csharp
reactor.Add(() =>
{
    if (position.Value < target.Value)
        position.Value = position.Value + 1;   // one step per run
});
```

The reaction runs again after each step, until `position` reaches `target`. Each step is a separate run. The new run goes to the end of the queue, so everything already waiting, such as UI messages and other reactions, runs before the next step (§11.9a, §14.7).

## Cycles

Fluorine runs what the state asks for (§6.2). Reactions that keep writing each other's registers without reaching a fixpoint keep running. Design such reactions so that the state converges; equality ends the chain as soon as a write changes nothing. `Reaction.ExecutionCount` helps to spot a reaction that runs more often than expected (cs 7.4).

## Creating reactions inside a reaction

A reaction may create reactions; they belong to the reactor they are added to, like any other (§6.4). Every new reaction is queued. A reaction that creates a new object with its own reactions on **every** run, and reads from it, creates and triggers new reactions forever. Create such objects once and reuse them (§14.3):

```csharp
CustomerView view = null;

reactor.Add(() =>
{
    var c = selectedCustomer.Value;
    if (view == null || view.Customer != c)
    {
        view?.Dispose();
        view = new CustomerView(c);   // only when the customer changes
    }
});
```

*Elsewhere: the same scenario in MobX, Vue, Solid, Knockout, Tracker, SignalsDotnet and Rx.NET: [Side by side: Derived values and self-writes](side-by-side/derived-and-self-writes.md).*

---

*Spec references: §5.4, §6, §14.3; cs 7.4. (§ = spec/semantics.md 0.10, cs = spec/semantics-cs.md 0.6)*

[← Reactive sets](reactive-sets.md) · [Topics](../index.md) · [Computed values →](computed.md)
