[← What Fluorine is](introduction.md) · [Topics](../index.md) · [Registers →](registers.md)

# 2. Your first reaction

This page builds a complete console program. It shows every piece once; later pages explain each of them in depth.

## The program

```csharp
using System;
using System.Threading;
using Fluorine;

class Program
{
    static void Main()
    {
        // 1. An event loop on its own thread.
        Scheduler loop = ThreadScheduler.CreateNew("loop");

        // 2. State.
        var first = new Register<string>("Ada");
        var last  = new Register<string>("Lovelace");

        // 3. A reactor on that loop, and a reaction in it.
        var reactor = new Reactor(loop);
        reactor.Add("full name", () =>
            Console.WriteLine(first.Value == ""
                ? "Must have a first name"
                : first.Value + " " + last.Value));

        // 4. Change the state from this (other) thread.
        Thread.Sleep(100);
        last.Value  = "Byron";
        first.Value = "Augusta";

        Thread.Sleep(100);
        // 5. Finish: drain the loop and dispose its reactors.
        Reactive.Shutdown();
    }
}
```

Output:

```
Ada Lovelace
Augusta Byron
```

(The `Thread.Sleep` calls only make the output easy to follow; they are not needed for correctness.)

## What happened

1. **The scheduler.** `ThreadScheduler.CreateNew("loop")` starts a thread named `loop` with its own event loop and returns its scheduler (cs 4.2). Reactions need a scheduler to run on.
2. **The registers.** `new Register<string>("Ada")` creates a register with an initial value. Storing an initial value triggers nothing (cs 1.7).
3. **The reactor and the reaction.** `new Reactor(loop)` creates a reactor on the given scheduler (cs 2.3). `Add` creates the reaction and queues its first run on that loop (cs 2.7). The first run prints *Ada Lovelace*. While it runs, it reads `first` and `last`, so it now depends on both.
4. **The writes.** Writing `last` queues a run. Writing `first` before that run has started queues nothing more: the run is already waiting. The single run sees both new values and prints *Augusta Byron*. The intermediate state *Ada Byron* is skipped, which is by design ([When reactions run](when-reactions-run.md)).
5. **Shutdown.** `Reactive.Shutdown()` lets every scheduler run what is queued, disposes their reactors and returns when all are done ([Shutting down](shutdown.md)).

## Dependencies follow the code

Add these lines before the shutdown:

```csharp
first.Value = "";        // prints "Must have a first name"
Thread.Sleep(100);
last.Value = "Shelley";  // prints nothing
```

With an empty first name the lambda reads only `first`. After that run the reaction is unsubscribed from `last`, so the write to `last` runs nothing. As soon as `first` gets a non-empty value, the reaction reads `last` again and depends on it again. [Reactions and dependencies](reactions.md) explains how.

## In a WPF or WinForms application

On a UI thread you attach a scheduler to the existing loop, and `new Reactor()` picks it up from the current thread:

```csharp
// In the WPF UI thread, for example in App.OnStartup:
DispatcherScheduler.AttachToCurrent();
var reactor = new Reactor();          // uses Scheduler.Current
reactor.Add(() => nameLabel.Content = first.Value + " " + last.Value);
```

See [Schedulers](schedulers.md).

*Elsewhere: the same scenario in MobX, Vue, Solid, Knockout, Tracker, SignalsDotnet and Rx.NET: [Side by side: First reaction](side-by-side/first-reaction.md).*

---

*Spec references: semantics §2; cs 1.7, 2.2, 2.3, 2.7, 4.2, 3.11. (§ = spec/semantics.md 0.10, cs = spec/semantics-cs.md 0.6)*

[← What Fluorine is](introduction.md) · [Topics](../index.md) · [Registers →](registers.md)
