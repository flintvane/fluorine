[← Pausing: reads that do not subscribe](pausing.md) · [Topics](../index.md) · [Derived values, self-writes and cycles →](derived-values.md)

# 11. Signal registers and shadow registers

`Register` without a type argument is a register without a value: a pure signal (§12.8, cs 1.1).

## The two calls

```csharp
var changed = new Register();

changed.RecordRead();        // inside a reaction: subscribe to this signal
changed.TriggerReactions();  // trigger every reaction that read it
```

- `RecordRead()` subscribes the running reaction, unless recording is paused (cs 1.3).
- `TriggerReactions()` triggers all connected reactions (cs 1.4).

Both can be called from any thread (cs 1.5). `Register<T>` derives from `Register`, so these calls exist on every register. `TriggerReactions()` on a `Register<T>` triggers its readers without changing the value and regardless of equality (§4.5).

## Shadow registers

Some controls hold their data themselves, such as a text box. A shadow register sits next to the control and makes it readable by reactions (§14.1):

```csharp
class TrackedTextBox
{
    readonly TextBox box;
    readonly Register textChanged = new Register();

    public TrackedTextBox(TextBox box)
    {
        this.box = box;
        box.TextChanged += (s, e) => textChanged.TriggerReactions();
    }

    public string Text
    {
        get { textChanged.RecordRead(); return box.Text; }
    }
}
```

A reaction that reads `Text` depends on the text box, and runs again whenever the user types:

```csharp
reactor.Add(() => okButton.Enabled = searchBox.Text.Length > 0);
```

This is the purpose of the manual trigger. Objects whose state changes inside them provide registers for that state instead (§14.2).

To bind a model to WinForms text, visibility and enabled state, `FormsReactor` is the dedicated helper ([Binding a model to WinForms](winforms-binding.md)).

---

*Spec references: §4.5, §12.8, §14.1, §14.2, §14.4; cs 1.1-1.5. (§ = spec/semantics.md 0.10, cs = spec/semantics-cs.md 0.6)*

[← Pausing: reads that do not subscribe](pausing.md) · [Topics](../index.md) · [Derived values, self-writes and cycles →](derived-values.md)
