using System;
using System.Reflection;
using System.Threading;
using Xunit;

namespace Fluorine.Acceptance
{
    public sealed class Cs7LoggingTests : AcceptanceTest
    {
        [Fact(DisplayName = "cs7.1-01")]
        public void cs7_1_01()
        {
            var ev = typeof(Reactive).GetEvent("UnhandledException", BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(ev);
            Assert.Equal(typeof(Action<Exception>), ev.EventHandlerType);
            Assert.True(ev.GetAddMethod().IsPublic);
            Assert.True(ev.GetAddMethod().IsStatic);
        }

        [Fact(DisplayName = "cs7.1-02")]
        public void cs7_1_02()
        {
            var s = Harness.Create();
            using (var console = new ConsoleCapture())
            {
                new Reactor(s).Add(() => { throw new InvalidOperationException("console-x"); });
                Harness.Barrier(s);
                Assert.False(string.IsNullOrWhiteSpace(console.Text));
                bool continued = false;
                s.Queue(() => continued = true);
                Harness.Barrier(s);
                Assert.True(continued);
            }
        }

        [Fact(DisplayName = "cs7.1-03")]
        public void cs7_1_03()
        {
            var s = Harness.Create();
            Action<Exception> handler = exception => { throw new ApplicationException("handler-h"); };
            Reactive.UnhandledException += handler;
            try
            {
                using (var console = new ConsoleCapture())
                {
                    new Reactor(s).Add(() => { throw new InvalidOperationException("boom-x"); });
                    Harness.Barrier(s);
                    bool continued = false;
                    s.Queue(() => continued = true);
                    Harness.Barrier(s);
                    string text = console.Text;
                    Assert.True(
                        text.IndexOf("boom-x", StringComparison.Ordinal) >= 0
                        || text.IndexOf("InvalidOperationException", StringComparison.Ordinal) >= 0,
                        "console did not mention X: " + text);
                    Assert.True(
                        text.IndexOf("handler-h", StringComparison.Ordinal) >= 0
                        || text.IndexOf("ApplicationException", StringComparison.Ordinal) >= 0,
                        "console did not mention H: " + text);
                    Assert.True(continued);
                }
            }
            finally
            {
                Reactive.UnhandledException -= handler;
            }
        }

        [Fact(DisplayName = "cs7.1-04")]
        public void cs7_1_04()
        {
            var s = Harness.Create();
            int first = 0;
            int second = 0;
            Action<Exception> a = exception => Interlocked.Increment(ref first);
            Action<Exception> b = exception => Interlocked.Increment(ref second);
            Reactive.UnhandledException += a;
            Reactive.UnhandledException += b;
            try
            {
                new Reactor(s).Add(() => { throw new InvalidOperationException("one"); });
                Harness.Barrier(s);
                Assert.Equal(1, first);
                Assert.Equal(1, second);
            }
            finally
            {
                Reactive.UnhandledException -= a;
                Reactive.UnhandledException -= b;
            }
        }

        [Fact(DisplayName = "cs7.2-01")]
        public void cs7_2_01()
        {
            var empty = new ReactiveException();
            var withMessage = new ReactiveException("m");
            var inner = new InvalidOperationException("inner");
            var withInner = new ReactiveException("m2", inner);
            Assert.IsAssignableFrom<Exception>(empty);
            Assert.Equal("m", withMessage.Message);
            Assert.Equal("m2", withInner.Message);
            Assert.Same(inner, withInner.InnerException);
        }

        [Fact(DisplayName = "cs7.2-02")]
        public void cs7_2_02()
        {
            using (var log = new Log())
            {
                var s = Harness.Create();
                var x = new InvalidOperationException("react-x");
                new Reactor(s).Add(() => { throw x; });
                Harness.Barrier(s);
                var items = log.Snapshot();
                Assert.Single(items);
                Assert.Same(x, items[0]);
            }
        }

        [Fact(DisplayName = "cs7.3-01")]
        public void cs7_3_01()
        {
            LoggedOnceUnwrapped(s => new Reactor(s).Add(() => { throw new InvalidOperationException("reaction"); }));
            LoggedOnceUnwrapped(s => s.Queue(() => { throw new InvalidOperationException("action"); }));
            CreateNewInitialLoggedOnce();
            UsageSilent(() =>
            {
                var done = new ManualResetEvent(false);
                Exception thrown = null;
                var thread = new Thread(() =>
                {
                    try
                    {
                        var reactor = new Reactor();
                        GC.KeepAlive(reactor);
                    }
                    catch (Exception ex)
                    {
                        thrown = ex;
                    }
                    finally
                    {
                        done.Set();
                    }
                });
                thread.IsBackground = true;
                thread.Start();
                Harness.Wait(done, "reactor without scheduler");
                Assert.IsType<ReactiveException>(thrown);
            });
            UsageSilent(() => Assert.Throws<ReactiveException>(() => new Reactor(null)));
            UsageSilent(() =>
            {
                var s = Harness.Create();
                bool caught = false;
                new Reactor(s).Add(() =>
                {
                    try
                    {
                        Reaction.ContinueRecording();
                    }
                    catch (ReactiveException)
                    {
                        caught = true;
                    }
                });
                Harness.Barrier(s);
                Assert.True(caught);
                s.Shutdown();
            });
            UsageSilent(() =>
            {
                var s = Harness.Create();
                bool caught = false;
                new Reactor(s).Add(() =>
                {
                    try
                    {
                        s.Shutdown();
                    }
                    catch (ReactiveException)
                    {
                        caught = true;
                    }
                });
                Harness.Barrier(s);
                Assert.True(caught);
                s.Shutdown();
            });
            UsageSilent(SecondSchedulerType);
            UsageSilent(WinFormsWithoutContext);
            UsageSilent(() =>
            {
                bool initial = false;
                Assert.Throws<ReactiveException>(() => ThreadScheduler.CreateNew(null, () => initial = true));
                Assert.Throws<ReactiveException>(() => ThreadScheduler.CreateNew("", () => initial = true));
                Assert.False(initial);
            });
        }

        static void LoggedOnceUnwrapped(Action<Scheduler> body)
        {
            using (var log = new Log())
            {
                var s = Harness.Create();
                body(s);
                Harness.Barrier(s);
                var items = log.Snapshot();
                Assert.Single(items);
                Assert.IsType<InvalidOperationException>(items[0]);
                s.Shutdown();
            }
        }

        static void CreateNewInitialLoggedOnce()
        {
            using (var log = new Log())
            {
                var x = new InvalidOperationException("initial");
                var s = ThreadScheduler.CreateNew("cs7.3-initial", () => { throw x; });
                Assert.Equal(1, log.Count);
                Assert.Same(x, log.Snapshot()[0]);
                s.Shutdown();
            }
        }

        static void UsageSilent(Action body)
        {
            using (var log = new Log())
            {
                body();
                Assert.Equal(0, log.Count);
            }
        }

        static void SecondSchedulerType()
        {
            using (var forms = WinFormsLoop.Start())
            {
                Exception error = null;
                var done = new ManualResetEvent(false);
                forms.Form.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        DispatcherScheduler.AttachToCurrent();
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                    done.Set();
                }));
                Harness.Wait(done, "second scheduler type");
                Assert.IsType<ReactiveException>(error);
            }
        }

        static void WinFormsWithoutContext()
        {
            Exception error = null;
            var done = new ManualResetEvent(false);
            var thread = new Thread(() =>
            {
                try
                {
                    WinFormsScheduler.AttachToCurrent();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    done.Set();
                }
            });
            thread.IsBackground = true;
            thread.Start();
            Harness.Wait(done, "winforms without context");
            Assert.IsType<ReactiveException>(error);
        }

        [Fact(DisplayName = "cs7.4-01")]
        public void cs7_4_01()
        {
            var s = Harness.Create();
            var reaction = new Reactor(s).Add(() => { });
            Harness.Barrier(s);
            reaction.Trigger();
            Harness.Barrier(s);
            reaction.Trigger();
            Harness.Barrier(s);
            Assert.Equal(3, reaction.ExecutionCount);
        }

        [Fact(DisplayName = "cs7.4-02")]
        public void cs7_4_02()
        {
            var s = Harness.Create();
            var reg = new Register<int>(0);
            var entered = new ManualResetEvent(false);
            var release = new ManualResetEvent(false);
            int runs = 0;
            Reaction reaction = null;
            try
            {
                reaction = new Reactor(s).Add(() =>
                {
                    int n = Interlocked.Increment(ref runs);
                    var unused = reg.Value;
                    if (n == 1)
                    {
                        entered.Set();
                        release.WaitOne();
                    }
                });
                Harness.Wait(entered, "run in progress");
                Exception error = null;
                int sample = 0;
                var reader = new Thread(() =>
                {
                    try
                    {
                        for (int i = 0; i < 100; i++)
                            sample = reaction.ExecutionCount;
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                });
                reader.IsBackground = true;
                reader.Start();
                Assert.True(reader.Join(Harness.TimeoutMs));
                Assert.Null(error);
                GC.KeepAlive(sample);
                release.Set();
                Harness.Barrier(s);

                int sideRuns = 0;
                new Reactor(s).Add(() =>
                {
                    sideRuns++;
                    int count = reaction.ExecutionCount;
                    GC.KeepAlive(count);
                });
                Harness.Barrier(s);
                int before = sideRuns;
                reg.Value = 1;
                Harness.Barrier(s);
                Assert.Equal(before, sideRuns);
            }
            finally
            {
                release.Set();
            }
        }
    }
}
