using System;
using System.Windows.Forms;
using Fluorine;

namespace PersonDemo
{
    // Opens the same Person in two frontends.
    //
    // A thread has one scheduler. WPF and WinForms therefore cannot share a
    // thread here: PropertyChanged from AutoNotify is raised on the scheduler
    // where the Person was constructed, and a WPF binding must hear that on
    // the dispatcher thread. The Person is created on a DispatcherScheduler.
    // The WinForms control is created on this thread, after
    // WinFormsScheduler.AttachToCurrent, and its FormsReactor reads the same
    // registers. A write in either window queues the other window's reaction
    // on that window's own scheduler.
    //
    // Closing this WinForms window returns from Application.Run and then shuts
    // the WPF scheduler down. Closing only the WPF window leaves this window
    // running; the Person stays alive with it.
    static class Program
    {
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
    }
}
