<p align="center"><img src="../logo.png" alt="Fluorine" width="120"></p>

[Topics](../index.md) · [Your first reaction →](first-reaction.md)

# 1. What Fluorine is

Fluorine is a reactive programming library. It keeps code in step with changing state, without event wiring, subscription bookkeeping or manual refreshes.

## The idea in three sentences

- Program state lives in **registers**.
- Code that must follow that state is written as a **reaction**.
- A reaction reads registers while it runs; those reads become its dependencies, and when one of them changes, the reaction runs again.

```csharp
reactor.Add(() => label.Text = first.Value + " " + last.Value);
```

This line is the whole subscription. The reaction reads `first` and `last`, so a change to either of them runs it again.

## Where reactions run

Every reaction belongs to an event loop: a UI thread, or a dedicated loop thread. Fluorine calls such a loop a **scheduler**. A change never runs a reaction on the spot. It queues a run on the reaction's own loop, which picks it up after the work it is currently doing.

That gives three properties you can rely on:

- Writers never wait. A worker thread can write a register and continue immediately.
- A reaction always runs on its own thread, one run at a time.
- Several changes in a row lead to one run that sees the latest values.

## The vocabulary

| Term | Meaning |
|---|---|
| Register | Holds a value and remembers which reactions read it. |
| Reaction | A piece of code (its *lambda*) that reads registers and runs again when they change. |
| Reactor | A group of reactions that are created and disposed together. |
| Scheduler | An event loop on which reactions run. |
| Run | One execution of a reaction's lambda. |
| Pause | A scope in which reads do not subscribe. |

## What it is for

Fluorine suits programs built around event loops, such as desktop UIs: view models, labels, enabled states, derived values, outputs that must match the current state. The following pages introduce these pieces one at a time, starting with a complete small program in [Your first reaction](first-reaction.md).

---

*Spec references: §Purpose, §0. (§ = spec/semantics.md 0.10, cs = spec/semantics-cs.md 0.6)*

[Topics](../index.md) · [Your first reaction →](first-reaction.md)
