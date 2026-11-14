namespace Fluorine;

// What a WPF view of a reactive dictionary shows: its keys, its values, or its
// key-value pairs. Passed as the ConverterParameter of ObservableCollectionConverter.
public enum DictionaryViewMode
{
    Pairs,
    Keys,
    Values
}
