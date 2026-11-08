[← Binding a model to WinForms](winforms-binding.md) · [Topics](../index.md) · [Comparison with other frameworks →](comparison.md)

# 21. PersonEditor: one model in two windows

`demos/PersonEditor` opens the same `Person` in a WinForms window and a WPF window. Typing in either window updates the other. The model has no reference to either UI framework.

The demo targets .NET Framework 4.8, with WPF and WinForms, and uses C# 7.3. It references the Fluorine library only; it does not use the source generator. The properties are written by hand and marked `[AutoNotify]`.

## Building and running

On Windows, from the repository root:

```
dotnet build demos/PersonEditor/PersonEditor.csproj
dotnet run --project demos/PersonEditor/PersonEditor.csproj
```

The project is also in `Fluorine.sln` as `PersonEditor`. `StartupObject` is `PersonDemo.Program`. The output type is `WinExe`.

WPF and WinForms require a Windows desktop. On other platforms the project does not build.

## The model

`Person` derives from `AutoNotify`. `FirstName`, `LastName` and `Age` each wrap a register. `AgeStatus` is a computed value: the constructor calls `Computed` on a `Register<string>` that this object owns, and a read-only `[AutoNotify]` property exposes it.

```csharp
public sealed class Person : AutoNotify
{
    public const string ValidAge = "Age is a positive integer.";
    public const string InvalidAge = "Age must be a positive integer.";

    readonly Register<string> _firstName = new Register<string>("Ada");
    readonly Register<string> _lastName = new Register<string>("Lovelace");
    readonly Register<int> _age = new Register<int>(36);
    readonly Register<string> _ageStatus = new Register<string>(ValidAge);

    public Person()
    {
        Computed(_ageStatus, () => DescribeAge(_age.Value));
    }

    [AutoNotify]
    public string FirstName
    {
        get { return _firstName.Value; }
        set { _firstName.Value = value ?? ""; }
    }

    // LastName and Age follow the same pattern.

    [AutoNotify]
    public string AgeStatus
    {
        get { return _ageStatus.Value; }
    }

    public static string DescribeAge(int age)
    {
        return age >= 1 ? ValidAge : InvalidAge;
    }
}
```

The first run of `Computed` is queued, so it happens after the constructor returns ([Computed values](computed.md)). The register is given `ValidAge` up front so the screens do not flash an empty status.

The check lives in the model. `Age` is an `int`, so the model sees a number, not the text the user is typing. Zero and negative numbers are rejected. Text that is not an integer yet (`""`, `"1-"`) never reaches `Age`: the WPF binding keeps the old value, and the WinForms converter does not call the setter. `AgeStatus` therefore stays on the last integer until a new one is stored.

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

`CreateNew` returns after its initial action, so `person` is assigned before the WinForms window is created. The WinForms control is created on this thread. Closing the WinForms window returns from `Application.Run` and then shuts the WPF scheduler down. Closing only the WPF window leaves the WinForms window running; the `Person` stays alive with it.

## The WPF window

`PersonWindow` sets `DataContext` to the `Person`. That attaches the bindings in `PersonWindow.xaml`, which subscribe to `PropertyChanged` and so arm the `AutoNotify` property reactions.

```xml
<TextBox Text="{Binding FirstName, UpdateSourceTrigger=PropertyChanged}"/>
<TextBox Text="{Binding LastName, UpdateSourceTrigger=PropertyChanged}"/>
<TextBox Text="{Binding Age, UpdateSourceTrigger=PropertyChanged, ValidatesOnExceptions=True}"/>
<TextBlock Text="{Binding AgeStatus, Mode=OneWay}" Foreground="DarkRed"/>
```

`UpdateSourceTrigger=PropertyChanged` writes the register on each keystroke, so the WinForms window follows along. Age is an `int`: WPF's own converter rejects text that is not an integer and leaves `Age` unchanged. `ValidatesOnExceptions` draws the box as invalid in that case. `AgeStatus` is one way. The model computes it; this window only displays it.

## The WinForms control

`PersonEditor` is a `UserControl`. Drop it on a form, and `OnLoad` binds the boxes. `PersonForm` only gives it a window and a message loop.

```csharp
protected override void OnLoad(EventArgs e)
{
    base.OnLoad(e);
    if (_forms != null)
        return;

    WinFormsScheduler.AttachToCurrent();
    _forms = new FormsReactor();
    _forms.BindText(_firstName, () => _person.FirstName, value => _person.FirstName = value);
    _forms.BindText(_lastName, () => _person.LastName, value => _person.LastName = value);
    _forms.BindText(_age, () => _person.Age, value => _person.Age = value, TextConverters.Int32);
    _forms.BindText(_status, () => _person.AgeStatus);
}

protected override void Dispose(bool disposing)
{
    if (disposing && _forms != null)
    {
        _forms.Dispose();
        _forms = null;
    }
    base.Dispose(disposing);
}
```

`AttachToCurrent` returns the scheduler already installed on this thread when one is there. Each `BindText` adds a reaction that reads a `Person` property and so subscribes to that register. A write in either window queues the other window's reaction on that window's own scheduler.

Age uses `TextConverters.Int32`. `"07"` becomes `7` and the box is rewritten as `"7"`. `"1-"` does not parse, so `Age` is left alone and the typed text stays until a later model change replaces it. `AgeStatus` is one way: the computed reaction on the `Person` is the only writer.

---

*The demo uses AutoNotify (cs 11), Computed (cs 8), DispatcherScheduler.CreateNew (cs 5.2), WinFormsScheduler (cs 6) and FormsReactor. (§ = spec/semantics.md 0.10, cs = spec/semantics-cs.md 0.6)*

[← Binding a model to WinForms](winforms-binding.md) · [Topics](../index.md) · [Comparison with other frameworks →](comparison.md)
