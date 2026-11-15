[← Reactive lists](reactive-lists.md) · [Topics](../index.md) · [Derived values, self-writes and cycles →](derived-values.md)

# 18. Reactive queues

`ReactiveQueue<T>` is a first-in, first-out queue of elements. `Push` adds at the tail. `Pop` and `TryPop` take from the head. Elements are taken, not held as a picture.

`ReactiveQueue<T>` derives from `Register`. Creating one does not need a scheduler. `Push`, `Pop` and `TryPop` can be called from any thread.

## Creating a queue

```csharp
var empty = new ReactiveQueue<int>();
var seeds = new ReactiveQueue<int>(new[] { 1, 1, 2 });
```

- `new ReactiveQueue<T>()` starts empty.
- The constructor that takes `IEnumerable<T>` stores those items in order. Duplicates stay, and the same instance can sit in the queue more than once. A null sequence throws `ArgumentNullException`.
- A null element is stored when `T` can be null.

The constructors store the items without triggering reactions. Elements are not compared: an equal value is another element ([Equality](equality.md)).

## Push, Pop and TryPop

```csharp
queue.Push("Ada");
var first = queue.Pop();
if (queue.TryPop(out var next))
    work(next);
```

Order is first in, first out. `TryPop` returns `false` when the queue is empty and sets the out value to `default(T)`. A stored `0` is a successful pop: `false` means empty. `Pop` on an empty queue throws `InvalidOperationException` with the message `The queue is empty.`

## The head is the dependency

`Pop` and `TryPop` record a read on whether a new element is available at the head, including when the queue is empty. `Push` records nothing.

A push triggers only when the queue was empty, because that element is then the new head. A push behind an existing head does not trigger. The later element is delivered when a pop uncovers it.

A successful pop triggers only when another element is then at the head. Popping the last element triggers nothing.

A reaction that pops one element per run therefore takes buffered elements one per run and does not run again after the last:

```csharp
var queue = new ReactiveQueue<string>(new[] { "Ada", "Byron" });
var seen = new List<string>();

reactor.Add(() =>
{
    if (queue.TryPop(out var name))
        seen.Add(name);
});
```

The first run takes `"Ada"` and is scheduled again because `"Byron"` is now at the head. The second run takes `"Byron"`. Nothing further is queued.

A reaction that drains the queue in one run takes every element present in that run. If one of those pops uncovers a further element, that trigger schedules one follow-up run, which finds the queue empty. Triggers coalesce, so the follow-up is one run:

```csharp
reactor.Add(() =>
{
    while (queue.TryPop(out var name))
        seen.Add(name);
});
```

A later `Push` of several items does the same again: one draining run, then one empty follow-up.

A run that does not call `Pop` or `TryPop` drops the watch, and later pushes do not wake it. The items stay in order. `Reaction.Pause` around a pop still takes the element and does not subscribe ([Pausing](pausing.md)).

`Pop` on an empty queue subscribes and then throws. Inside a reaction the exception reaches `Reactive.UnhandledException` as `InvalidOperationException` ([Errors and logging](errors-and-logging.md)). A later push reruns the reaction, and `Pop` returns that element.

Order inside one run follows the same rules. A `TryPop` that finds the queue empty subscribes first; a `Push` in that run then sees a listener and schedules a follow-up, which takes the element. A `Push` before any pop in that run notifies nobody, because `Push` itself does not subscribe; if the same run then pops the element, no follow-up is queued.

## Each element is taken once

Each element is taken by exactly one pop, from a reaction or from outside one. Pushes from one thread keep their order. Which of several popping reactions takes which element is not specified.

---

*Push, Pop and TryPop follow cs 12.34–12.37. Recording follows §7.1. (§ = spec/semantics.md 0.10, cs = spec/semantics-cs.md 0.6)*

[← Reactive lists](reactive-lists.md) · [Topics](../index.md) · [Derived values, self-writes and cycles →](derived-values.md)
