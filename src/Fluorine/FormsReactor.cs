using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Fluorine
{
    // Bindings for one WinForms screen. The form creates it on the UI thread,
    // after WinFormsScheduler is attached, and disposes it before the controls.
    //
    // BindText on a Control is one way (a label). BindText on a TextBoxBase
    // with a write function is two way. BindVisible and BindEnabled are set
    // only: their getters report the parents as well as the control, so reading
    // them back would replace the model's desired value with the effective one.
    public class FormsReactor : Reactor
    {
        readonly List<Action> _unhooks = new List<Action>();
        int _unhooked;

        public FormsReactor()
        {
        }

        public FormsReactor(Scheduler scheduler)
            : base(scheduler)
        {
        }

        public void BindText(Control control, Func<string> read)
        {
            if (read == null)
                throw new ReactiveException("BindText requires a read function.");
            BindSet(control, "Text", read, value => control.Text = value);
        }

        public void BindText<T>(Control control, Func<T> read, TextConverter<T> converter)
        {
            if (read == null)
                throw new ReactiveException("BindText requires a read function.");
            if (converter == null)
                throw new ReactiveException("BindText requires a converter.");
            BindText(control, () => converter.Format(read()) ?? "");
        }

        public void BindText(TextBoxBase box, Func<string> read, Action<string> write)
        {
            if (box == null)
                throw new ReactiveException("BindText requires a control.");
            if (read == null)
                throw new ReactiveException("BindText requires a read function.");
            if (write == null)
                throw new ReactiveException("BindText requires a write function.");

            var suppress = 0;
            EventHandler onChanged = (sender, args) =>
            {
                if (suppress != 0 || box.IsDisposed)
                    return;
                write(box.Text);
            };
            Reaction reaction = null;
            EventHandler onDisposed = null;
            Action unhook = () =>
            {
                box.TextChanged -= onChanged;
                box.Disposed -= onDisposed;
            };
            onDisposed = (sender, args) =>
            {
                unhook();
                if (reaction != null)
                    reaction.Dispose();
            };
            box.TextChanged += onChanged;
            box.Disposed += onDisposed;
            Track(unhook);

            // The formatted text is read while recording. The box is touched
            // inside the pause, after that, because setting Text runs layout
            // and TextChanged on this stack (semantics 9.1).
            reaction = TrackValue("Text", () => read() ?? "", value =>
            {
                if (box.IsDisposed)
                    return false;
                // The box already shows this text when the user just typed it.
                // Assigning again moves the caret.
                if (!Equals(box.Text, value))
                {
                    suppress++;
                    try
                    {
                        box.Text = value;
                    }
                    finally
                    {
                        suppress--;
                    }
                }
                return true;
            });
            if (reaction == null)
                unhook();
        }

        public void BindText<T>(TextBoxBase box, Func<T> read, Action<T> write, TextConverter<T> converter)
        {
            if (read == null)
                throw new ReactiveException("BindText requires a read function.");
            if (write == null)
                throw new ReactiveException("BindText requires a write function.");
            if (converter == null)
                throw new ReactiveException("BindText requires a converter.");
            BindText(
                box,
                () => converter.Format(read()) ?? "",
                text =>
                {
                    T value;
                    if (converter.TryParse(text, out value))
                        write(value);
                });
        }

        public void BindVisible(Control control, Func<bool> read)
        {
            BindSet(control, "Visible", read, value => control.Visible = value);
        }

        public void BindEnabled(Control control, Func<bool> read)
        {
            BindSet(control, "Enabled", read, value => control.Enabled = value);
        }

        public override void Dispose()
        {
            Unhook();
            base.Dispose();
        }

        void BindSet<T>(Control control, string name, Func<T> read, Action<T> assign)
        {
            if (control == null)
                throw new ReactiveException("Bind" + name + " requires a control.");
            if (read == null)
                throw new ReactiveException("Bind" + name + " requires a read function.");

            Reaction reaction = null;
            EventHandler onDisposed = null;
            Action unhook = () => control.Disposed -= onDisposed;
            onDisposed = (sender, args) =>
            {
                unhook();
                if (reaction != null)
                    reaction.Dispose();
            };
            control.Disposed += onDisposed;
            Track(unhook);

            reaction = TrackValue(name, read, value =>
            {
                if (control.IsDisposed)
                    return false;
                assign(value);
                return true;
            });
            if (reaction == null)
                unhook();
        }

        // Compares against the last value this binding wrote, then assigns
        // inside a pause. The control getter is the wrong comparison for
        // Visible and Enabled: it folds in the parent, and SetVisibleCore
        // treats that effective value as a change and lays out again.
        Reaction TrackValue<T>(string name, Func<T> read, Func<T, bool> applyInsidePause)
        {
            var has = false;
            var last = default(T);
            return Add(name, () =>
            {
                var value = read();
                if (has && Equals(last, value))
                    return;
                var applied = false;
                Reaction.Pause(() =>
                {
                    applied = applyInsidePause(value);
                });
                if (!applied)
                    return;
                last = value;
                has = true;
            });
        }

        void Track(Action unhook)
        {
            lock (_unhooks)
                _unhooks.Add(unhook);
        }

        void Unhook()
        {
            if (System.Threading.Interlocked.Exchange(ref _unhooked, 1) != 0)
                return;
            Action[] copy;
            lock (_unhooks)
            {
                copy = _unhooks.ToArray();
                _unhooks.Clear();
            }
            for (var i = 0; i < copy.Length; i++)
                copy[i]();
        }
    }
}
