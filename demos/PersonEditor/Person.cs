using Fluorine;

namespace PersonDemo
{
    // One person, with no reference to WPF or WinForms. Either frontend can
    // edit it. The window binds this object as its DataContext. The WinForms
    // control reads and writes the same properties through a FormsReactor.
    //
    // FirstName, LastName and Age are ordinary registers. Reading the property
    // inside a reaction subscribes to that register. Writing it from either
    // frontend triggers those reactions. The write may come from the other UI
    // thread; the reaction still runs on the scheduler it was created on.
    //
    // AgeStatus is a computed value. The library name for the helper is
    // Reactor.Computed. The docs also call this a derived value: a reaction
    // whose job is to write a register. Computed runs the function, writes the
    // result, and triggers readers only when the result differs by
    // object.Equals. The register itself is a normal Register<string> that
    // this object owns. The first run is queued, so it happens after this
    // constructor returns; the register is given that same text up front so
    // the screens do not flash an empty status.
    //
    // The check lives here, not in either frontend. Age is an int, so the
    // model sees a number, not the text the user is typing. A positive
    // integer means 1 or greater. Zero and negative numbers are rejected.
    // Text that is not an integer yet ("", "1-") never reaches Age: the WPF
    // binding keeps the old value, and the WinForms converter does not call
    // the setter. AgeStatus therefore stays on the last integer until a new
    // one is stored.
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

        // Tags is a reactive set. WPF binds it through ObservableCollectionConverter.
        // WinForms reads the tag values in a reaction and fills a ListBox.
        public ReactiveSet<string> Tags { get; } = [];

        private string DescribeAge() 
            => Age >= 1 ? null : "Age must be a positive integer.";
    }
}
