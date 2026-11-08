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

        [AutoNotify]
        public string LastName
        {
            get { return _lastName.Value; }
            set { _lastName.Value = value ?? ""; }
        }

        [AutoNotify]
        public int Age
        {
            get { return _age.Value; }
            set { _age.Value = value; }
        }

        // Read only. The computed reaction is the writer. [AutoNotify] raises
        // PropertyChanged when the register changes, which is what the WPF
        // binding listens to. The WinForms control does not: its reaction
        // reads this getter and so subscribes to the register directly.
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
}
