using System.Windows;

namespace PersonDemo
{
    public partial class PersonWindow
    {
        // The caller has already constructed the Person on this dispatcher
        // thread. Setting DataContext is what attaches the bindings in the
        // XAML. Those bindings subscribe to PropertyChanged, and that is when
        // AutoNotify arms the property reactions (they stay idle until then).
        // The Tags ListBox goes through ObservableCollectionConverter, which
        // builds an ObservableCollection on this thread and keeps it in step
        // with the set.
        readonly Person _person;

        public PersonWindow(Person person)
        {
            InitializeComponent();
            _person = person;
            DataContext = person;
        }

        void AddTag_Click(object sender, RoutedEventArgs e)
        {
            var text = TagText.Text;
            if (string.IsNullOrWhiteSpace(text))
                return;
            _person.Tags.Add(text.Trim());
            TagText.Clear();
        }

        void RemoveTag_Click(object sender, RoutedEventArgs e)
        {
            var selected = TagList.SelectedItem as string;
            if (selected != null)
                _person.Tags.Remove(selected);
            else if (!string.IsNullOrWhiteSpace(TagText.Text))
                _person.Tags.Remove(TagText.Text.Trim());
        }
    }
}
