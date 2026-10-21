# Fluorine: platform notes (collected for the per-platform specifications)

Things that were removed from the platform-neutral spec because they belong in a specification per platform. Not part of `semantics.md`.

## C# / .NET
- Equality: `object.Equals(value, stored)`. `double.Equals` treats NaN as equal to itself, `==` does not. NaN is a good example of where natural equality often goes wrong.
- Async/await inside reactions: moved to `semantics-cs.md` (9.1).
- A lifetime link between a reactor and another object (disposed event, finalizer on a helper object) is an optional convenience (see `ideas.md`).
- Garbage-collection-driven cleanup of abandoned reactions is an optional platform extra. The core does not depend on it. Reasoning: with GC cleanup an unreachable but not yet collected reaction keeps running, so two reactions can fight over the same target property, and when this happens depends on GC timing.

## WPF
- `Dispatcher.InvokeShutdown()` aborts queued work, so the work does not run. `Dispatcher.BeginInvokeShutdown(...)` queues the shutdown as one more item, and the work queued before it runs. A WPF implementation of the shutdown rule (11.15) must therefore shut down with the second form. (Verified by test earlier.)

## WinForms
- A WinForms scheduler needs an STA message pump. `DoEvents` and modal dialogs are nested pumps, which are prohibited (10.3, 14.6).

## C# naming decisions for semantics-cs.md (2026-10-18)
- `Register` is a base class: a register without a value. `Register<T>` derives from it and adds the typed value and equality.
- `Register.RecordRead()` subscribes the running reaction. `Register.TriggerReactions()` posts every connected reaction and is the manual register trigger. `Reaction.Trigger()` is the manual reaction trigger.
- `Register<T>` has a `Value` property (getter calls `RecordRead`) and `bool SetValue(T newValue, out T oldValue)`: returns true if the value changed; `oldValue` holds the previous value (also when false).
- `SetValue` records no read: `oldValue` is an untracked read, intended for disposing or releasing the old value, not for control flow in a reaction (decided 2026-10-18).
- Overload `bool SetValue(T newValue)`: returns true if the value changed, no old value returned, records no read (2026-10-18).
- `Register<T>` has an empty constructor that initializes the backing value with default(T) (3.1a). A constructor with an initial value also exists (Grok Build G6: publishes the value, no trigger).

## Nested pumps (investigation, 2026-10-18)
- Grok Build tried about 54 approaches to detect nested pumps from .NET code: message loop hooks, a custom synchronisation context, and finally a per-thread pump depth that every pump has to bracket with a scope (`PumpScope`). The depth idea works only if the programmer marks every pump (`ShowDialog`, `DoEvents`, `Dispatcher.PushFrame`, OLE waits, private `GetMessage` loops), so it does not detect anything by itself. Nested pumps are therefore prohibited (`semantics.md` 10.3, 14.6).
- A reaction is never re-entered by a nested pump without the second start being visible: the check in 10.4 reports it.
