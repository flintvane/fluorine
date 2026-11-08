[← The Reactive attribute and the source generator](reactive-attribute.md) · [Topics](../index.md) · [PersonEditor →](person-editor.md)

# 21. Binding a model to WinForms

`FormsReactor` binds WinForms controls to functions that read (and, for text boxes, write) your model. It is a `Reactor`, so its bindings are reactions: they subscribe to whatever registers the read function touches, and they run on the scheduler of the reactor.

Create it on the UI thread after `WinFormsScheduler` is attached, and dispose it before the controls.

```csharp
WinFormsScheduler.AttachToCurrent();
var forms = new FormsReactor();
forms.BindText(firstNameBox, () => person.FirstName, value => person.FirstName = value);
forms.BindText(ageBox, () => person.Age, value => person.Age = value, TextConverters.Int32);
forms.BindText(statusLabel, () => person.AgeStatus);
forms.BindVisible(warning, () => person.Age < 1);
forms.BindEnabled(saveButton, () => person.Age >= 1);
```

`FormsReactor()` uses `Scheduler.Current`. `FormsReactor(Scheduler)` uses the scheduler you pass. Either constructor throws `ReactiveException` when there is no scheduler, as `Reactor` does.

The WinForms synchronisation context exists once the message loop has started. `Application.Run(new Form())` constructs the form first, so attach and bind in `OnLoad`, not in the control constructor. [PersonEditor](person-editor.md) follows that order.

## Text

`BindText` on a `Control` is one way, for a label or any control whose `Text` you want to set:

```csharp
public void BindText(Control control, Func<string> read)
public void BindText<T>(Control control, Func<T> read, TextConverter<T> converter)
```

`BindText` on a `TextBoxBase` with a write function is two way:

```csharp
public void BindText(TextBoxBase box, Func<string> read, Action<string> write)
public void BindText<T>(TextBoxBase box, Func<T> read, Action<T> write, TextConverter<T> converter)
```

The read function runs while the reaction is recording, so the binding depends on the registers it reads. The control is assigned inside a `Reaction.Pause`, after that read, because setting `Text` runs layout and `TextChanged` on the same stack. Those side effects do not join the subscriptions ([Pausing](pausing.md)).

A two-way box also writes the model from `TextChanged`. While the reaction itself is assigning, that handler stays quiet. If the box already shows the formatted text, the reaction does not assign again, so the caret stays where the user left it.

## Converters

`TextConverter<T>` formats a model value as control text and parses that text back:

```csharp
public sealed class TextConverter<T>
{
    public TextConverter(Func<T, string> format, TryParseText<T> tryParse);
    public string Format(T value);
    public bool TryParse(string text, out T value);
}

public delegate bool TryParseText<T>(string text, out T value);
```

When `TryParse` returns false, the write function is not called. Text that is not a value yet, such as `""` or `"1-"`, stays on screen and the model keeps its last stored value.

`TextConverters` provides stock converters. The properties read `CultureInfo.CurrentCulture` each time they format or parse. The `*In` methods pin an `IFormatProvider`, which is what tests use.

| Property | `*In` method |
|---|---|
| `String` | — |
| `Boolean` | — |
| `Byte`, `SByte`, `Int16`, `UInt16`, `Int32`, `UInt32`, `Int64`, `UInt64` | `ByteIn`, `SByteIn`, `Int16In`, `UInt16In`, `Int32In`, `UInt32In`, `Int64In`, `UInt64In` |
| `Single`, `Double`, `Decimal` | `SingleIn`, `DoubleIn`, `DecimalIn` |

A missing format function, parse function or format provider throws `ReactiveException`.

## Visible and enabled

```csharp
public void BindVisible(Control control, Func<bool> read)
public void BindEnabled(Control control, Func<bool> read)
```

These are set only. Their control getters report the parents as well as the control, so reading them back would replace the model's desired value with the effective one. The binding compares against the last value it wrote, then assigns inside a pause.

## Disposal

`Dispose()` unhooks every `TextChanged` and `Disposed` handler, then disposes the reactor and its reactions. Dispose the `FormsReactor` before the child controls: the base `Dispose` of a `UserControl` disposes the boxes, and a reaction must not assign into a disposed box.

If a bound control is disposed first, its `Disposed` handler unhooks the events and disposes that binding's reaction.

A missing control, read function, write function or converter throws `ReactiveException` from the `Bind*` call.

For the lower-level pattern of wrapping a control that holds its own data, see [Signal registers and shadow registers](signal-registers.md). `FormsReactor` is the helper for text, visibility and enabled state.

---

*FormsReactor builds on reactors, pausing and `WinFormsScheduler` (cs 2, 2.24, 6). (§ = spec/semantics.md 0.10, cs = spec/semantics-cs.md 0.6)*

[← The Reactive attribute and the source generator](reactive-attribute.md) · [Topics](../index.md) · [PersonEditor →](person-editor.md)
