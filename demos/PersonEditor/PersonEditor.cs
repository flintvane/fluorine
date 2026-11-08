using System;
using System.Drawing;
using System.Windows.Forms;
using Fluorine;

namespace PersonDemo
{
    // A WinForms control that edits one Person. It is the whole WinForms
    // frontend: drop it on a form, and OnLoad binds the boxes.
    //
    // The control does not bind in its constructor. WinFormsScheduler can
    // attach only once this thread has a WinForms synchronization context,
    // and that context is installed when the message loop starts, not when
    // the control is new. OnLoad runs after that. AttachToCurrent returns
    // the scheduler already installed on this thread when one is there.
    //
    // Each BindText adds a reaction on a FormsReactor owned by this control.
    // The read function runs while recording, so the reaction subscribes to
    // whatever Person property it touches. The control assignment happens
    // inside FormsReactor, paused, so layout started by the setter does not
    // join those subscriptions. Two-way boxes also write the model from
    // TextChanged, and they stay quiet while the reaction itself is assigning.
    //
    // Age uses TextConverters.Int32. "07" becomes 7 and the box is rewritten
    // as "7". "1-" does not parse, so Age is left alone and the typed text
    // stays until a later model change replaces it. AgeStatus is one way:
    // the computed reaction on the Person is the only writer.
    //
    // Dispose the FormsReactor before the child controls. The base Dispose
    // disposes the boxes, and a reaction must not assign into a disposed box.
    public sealed class PersonEditor : UserControl
    {
        readonly Person _person;
        readonly TextBox _firstName = new TextBox();
        readonly TextBox _lastName = new TextBox();
        readonly TextBox _age = new TextBox();
        readonly Label _status = new Label();
        FormsReactor _forms;

        public PersonEditor(Person person)
        {
            if (person == null)
                throw new ArgumentNullException("person");
            _person = person;
            BuildLayout();
        }

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

        void BuildLayout()
        {
            var intro = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(360, 0),
                Margin = new Padding(0, 0, 0, 12),
                Text = "This window and the WPF window edit one Person. The bindings below are the frontend."
            };
            _status.AutoSize = true;
            _status.ForeColor = Color.DarkRed;
            _status.Margin = new Padding(0, 8, 0, 0);

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 5,
                Padding = new Padding(12)
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.Controls.Add(intro, 0, 0);
            table.SetColumnSpan(intro, 2);
            AddRow(table, 1, "First name", _firstName);
            AddRow(table, 2, "Last name", _lastName);
            AddRow(table, 3, "Age", _age);
            table.Controls.Add(_status, 0, 4);
            table.SetColumnSpan(_status, 2);
            Controls.Add(table);
        }

        static void AddRow(TableLayoutPanel table, int row, string caption, Control editor)
        {
            var label = new Label
            {
                Text = caption,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 8, 0)
            };
            editor.Dock = DockStyle.Top;
            editor.Margin = new Padding(0, 3, 0, 3);
            table.Controls.Add(label, 0, row);
            table.Controls.Add(editor, 1, row);
        }
    }
}
