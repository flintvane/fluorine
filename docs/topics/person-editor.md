[← Binding a model to WinForms](winforms-binding.md) · [Topics](../index.md) · [Comparison with other frameworks →](comparison.md)

# 2. PersonEditor: one model in two windows

`demos/PersonEditor` opens the same `Person` in a WinForms window and a WPF window. Typing in either window updates the other. The model has no reference to either UI framework.

The demo targets .NET Framework 4.8, with WPF and WinForms, and uses C# 13. `[Reactive]` partial properties are written by the Fluorine source generator. `Person.Tags` is a `ReactiveSet<string>`: the WPF window shows it through `ObservableCollectionConverter`, and the WinForms window fills a `ListBox` by enumerating the set.

## Building and running

On Windows, from the repository root:

```
dotnet build demos/PersonEditor/PersonEditor.csproj
dotnet run --project demos/PersonEditor/PersonEditor.csproj
```

The project is also in `Fluorine.sln` as `PersonEditor`. `StartupObject` is `PersonDemo.Program`. The output type is `WinExe`.

WPF and WinForms require a Windows desktop. On other platforms the project does not build as a runnable application.

## The model

`Person` derives from `AutoNotify`. `FirstName`, `LastName`, `Age` and `AgeStatus` are `[Reactive]` partial properties: the generator writes a register for each one. `AgeStatus` is a computed value: the constructor calls `Computed` on that register with `DescribeAge`.

```csharp
public partial class Person : AutoNotify
{
    public Person() => Computed(ageStatus, DescribeAge);

    [Reactive]
    public partial string FirstName { get; set; }

    [Reactive]
    public partial string LastName { get; set; }

    [Reactive]
    public partial int Age { get; set; }

    [Reactive]
    public partial string AgeStatus { get; set; }

    public ReactiveSet<string> Tags { get; } = [];

    private string DescribeAge()
        => Age >= 1 ? null : "Age must be a positive integer.";
}
```

`Tags` is a get-only property so a WPF binding can reach the set. Enumerating it in a reaction subscribes to the whole set ([Reactive sets](reactive-sets.md)).

The first run of `Computed` is queued, so it happens after the constructor returns ([Computed values](computed.md)).

The check lives in the model. `Age` is an `int`, so the model sees a number, not the text the user is typing. Zero and negative numbers are rejected. Text that is not an integer yet (`""`, `"1-"`) never reaches `Age`: the WPF binding keeps the old value, and the WinForms converter does not call the setter. `AgeStatus` therefore stays on the last integer until a new one is stored, and is empty when the age is valid.

## Two schedulers

A thread has one scheduler. WPF and WinForms therefore cannot share a thread here: `PropertyChanged` from `AutoNotify` is raised on the scheduler where the `Person` was constructed, and a WPF binding must hear that on the dispatcher thread.

```csharp
[STAThread]
static void Main()
{
    Person person = null;
    var wpf = DispatcherScheduler.CreateNew("person-wpf", () =>
    {
        person = new Person();
        var window = new PersonWindow(person);
        window.Show();
    });

    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);
    Application.Run(new PersonForm(person));

    wpf.Shutdown();
}
```

`CreateNew` returns after its initial action, so `person` is assigned before the WinForms window is created. The WinForms form is created on this thread. Closing the WinForms window returns from `Application.Run` and then shuts the WPF scheduler down. Closing only the WPF window leaves the WinForms window running; the `Person` stays alive with it.

## The WPF window

`PersonWindow` sets `DataContext` to the `Person`. That attaches the bindings in `PersonWindow.xaml`, which subscribe to `PropertyChanged` and so arm the `AutoNotify` property reactions. `Tags` goes through `ObservableCollectionConverter`, which builds an `ObservableCollection<string>` on the dispatcher thread and keeps it in step with the set ([Showing a reactive set in WPF](observable-collections.md)).

```xml
<Window.Resources>
    <fluorine:ObservableCollectionConverter x:Key="ToObservableCollection"/>
</Window.Resources>
<TextBox Text="{Binding FirstName, UpdateSourceTrigger=PropertyChanged}"/>
<TextBox Text="{Binding LastName, UpdateSourceTrigger=PropertyChanged}"/>
<TextBox Text="{Binding Age, UpdateSourceTrigger=PropertyChanged, ValidatesOnExceptions=True}"/>
<TextBlock Text="{Binding AgeStatus, Mode=OneWay}" Foreground="DarkRed"/>
<ListBox ItemsSource="{Binding Tags, Converter={StaticResource ToObservableCollection}}"/>
```

`UpdateSourceTrigger=PropertyChanged` writes the register on each keystroke, so the WinForms window follows along. Age is an `int`: WPF's own converter rejects text that is not an integer and leaves `Age` unchanged. `ValidatesOnExceptions` draws the box as invalid in that case. `AgeStatus` is one way. The model computes it; this window only displays it.

Add and Remove buttons call `Tags.Add` and `Tags.Remove`. The same changes appear in the WinForms list.

## The WinForms window

`PersonForm` is the WinForms window. It attaches `WinFormsScheduler` in the constructor and binds the boxes in `OnLoad`, once the message loop has started.

```csharp
protected override void OnLoad(EventArgs e)
{
    base.OnLoad(e);
    _forms.BindText(_firstName, () => _person.FirstName, value => _person.FirstName = value);
    _forms.BindText(_lastName, () => _person.LastName, value => _person.LastName = value);
    _forms.BindText(_age, () => _person.Age, value => _person.Age = value, TextConverters.Int32);
    _forms.BindText(_status, () => _person.AgeStatus);
    _forms.Add(() =>
    {
        var current = _person.Tags.ToList();
        Reaction.Pause(() =>
        {
            _tags.Items.Clear();
            foreach (var tag in current)
                _tags.Items.Add(tag);
        });
    });
}
```

Each `BindText` adds a reaction that reads a `Person` property and so subscribes to that register. The tags reaction enumerates `Tags` into a list, then fills the `ListBox` inside `Reaction.Pause` so the list assignments do not join the subscriptions. A write in either window queues the other window's reaction on that window's own scheduler. Add and Remove buttons call `Tags.Add` and `Tags.Remove` on the same set.

Age uses `TextConverters.Int32`. `"07"` becomes `7` and the box is rewritten as `"7"`. `"1-"` does not parse, so `Age` is left alone and the typed text stays until a later model change replaces it. `AgeStatus` is one way: the computed reaction on the `Person` is the only writer.

Dispose the `FormsReactor` before the child controls. The base `Dispose` disposes the boxes, and a reaction must not assign into a disposed box.

---

*The demo uses AutoNotify (cs 11), Computed (cs 8), the `[Reactive]` generator, DispatcherScheduler.CreateNew (cs 5.2), WinFormsScheduler (cs 6), FormsReactor and `ObservableCollectionConverter`. (§ = spec/semantics.md 0.10, cs = spec/semantics-cs.md 0.6)*

[← Binding a model to WinForms](winforms-binding.md) · [Topics](../index.md) · [Comparison with other frameworks →](comparison.md)
