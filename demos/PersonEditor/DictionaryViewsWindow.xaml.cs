using System.Windows;
using Fluorine;

namespace PersonDemo
{
    public partial class DictionaryViewsWindow
    {
        // The dictionaries are created on this dispatcher thread. The six
        // bindings go through ObservableCollectionConverter, which builds a
        // view for each mode on the same thread and keeps it in step.
        public ReactiveDictionary<string, int> Unsorted { get; } = [];

        public ReactiveSortedDictionary<string, int> Sorted { get; } = [];

        public DictionaryViewsWindow()
        {
            InitializeComponent();

            // The same three entries, added in this order, so the unsorted
            // lists start in insertion order and the sorted lists in key order.
            SetBoth("pear", 3);
            SetBoth("apple", 1);
            SetBoth("banana", 2);

            DataContext = this;
        }

        void Set_Click(object sender, RoutedEventArgs e)
        {
            var key = KeyText.Text.Trim();
            if (key.Length == 0)
                return;
            if (!int.TryParse(ValueText.Text, out var value))
                return;
            SetBoth(key, value);
            KeyText.Clear();
            ValueText.Clear();
        }

        void Remove_Click(object sender, RoutedEventArgs e)
        {
            var key = KeyText.Text.Trim();
            if (key.Length == 0)
                return;
            Unsorted.Remove(key);
            Sorted.Remove(key);
            KeyText.Clear();
            ValueText.Clear();
        }

        void SetBoth(string key, int value)
        {
            Unsorted[key] = value;
            Sorted[key] = value;
        }
    }
}
