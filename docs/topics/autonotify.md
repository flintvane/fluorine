[← Rules of thumb](rules-of-thumb.md) · [Topics](../index.md) · [The Reactive attribute and the source generator →](reactive-attribute.md)

# 20. AutoNotify: notifying WPF

WPF bindings listen to `INotifyPropertyChanged`. `AutoNotify` is a model base class that raises `PropertyChanged` when a marked getter starts returning a different value (cs 11). The getter reads registers in the usual way, so the notification is just another reaction: it runs on the scheduler where the object was created.

## A model

Derive from `AutoNotify` and mark each property that WPF should see:

```csharp
public class Person : AutoNotify
{
    readonly Register<string> _firstName = new Register<string>("Ada");
    readonly Register<string> _lastName  = new Register<string>("Lovelace");

    [AutoNotify]
    public string FirstName
    {
        get { return _firstName.Value; }
        set { _firstName.Value = value ?? ""; }
    }

    [AutoNotify]
    public string LastName
    {
        get { return _lastName.Value; }
        set { _lastName.Value = value ?? ""; }
    }

    [AutoNotify]
    public string FullName
    {
        get { return FirstName + " " + LastName; }
    }
}
```

Construct the object on the dispatcher thread, after that thread has a `DispatcherScheduler`. `AutoNotify()` creates the object's reactor on `Scheduler.Current` (cs 11.1). In this implementation `AutoNotify` itself derives from `Reactor`, so the object is the reactor: you can call `Add`, `Computed` and `Dispose` on it.

A constructor with no scheduler on the thread throws `ReactiveException`, as `new Reactor()` does (cs 2.2, 11.1). The exception is not logged.

## How notification works

For every marked property, one reaction is added to that reactor (cs 11.3). It reads the getter. The first run records the dependencies and the value, and raises nothing: `INotifyPropertyChanged` notifies only on change (cs 11.4). A later run raises `PropertyChanged` with the property name when the getter returns a value that differs by `object.Equals`, and stores the new value.

The reaction runs on the reactor's scheduler, so `PropertyChanged` is raised on that thread (cs 11.3). WPF bindings must hear it on the dispatcher thread. Create the model there: for example in `App.OnStartup` after `DispatcherScheduler.AttachToCurrent()`, or in the initial action of `DispatcherScheduler.CreateNew`.

The first run is queued, so it happens after the derived constructor has finished (cs 11.4). Property reactions are also created only when the first handler attaches to `PropertyChanged` (cs 9.4). Objects that nobody binds to cost no property reactions. Methods marked `[Reactive]` are different: they are added in the constructor; see [The Reactive attribute](reactive-attribute.md).

## Inheritance

A marked property stays marked in derived classes, including overrides. A property declared with `new` hides it and is marked only if it carries `[AutoNotify]` itself (cs 11.2).

## Lifetime

`Dispose()` disposes the reactor and its reactions (cs 11.5). Afterwards the object raises no further notifications. Disposing twice is fine.

```csharp
person.Dispose();
```

## Binding in XAML

Set the object as `DataContext`. The bindings are ordinary WPF bindings:

```xml
<TextBox Text="{Binding FirstName, UpdateSourceTrigger=PropertyChanged}"/>
<TextBlock Text="{Binding FullName, Mode=OneWay}"/>
```

`UpdateSourceTrigger=PropertyChanged` writes the setter on each keystroke. A write from any thread still queues the notification on the model's scheduler, so another window that shares the same object can follow along ([PersonEditor](person-editor.md)).

The same object can be bound from WinForms without `PropertyChanged`: a `FormsReactor` reaction that reads the getter subscribes to the registers directly ([Binding a model to WinForms](winforms-binding.md)).

---

*Spec references: cs 11, 9.4, 2.2. (§ = spec/semantics.md 0.10, cs = spec/semantics-cs.md 0.6)*

[← Rules of thumb](rules-of-thumb.md) · [Topics](../index.md) · [The Reactive attribute and the source generator →](reactive-attribute.md)
