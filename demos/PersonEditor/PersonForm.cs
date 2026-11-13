using System;
using System.Drawing;
using System.Linq;
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
    public sealed class PersonForm : Form
    {
        readonly Person _person;
        readonly TextBox _firstName = new TextBox();
        readonly TextBox _lastName = new TextBox();
        readonly TextBox _age = new TextBox();
        readonly Label _status = new Label();
        readonly ListBox _tags = new ListBox();
        readonly TextBox _tagText = new TextBox();
        readonly Button _addTag = new Button();
        readonly Button _removeTag = new Button();
        FormsReactor _forms;

        public PersonForm(Person person)
        {
            WinFormsScheduler.AttachToCurrent();
            _person = person;
            Text = "Person — WinForms";
            _forms= new FormsReactor();
            Font = new Font("Segoe UI", 10);
            ClientSize = new Size(400, 460);
            StartPosition = FormStartPosition.Manual;
            Location = new Point(80, 80);
            BuildLayout();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            _forms.BindText(_firstName, () => _person.FirstName, value => _person.FirstName = value);
            _forms.BindText(_lastName, () => _person.LastName, value => _person.LastName = value);
            _forms.BindText(_age, () => _person.Age, value => _person.Age = value, TextConverters.Int32);
            _forms.BindText(_status, () => _person.AgeStatus);
            _forms.Add(() =>
            {
                var current = _person.Tags.ToList();
                Reaction.Pause(() =>
                {
                    _tags.Items.Clear();
                    foreach (var tag in current)
                        _tags.Items.Add(tag);
                });
            });
            _addTag.Click += AddTag;
            _removeTag.Click += RemoveTag;
        }

        void AddTag(object sender, EventArgs e)
        {
            var text = _tagText.Text;
            if (string.IsNullOrWhiteSpace(text))
                return;
            _person.Tags.Add(text.Trim());
            _tagText.Clear();
        }

        void RemoveTag(object sender, EventArgs e)
        {
            var selected = _tags.SelectedItem as string;
            if (selected != null)
                _person.Tags.Remove(selected);
            else if (!string.IsNullOrWhiteSpace(_tagText.Text))
                _person.Tags.Remove(_tagText.Text.Trim());
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
                RowCount = 7,
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
            _tags.Height = 96;
            AddRow(table, 5, "Tags", _tags);
            _addTag.Text = "Add";
            _addTag.AutoSize = true;
            _removeTag.Text = "Remove";
            _removeTag.AutoSize = true;
            var tagEditor = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                WrapContents = false,
                Margin = new Padding(0, 3, 0, 3)
            };
            _tagText.Width = 160;
            tagEditor.Controls.Add(_tagText);
            tagEditor.Controls.Add(_addTag);
            tagEditor.Controls.Add(_removeTag);
            table.Controls.Add(tagEditor, 1, 6);
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
