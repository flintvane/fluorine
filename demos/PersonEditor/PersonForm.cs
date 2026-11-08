using System.Drawing;
using System.Windows.Forms;

namespace PersonDemo
{
    // Hosts PersonEditor. The control is the frontend; this form only gives
    // it a window and a message loop. Application.Run installs the WinForms
    // synchronization context before the form loads, which is when the
    // control attaches WinFormsScheduler and builds its bindings.
    sealed class PersonForm : Form
    {
        public PersonForm(Person person)
        {
            Text = "Person — WinForms";
            Font = new Font("Segoe UI", 10);
            ClientSize = new Size(400, 280);
            StartPosition = FormStartPosition.Manual;
            Location = new Point(80, 80);
            Controls.Add(new PersonEditor(person) { Dock = DockStyle.Fill });
        }
    }
}
