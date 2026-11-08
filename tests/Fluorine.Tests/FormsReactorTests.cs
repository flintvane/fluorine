using System;
using System.Globalization;
using System.Windows.Forms;
using Xunit;

namespace Fluorine.Tests
{
    public class FormsReactorTests
    {
        [Fact]
        public void ConstructorWithoutSchedulerThrows()
        {
            Assert.Null(Scheduler.Current);
            Assert.Throws<ReactiveException>(() => new FormsReactor());
        }

        [Fact]
        public void ConvertersRoundTripAndRejectPartialText()
        {
            var culture = CultureInfo.InvariantCulture;

            RoundTrip(TextConverters.String, "Ada", "Ada");
            Assert.Equal("", TextConverters.String.Format(null));
            string parsedText;
            Assert.True(TextConverters.String.TryParse(null, out parsedText));
            Assert.Equal("", parsedText);

            RoundTrip(TextConverters.Boolean, true, "True");
            RoundTrip(TextConverters.Boolean, false, "False");
            bool parsedFlag;
            Assert.True(TextConverters.Boolean.TryParse("true", out parsedFlag));
            Assert.True(parsedFlag);
            Reject(TextConverters.Boolean, "yes");
            Reject(TextConverters.Boolean, "");

            RoundTrip(TextConverters.ByteIn(culture), (byte)255, "255");
            RoundTrip(TextConverters.SByteIn(culture), (sbyte)-12, "-12");
            RoundTrip(TextConverters.Int16In(culture), (short)-32000, "-32000");
            RoundTrip(TextConverters.UInt16In(culture), (ushort)65000, "65000");
            RoundTrip(TextConverters.Int32In(culture), -42, "-42");
            RoundTrip(TextConverters.Int32In(culture), 0, "0");
            RoundTrip(TextConverters.UInt32In(culture), 4000000000u, "4000000000");
            RoundTrip(TextConverters.Int64In(culture), long.MinValue, long.MinValue.ToString(culture));
            RoundTrip(TextConverters.UInt64In(culture), ulong.MaxValue, ulong.MaxValue.ToString(culture));
            RoundTrip(TextConverters.SingleIn(culture), 1.5f, "1.5");
            RoundTrip(TextConverters.DoubleIn(culture), -2.25d, "-2.25");
            RoundTrip(TextConverters.DecimalIn(culture), 12.5m, "12.5");

            int parsed;
            Assert.True(TextConverters.Int32In(culture).TryParse("  8 ", out parsed));
            Assert.Equal(8, parsed);
            Reject(TextConverters.Int32In(culture), "");
            Reject(TextConverters.Int32In(culture), "1a");
            Reject(TextConverters.Int32In(culture), "1-");
            Reject(TextConverters.Int64In(culture), "999999999999999999999");
            Reject(TextConverters.DoubleIn(culture), "x");

            Assert.Equal((-7).ToString(CultureInfo.CurrentCulture), TextConverters.Int32.Format(-7));
            Assert.Throws<ReactiveException>(() => new TextConverter<int>(null, RejectAll));
            Assert.Throws<ReactiveException>(() => TextConverters.Int32In(null));

            bool RejectAll(string text, out int value)
            {
                value = 0;
                return false;
            }
        }

        [Fact]
        public void NullArgumentsThrow()
        {
            using (var host = new WinFormsHost())
            {
                host.OnScheduler(() =>
                {
                    var forms = new FormsReactor();
                    var box = new TextBox();
                    var label = new Label();
                    try
                    {
                        Assert.Throws<ArgumentNullException>(() => forms.BindText(label, (Func<string>)null));
                        Assert.Throws<ArgumentNullException>(() => forms.BindText((Control)null, () => "a"));
                        Assert.Throws<ArgumentNullException>(() => forms.BindText(box, null, value => { }));
                        Assert.Throws<ArgumentNullException>(() => forms.BindText(box, () => "a", null));
                        Assert.Throws<ArgumentNullException>(() => forms.BindText(box, () => 1, value => { }, null));
                        Assert.Throws<ArgumentNullException>(() => forms.BindText<int>(label, null, TextConverters.Int32));
                        Assert.Throws<ArgumentNullException>(() => forms.BindVisible(null, () => true));
                        Assert.Throws<ArgumentNullException>(() => forms.BindVisible(label, null));
                        Assert.Throws<ArgumentNullException>(() => forms.BindEnabled(null, () => true));
                        Assert.Throws<ArgumentNullException>(() => forms.BindEnabled(label, null));
                    }
                    finally
                    {
                        forms.Dispose();
                    }
                });
            }
        }

        [Fact]
        public void OneWayTextFollowsTheModelAndIgnoresEdits()
        {
            using (var host = new WinFormsHost())
            {
                Label label = null;
                TextBox box = null;
                Register<string> name = null;
                Register<int> count = null;
                FormsReactor forms = null;
                host.OnScheduler(() =>
                {
                    label = new Label();
                    box = new TextBox();
                    name = new Register<string>("Ada");
                    count = new Register<int>(4);
                    forms = new FormsReactor();
                    forms.BindText(label, () => name.Value);
                    forms.BindText(box, () => count.Value, TextConverters.Int32In(CultureInfo.InvariantCulture));
                });
                try
                {
                    host.OnScheduler(() =>
                    {
                        Assert.Equal("Ada", label.Text);
                        Assert.Equal("4", box.Text);
                        box.Text = "typed";
                        label.Text = "typed";
                        Assert.Equal("Ada", name.Value);
                        Assert.Equal(4, count.Value);
                    });
                    name.Value = "Grace";
                    count.Value = 9;
                    host.OnScheduler(() =>
                    {
                        Assert.Equal("Grace", label.Text);
                        Assert.Equal("9", box.Text);
                    });
                }
                finally
                {
                    DisposeOn(host, forms);
                }
            }
        }

        [Fact]
        public void TwoWayStringWritesTheModelAndKeepsTheCaret()
        {
            using (var host = new WinFormsHost())
            {
                TextBox box = null;
                Register<string> name = null;
                Register<int> extra = null;
                FormsReactor forms = null;
                var writes = 0;
                host.OnScheduler(() =>
                {
                    box = new TextBox();
                    var handle = box.Handle;
                    Assert.NotEqual(IntPtr.Zero, handle);
                    name = new Register<string>("hello");
                    extra = new Register<int>(0);
                    forms = new FormsReactor();
                    forms.BindText(box, () =>
                    {
                        var ignored = extra.Value;
                        return name.Value;
                    }, value =>
                    {
                        writes++;
                        name.Value = value;
                    });
                });
                try
                {
                    host.OnScheduler(() =>
                    {
                        Assert.Equal("hello", box.Text);
                        Assert.Equal(0, writes);
                        box.SelectionStart = 2;
                        extra.Value = 1;
                    });
                    host.OnScheduler(() =>
                    {
                        Assert.Equal("hello", box.Text);
                        Assert.Equal(2, box.SelectionStart);
                        box.Text = "world";
                        Assert.Equal("world", name.Value);
                        Assert.Equal(1, writes);
                    });
                    host.OnScheduler(() =>
                    {
                        Assert.Equal("world", box.Text);
                        Assert.Equal(1, writes);
                    });
                    name.Value = null;
                    host.OnScheduler(() => Assert.Equal("", box.Text));
                }
                finally
                {
                    DisposeOn(host, forms);
                }
            }
        }

        [Fact]
        public void TwoWayIntNormalizesAndKeepsPartialText()
        {
            using (var host = new WinFormsHost())
            {
                TextBox box = null;
                Label label = null;
                Register<long> number = null;
                FormsReactor forms = null;
                var writes = 0;
                host.OnScheduler(() =>
                {
                    box = new TextBox();
                    label = new Label();
                    number = new Register<long>(12);
                    forms = new FormsReactor();
                    var converter = TextConverters.Int64In(CultureInfo.InvariantCulture);
                    forms.BindText(box, () => number.Value, value =>
                    {
                        writes++;
                        number.Value = value;
                    }, converter);
                    forms.BindText(label, () => number.Value, converter);
                });
                try
                {
                    host.OnScheduler(() =>
                    {
                        Assert.Equal("12", box.Text);
                        Assert.Equal("12", label.Text);
                        Assert.Equal(0, writes);
                        box.Text = "07";
                        Assert.Equal(7L, number.Value);
                        Assert.Equal(1, writes);
                    });
                    host.OnScheduler(() =>
                    {
                        Assert.Equal("7", box.Text);
                        Assert.Equal("7", label.Text);
                        Assert.Equal(1, writes);
                        box.Text = "1-";
                        Assert.Equal(7L, number.Value);
                        Assert.Equal("1-", box.Text);
                        Assert.Equal(1, writes);
                    });
                    number.Value = 30;
                    host.OnScheduler(() =>
                    {
                        Assert.Equal("30", box.Text);
                        Assert.Equal("30", label.Text);
                    });
                }
                finally
                {
                    DisposeOn(host, forms);
                }
            }
        }

        [Fact]
        public void VisibleAndEnabledAreSetOnly()
        {
            using (var host = new WinFormsHost())
            {
                Panel parent = null;
                Panel child = null;
                Button button = null;
                Register<bool> show = null;
                Register<bool> on = null;
                Register<int> noise = null;
                FormsReactor forms = null;
                var visibleChanges = 0;
                var runs = 0;
                host.OnScheduler(() =>
                {
                    parent = new Panel { Visible = false, Enabled = false };
                    child = new Panel();
                    button = new Button();
                    parent.Controls.Add(child);
                    parent.Controls.Add(button);
                    show = new Register<bool>(true);
                    on = new Register<bool>(true);
                    noise = new Register<int>(0);
                    forms = new FormsReactor();
                    child.VisibleChanged += (sender, args) =>
                    {
                        var ignored = noise.Value;
                        visibleChanges++;
                    };
                    forms.BindVisible(child, () =>
                    {
                        runs++;
                        return show.Value;
                    });
                    forms.BindEnabled(button, () => on.Value);
                });
                try
                {
                    host.OnScheduler(() =>
                    {
                        // Parent is hidden and disabled, so the getters disagree with the model.
                        Assert.False(child.Visible);
                        Assert.True(show.Value);
                        Assert.False(button.Enabled);
                        Assert.True(on.Value);
                        Assert.Equal(1, visibleChanges);
                        Assert.Equal(1, runs);
                        noise.Value = 1;
                    });
                    host.OnScheduler(() =>
                    {
                        // The VisibleChanged handler read `noise` during layout. That read
                        // stays untracked, and the same model value does not enter the setter again.
                        Assert.Equal(1, runs);
                        Assert.Equal(1, visibleChanges);
                        parent.Visible = true;
                        parent.Enabled = true;
                        Assert.True(child.Visible);
                        Assert.True(button.Enabled);
                        Assert.True(show.Value);
                        Assert.True(on.Value);
                        show.Value = false;
                        on.Value = false;
                    });
                    host.OnScheduler(() =>
                    {
                        Assert.False(child.Visible);
                        Assert.False(button.Enabled);
                        Assert.False(show.Value);
                        Assert.False(on.Value);
                        Assert.Equal(2, runs);
                    });
                }
                finally
                {
                    DisposeOn(host, forms);
                }
            }
        }

        [Fact]
        public void DisposeStopsBothDirections()
        {
            using (var host = new WinFormsHost())
            {
                TextBox box = null;
                Register<string> name = null;
                FormsReactor forms = null;
                host.OnScheduler(() =>
                {
                    box = new TextBox();
                    name = new Register<string>("Ada");
                    forms = new FormsReactor();
                    forms.BindText(box, () => name.Value, value => name.Value = value);
                });
                host.OnScheduler(() => Assert.Equal("Ada", box.Text));
                host.OnScheduler(() => forms.Dispose());
                name.Value = "Grace";
                host.OnScheduler(() =>
                {
                    Assert.Equal("Ada", box.Text);
                    box.Text = "typed";
                    Assert.Equal("Grace", name.Value);
                });
            }
        }

        static void DisposeOn(WinFormsHost host, FormsReactor forms)
        {
            if (forms == null)
                return;
            host.OnScheduler(() => forms.Dispose());
        }

        static void RoundTrip<T>(TextConverter<T> converter, T value, string text)
        {
            Assert.Equal(text, converter.Format(value));
            T parsed;
            Assert.True(converter.TryParse(text, out parsed));
            Assert.Equal(value, parsed);
        }

        static void Reject<T>(TextConverter<T> converter, string text)
        {
            T parsed;
            Assert.False(converter.TryParse(text, out parsed));
        }
    }
}
