using System.Windows;

namespace PersonDemo
{
    public partial class PersonWindow : Window
    {
        // The caller has already constructed the Person on this dispatcher
        // thread. Setting DataContext is what attaches the bindings in the
        // XAML. Those bindings subscribe to PropertyChanged, and that is when
        // AutoNotify arms the property reactions (they stay idle until then).
        public PersonWindow(Person person)
        {
            InitializeComponent();
            DataContext = person;
        }
    }
}
