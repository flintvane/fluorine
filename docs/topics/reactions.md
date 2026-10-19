[← Registers](registers.md) · [Topics](../index.md) · [When reactions run →](when-reactions-run.md)

# 4. Reactions and dependencies

A reaction is a piece of code that runs again whenever a register it read has changed.

## Creating a reaction

Reactions are created through a reactor (cs 2.6, 2.7):

```csharp
Reaction r = reactor.Add(() => statusLabel.Text = status.Value);
Reaction n = reactor.Add("status label", () => statusLabel.Text = status.Value);
```

`Add` returns the `Reaction` and queues its first run on the reactor's scheduler. The first run always goes through the queue, also when you call `Add` on the scheduler's own thread (cs 2.8). The lambda is fixed at creation (cs 2.13).

## How dependencies are found

Every read during a run is recorded. Before each run, the reaction marks all registers it read last time as candidates for removal. A read of such a register keeps it; a read of a new register subscribes it at once. After the run, the registers still marked are unsubscribed (§7.3).

The result: **the dependencies of a reaction are exactly the registers its last run read.**

```csharp
reactor.Add(() =>
{
    if (showDetails.Value)
        details.Text = selected.Value.Description;
    else
        details.Text = "";
});
```

| `showDetails` | Registers read | A write to `selected` ... |
|---|---|---|
| `true` | `showDetails`, `selected` | runs the reaction |
| `false` | `showDetails` | runs nothing |

A branch that is no longer taken stops causing runs; a branch that is taken again subscribes again.

## A run is an ordinary call

A run is a plain synchronous call: it starts, does its work, and ends (§10.1). Inside, the lambda can read and write registers at any point. Writes take effect immediately, and the reaction sees its own writes at once (§11.10).

The same lambda may also be called directly, outside a reaction. Then it is an ordinary call and nothing is recorded (cs 2.14).

## Which reaction is running

`Reaction.Current` returns the reaction running on the calling thread, or `null` (cs 2.18). Reads are always attributed to the reaction running on the *reading* thread (§1.9). Code you hand to another thread does not subscribe your reaction.

## Next

How and when the runs happen is the subject of [When reactions run](when-reactions-run.md).

*Elsewhere: the same scenario in MobX, Vue, Solid, Knockout, Tracker, SignalsDotnet and Rx.NET: [Side by side: Dynamic dependencies](side-by-side/dynamic-dependencies.md).*

---

*Spec references: §1.9, §7.3, §10.1, §11.10; cs 2.6-2.18. (§ = spec/semantics.md 0.9, cs = spec/semantics-cs.md 0.5)*

[← Registers](registers.md) · [Topics](../index.md) · [When reactions run →](when-reactions-run.md)
