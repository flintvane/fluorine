using System;
using System.Reflection;
using System.Threading;
using Fluorine;
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
                var wrapped = Assert.IsType<ReactiveException>(items[0]);
                Assert.Same(x, wrapped.InnerException);
            }
        }

        [Fact(DisplayName = "cs7.3-01")]
        public void cs7_3_01()
        {
            ReactionThrewOnce();
            ReactorWithoutSchedulerOnce();
            SecondSchedulerTypeOnce();
            ShutdownInsideReactionOnce();
            NestedRunOnce();
            ContinueRecordingOnce();
        }

        static void ReactionThrewOnce()
        {
            using (var log = new Log())
            {
                var s = Harness.Create();
                new Reactor(s).Add(() => { throw new InvalidOperationException("listed"); });
                Harness.Barrier(s);
                ExactlyOne(log);
                s.Shutdown();
            }
        }

        static void ReactorWithoutSchedulerOnce()
        {
            using (var log = new Log())
            {
                var done = new ManualResetEvent(false);
                var thread = new Thread(() =>
                {
                    try
                    {
                        var reactor = new Reactor();
                        GC.KeepAlive(reactor);
                    }
                    catch (ReactiveException)
                    {
                    }
                    finally
                    {
                        done.Set();
                    }
                });
                thread.IsBackground = true;
                thread.Start();
                Harness.Wait(done, "reactor without scheduler");
                ExactlyOne(log);
            }
        }

        static void SecondSchedulerTypeOnce()
        {
            using (var log = new Log())
            {
                var done = new ManualResetEvent(false);
                var thread = new Thread(() =>
                {
                    try
                    {
                        var scheduler = DispatcherScheduler.AttachToCurrent();
                        try
                        {
                            SynchronizationContext.SetSynchronizationContext(
                                new System.Windows.Forms.WindowsFormsSynchronizationContext());
                            WinFormsScheduler.AttachToCurrent();
                        }
                        catch (ReactiveException)
                        {
                        }
                        scheduler.Shutdown();
                    }
                    finally
                    {
                        done.Set();
                    }
                });
                thread.IsBackground = true;
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                Harness.Wait(done, "second scheduler type");
                ExactlyOne(log);
            }
        }

        static void ShutdownInsideReactionOnce()
        {
            using (var log = new Log())
            {
                var s = Harness.Create();
                new Reactor(s).Add(() =>
                {
                    try
                    {
                        s.Shutdown();
                    }
                    catch (ReactiveException)
                    {
                    }
                });
                Harness.Barrier(s);
                ExactlyOne(log);
                s.Shutdown();
            }
        }

        static void NestedRunOnce()
        {
            using (var log = new Log())
            {
                var scheduler = new ProbeScheduler();
                var reactor = new Reactor(scheduler);
                Reaction other = null;
                int selfRuns = 0;
                var self = reactor.Add(() =>
                {
                    selfRuns++;
                    if (selfRuns == 1)
                        return;
                    try
                    {
                        scheduler.RunNested(other);
                    }
                    catch (ReactiveException)
                    {
                    }
                });
                other = reactor.Add(() => { });
                scheduler.Pump();
                log.Clear();
                self.Trigger();
                scheduler.Pump();
                ExactlyOne(log);
                scheduler.Shutdown();
            }
        }

        static void ContinueRecordingOnce()
        {
            using (var log = new Log())
            {
                var s = Harness.Create();
                new Reactor(s).Add(() => Reaction.ContinueRecording());
                Harness.Barrier(s);
                ExactlyOne(log);
                s.Shutdown();
            }
        }

        static void ExactlyOne(Log log)
        {
            var items = log.Snapshot();
            Assert.Single(items);
            Assert.IsType<ReactiveException>(items[0]);
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

        [Fact(DisplayName = "cs8.1-01")]
        public void cs8_1_01()
        {
            var s = Harness.Create();
            var a = new Register<int>(0);
            var b = new Register<int>(0);
            int runs = 0;
            int continuations = 0;
            var continued = new ManualResetEvent(false);
            new Reactor(s).Add(async () =>
            {
                Interlocked.Increment(ref runs);
                var unusedA = a.Value;
                await System.Threading.Tasks.Task.Delay(50);
                var unusedB = b.Value;
                if (Interlocked.Increment(ref continuations) >= 1)
                    continued.Set();
            });
            Harness.Barrier(s);
            Assert.Equal(1, runs);
            Harness.Wait(continued, "continuation");
            continued.Reset();
            a.Value = 1;
            Harness.Barrier(s);
            Assert.Equal(2, runs);
            Harness.Wait(continued, 5000, "second continuation");
            while (Volatile.Read(ref continuations) < 2)
            {
                if (!continued.WaitOne(Harness.TimeoutMs))
                    break;
            }
            Assert.True(Volatile.Read(ref continuations) >= 2);
            int before = Volatile.Read(ref runs);
            b.Value = 1;
            Harness.Barrier(s);
            Assert.Equal(before, Volatile.Read(ref runs));
        }

        [Fact(DisplayName = "cs8.1-02")]
        public void cs8_1_02()
        {
            var s = Harness.Create();
            Reaction before = null;
            Reaction after = null;
            var done = new ManualResetEvent(false);
            Reaction reaction = null;
            reaction = new Reactor(s).Add(async () =>
            {
                before = Reaction.Current;
                await System.Threading.Tasks.Task.Yield();
                after = Reaction.Current;
                done.Set();
            });
            Harness.Barrier(s);
            Harness.Wait(done, "continuation");
            Assert.Same(reaction, before);
            Assert.Null(after);
        }

        [Fact(DisplayName = "cs8.1-03")]
        public void cs8_1_03()
        {
            using (var log = new Log())
            {
                var s = Harness.Create();
                System.Threading.Tasks.Task work = null;
                var done = new ManualResetEvent(false);
                Func<System.Threading.Tasks.Task> lambda = async () =>
                {
                    try
                    {
                        await System.Threading.Tasks.Task.Yield();
                        throw new InvalidOperationException("after-await");
                    }
                    finally
                    {
                        done.Set();
                    }
                };
                new Reactor(s).Add(() => { work = lambda(); });
                Harness.Barrier(s);
                Harness.Wait(done, "continuation");
                Assert.NotNull(work);
                try
                {
                    work.Wait(Harness.TimeoutMs);
                }
                catch (AggregateException)
                {
                }
                Assert.Equal(0, log.Count);
            }
        }
    }
}
