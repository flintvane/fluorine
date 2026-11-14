[← AutoNotify: notifying WPF](autonotify.md) · [Topics](../index.md) · [Binding a model to WinForms →](winforms-binding.md)

# 2. Showing a reactive set in WPF

A WPF `ItemsControl` or `ListBox` binds to an `ObservableCollection`. `ObservableCollectionConverter` is an `IValueConverter` that turns a reactive set or dictionary into an `ObservableCollection` and keeps that collection in step with the source.

Put the converter in the window resources and bind the set through it:

```xml
<Window xmlns:fluorine="clr-namespace:Fluorine;assembly=Fluorine.Wpf"
        ...>
    <Window.Resources>
        <fluorine:ObservableCollectionConverter x:Key="ToObservableCollection"/>
    </Window.Resources>
    <ListBox ItemsSource="{Binding Tags, Converter={StaticResource ToObservableCollection}}"/>
</Window>
```

The bound property is the set itself, as a get-only property so WPF can bind it. The `Version` property stays the picture you use in reactions:

```csharp
public ReactiveSet<string> Tags { get; } = new ReactiveSet<string>();
public ReactiveSet<string>.Version TagValues => Tags.Value;
```

Add and remove on the set, from any thread:

```csharp
person.Tags.Add("mathematician");
person.Tags.Remove("mathematician");
```

The converter constructs the collection on the thread that runs `Convert`, which for a WPF binding is the dispatcher thread. The collection creates its own reactor on that thread, so it is only written on that scheduler.

The reaction starts when the first `CollectionChanged` handler attaches, typically when the control binds, and fills the collection from the current picture. It stops when the last handler leaves. Each later run diffs the new picture against the last one it applied, and writes only the net changes ([Reactive sets](reactive-sets.md)):

- `Added` appends the item.
- `Removed` moves the last item into the gap and then drops the last slot. A set has no order, so the collection does not shift the items after the hole.
- `Replaced` puts the new instance at the same index.

The collection is written only by that reaction. An outside write, such as `Add` or `Clear` on the collection itself, throws `InvalidOperationException`.

[PersonEditor](person-editor.md) binds `Person.Tags` this way, and the WinForms window edits the same set.

The converter also accepts a `ReactiveSortedSet<T>`. That view stays in comparer order.

## Dictionary views

The same converter turns a `ReactiveDictionary<TKey, TValue>` or a `ReactiveSortedDictionary<TKey, TValue>` into an `ObservableCollection`. `ConverterParameter` selects what each entry contributes, as a `DictionaryViewMode` or as its name: `Keys`, `Values`, or `Pairs`. With no parameter the view shows pairs.

```xml
<ListBox ItemsSource="{Binding Scores, Converter={StaticResource ToObservableCollection}, ConverterParameter=Keys}"/>
<ListBox ItemsSource="{Binding Scores, Converter={StaticResource ToObservableCollection}, ConverterParameter=Values}"/>
<ListBox ItemsSource="{Binding Scores, Converter={StaticResource ToObservableCollection}, ConverterParameter=Pairs}"/>
```

The bound property is the dictionary itself. Add, replace and remove on the dictionary, from any thread; the view follows those changes on the dispatcher thread.

A `ReactiveDictionary` has no order: the view keeps the position of each key and fills a hole from the end. A `ReactiveSortedDictionary` view stays in key order. A value change updates the item in a `Values` or `Pairs` view, and leaves a `Keys` view as it is.

[PersonEditor](person-editor.md) opens a WPF window that shows both dictionaries in all three modes.

---

*`ObservableCollectionConverter` is described here from the C# library. The collection is kept in step through a reaction on the constructing thread's scheduler.*

[← AutoNotify: notifying WPF](autonotify.md) · [Topics](../index.md) · [Binding a model to WinForms →](winforms-binding.md)
